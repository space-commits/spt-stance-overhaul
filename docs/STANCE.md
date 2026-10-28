# Stance-Overhaul Project Instructions

## Purpose
Implements procedural stance system for SPT — adds animated weapon position/rotation offsets to the player's first-person view. Stances enable tactical variety (high/low ready, active aim/canted, left shoulder, short stock, patrol) with gameplay benefits. Animation is driven by injecting into the game's existing `Spring` system inside `ProceduralWeaponAnimations`.

## Key Features
- **Multiple stance types**: High ready, low ready, active aim (canting), left shoulder, short stock, patrol
- **Curve-driven animation**: Each stance defines Enter/Exit `Vector3Curve` for position and rotation; a `StanceSlot` drives progress 0→1 along the curve
- **Dual-slot blend system**: `StanceState` manages a `_primary` and `_incoming` slot, blending smoothly when transitioning between stances
- **Spring injection**: Clones the game's `HandsPosition`/`HandsRotation` `Spring` objects, patches `Spring.Get`, `Spring.GetRelative`, `Spring.FixedUpdate`, and `Spring.Reset` to inject stance offsets
- **Gameplay benefits**: Per-stance walk speed, sprint accel, ADS speed, and reload speed modifiers applied via `StatModifiers`
- **ADS gating**: `StanceAimHandler` registers a `BoolGateHandle` on `BoolHandlers.CanAim` to optionally block ADS
- **Input pipeline integration**: `InputHookPipeline` registers with `Pipelines.InputVetoPipeline` and `Pipelines.InputOverridePipeline` to intercept EFT input commands
- **Health/hazard awareness**: `HealthConditionForcesLowReady` checks for toxicity, radiation, injuries, overdose, dehydration

## Project Structure
```
Stance-Overhaul/
├── Plugin.cs                        # BepInEx entry point; adds StanceController to player.gameObject
├── PluginConfig.cs                  # Stance system configuration
├── Controllers/
│   └── StanceController.cs          # MonoBehaviour coordinator; owns all handlers, stances, springs
├── Enums/
│   ├── EStanceType.cs               # None, HighReady, LowReady, ActiveAiming, LeftShoulder, ShortStock, PatrolStance, Melee, Mounting, PistolCompressed
│   ├── EStanceState.cs              # Idle, Transitioning, Active (used in older code, superseded by StanceState logic)
│   ├── EBracingDirection.cs         # Bracing directions
│   └── ECurveType.cs                # Enter, Exit (determines which curve a StanceSlot evaluates)
├── Handlers/
│   ├── IControllerHelper.cs         # Interface: RunOnAwake, RunOnDestroy, RunOnUpdate(deltaTime)
│   ├── Aiming/
│   │   ├── StanceAimHandler.cs      # Manages BoolGateHandle on BoolHandlers.CanAim; blocks ADS when needed
│   │   └── AimPIDHandler.cs         # TODO/stub
│   ├── Input/
│   │   ├── StanceInputEvents.cs     # Stance-specific input events (ToggleHighReady, ToggleLowReady, etc.)
│   │   ├── StanceInputHandler.cs    # Subscribes to events, manages _storedStance, ADS restore logic, calls StanceState.RequestStance
│   │   ├── StanceInputListener.cs   # Reads raw Unity Input each frame, raises StanceInputEvents
│   │   └── InputHookPipeline.cs     # Registers with RealismCommonLib Pipelines for input veto/override
│   ├── CollisionHandler.cs          # Collision detection for bracing (in-progress)
│   ├── MountingHandler.cs           # Mounting/bracing surface detection (in-progress)
│   ├── PositionOffsetHandler.cs     # Per-weapon position offset adjustments
│   ├── StanceHealthHandler.cs       # Health/damage interaction (in-progress)
│   ├── StanceMovementHandler.cs     # Registers FloatMultiplierHandles on StatModifiers for walk/sprint speed
│   ├── StanceStaminaHandler.cs      # Stamina cost/recovery per stance (in-progress)
│   └── TacSprintHandler.cs          # Tactical sprint handling
├── Patches/
│   └── StancePatches.cs         # Spring patches (SpringUpdatePatch, SpringGetPatch, SpringGetRelativePatch, SpringResetPatch, UpdateWeaponVariablesPatch, ZeroAdjustmentsPatch, SetFireModePatch, OperateStationaryWeaponPatch)
├── PatchHooks/                      # (empty — future patch hook implementations)
├── SpringAnimators/
│   ├── ISpringAnimator.cs           # Marker interface
│   ├── AdsAnimator.cs               # TODO/stub
│   ├── ExtraDetailsAnimator.cs      # TODO/stub
│   ├── IdleAnimator.cs              # TODO/stub — planned idle animations
│   └── WiggleAnimator.cs            # TODO/stub
├── Stances/
│   ├── IStance.cs                   # Interface: curves, aim speed curves, blend/transition modifiers, OnEnter/OnExit/OnHoldUpdate
│   ├── StanceBase.cs                # Abstract base; subscribes to ReloadEvents, provides virtual override hooks
│   ├── ActiveAim.cs                 # Active aim / canting stance
│   ├── HighReady.cs                 # High ready stance
│   ├── LowReady.cs                  # Low ready stance
│   ├── LeftShoulder.cs              # Left shoulder / blind fire stance
│   ├── ShortStock.cs                # Short stock stance
│   ├── PatrolStance.cs              # Patrol (relaxed carry) stance
│   ├── Melee.cs                     # Melee stance (stub/TODO)
│   └── Mounting.cs                  # Mounting stance (stub/TODO)
└── State/
    ├── StanceState.cs               # Dual-slot animation state machine (primary + incoming)
    └── StanceSlot.cs                # Single stance slot: progress, direction, curve evaluation
```

## Core Systems

### IControllerHelper — Lifecycle Interface
All handlers implement this interface. `StanceController` owns a `List<IControllerHelper>` and drives them all:
- `RunOnAwake()` — called from `StanceController.Awake()`, used to subscribe to events, register modifier handles
- `RunOnDestroy()` — called from `StanceController.OnDestroy()`, used to unsubscribe and unregister handles
- `RunOnUpdate(float deltaTime)` — called every frame from `StanceController.Update()`

> **Important**: Handlers are NOT MonoBehaviours. They are plain C# classes instantiated and driven by `StanceController`.

### StanceController (MonoBehaviour Coordinator)
`StanceController` is a `MonoBehaviour` added to `player.gameObject` on player init. It owns everything:
- **Springs**: Clones `PWA.HandsContainer.HandsPosition` and `PWA.HandsContainer.HandsRotation` via `Cloner.ShallowClone()`, stored as `StancePositionSpring` and `StanceRotationSpring`. The Spring patches inject these into the game's animation system.
- **Stances**: Instantiates and owns all `StanceBase` objects (`PatrolStance`, `LeftShoulder`, `HighReady`, `LowReady`, `ActiveAim`, `ShortStock`)
- **Handlers**: Creates and manages all `IControllerHelper` instances (`StanceState`, `InputHookPipeline`, `StanceMovementHandler`, `StanceAimHandler`, `StanceInputHandler`, `StanceInputListener`)
- **Gameplay state**: Exposes `CurrentStanceType`, `StancePosition`, `StanceRotation`, `PwaAimSpeed` (via reflection into `_aimingSpeed` field), `PwaOriginalAimSpeed`
- **Constants**: ADS speed multipliers, reload speed buffs, weight limits defined as `const float`
- **Reload multiplier handles**: Holds `FloatMultiplierHandle` references for each reload type, registers them via `AssignReloadHandlers()`

### StanceState (Dual-Slot Animation State Machine)
The actual state machine for stance transitions. Not a MonoBehaviour — driven by `StanceController` via `IControllerHelper`.

Uses two slots:
- `_primary`: The currently active or exiting stance
- `_incoming`: The next stance, paused until `_primary` reaches its blend threshold

**Transition logic** in `RequestStance(IStance)`:
1. No active stance → create `_primary` on Enter curve, call `stance.OnEnter()`
2. Same stance as `_primary`, no incoming → toggle exit or reverse direction
3. Same stance as `_incoming` during blend → cancel incoming (BeginExit)
4. Third stance during blend → collapse (drop primary, promote incoming, BeginExit), queue new as incoming
5. Normal A→B → `BeginExit(_primary)`, create `_incoming` paused

**Blend threshold**: `_incoming` is unpaused when `_primary.IsHeadingToIdle && _primary.IdleProximity >= _primary.Stance.BlendIntoThreshold(incomingType)`

**Output**: `StancePosition` and `StanceRotation` — Lerped between slot evaluations and then smoothed with `Mathf.Lerp` using `PluginConfig.test19.Value` as speed.

### StanceSlot (Animation Cursor)
Tracks a single stance's animation progress:
- `Progress` (0..1): Position along the curve
- `Direction` (+1 / -1 / 0): 0 = holding at pose, +1 = forward, -1 = reverse
- `ActiveCurve` (`ECurveType.Enter` or `ECurveType.Exit`): Which curve to evaluate
- `IsAtIdle`, `IsAtPose`, `IsHeadingToIdle`, `IsHeadingToPose`, `IsAtOrHeadingToPose`
- `IdleProximity`: How close to idle (0 = at pose, 1 = at idle)
- `EvaluatePosition()` / `EvaluateRotation()` / `EvaluateAimSpeed()`: Sample the active curve at `Progress`
- `SlotUpdate(deltaTime)`: Advances `Progress` using `TransitionFromModifier` × `TransitionToSpeedModifier` × `PluginConfig.test20.Value`

### IStance / StanceBase (Stance Definition)
`IStance` defines what a stance provides:
- `EnterPositionCurve`, `EnterRotationCurve` — `Vector3Curve` played when entering
- `ExitPositionCurve`, `ExitRotationCurve` — `Vector3Curve` played when exiting
- `EnterAimSpeedCurve`, `ExitAimSpeedCurve` — `AnimationCurve` controlling ADS speed multiplier during transition
- `BlendIntoThreshold(EStanceType nextStance)` — idle proximity at which incoming is unpaused (default 0.5)
- `TransitionFromModifier(EStanceType? previousStance)` — speed modifier based on what stance we came from
- `TransitionToSpeedModifier(EStanceType? nextStance)` — speed modifier based on where we are going
- `OnEnter()`, `OnExit()`, `OnHoldUpdate(float deltaTime)` — lifecycle hooks

`StanceBase` provides:
- Subscribes to `ReloadEvents` in constructor; unsubscribes in `Dispose()`
- Override `OnMagReload()`, `OnCheckAmmo()`, `OnRechamber()`, etc. for per-stance reload behavior

### Spring Injection System
Stance offsets are applied by piggybacking on the game's `Spring` system:

1. `InitSprings()`: `StancePositionSpring = Cloner.ShallowClone(PWA.HandsContainer.HandsPosition)` — shallow clone preserves spring settings but is a separate instance
2. **`SpringUpdatePatch`** (prefix on `Spring.FixedUpdate`): When the game updates `HandsPosition` or `HandsRotation`, also calls `FixedUpdate` on the cloned spring
3. **`SpringResetPatch`** (postfix on `Spring.Reset`): Resets the cloned spring when the game resets
4. **`SpringGetPatch`** (postfix on `Spring.Get`): Adds the cloned spring's value to the result → position/rotation offset applied
5. **`SpringGetRelativePatch`**: Same for `Spring.GetRelative`
6. **`UpdateWeaponVariablesPatch`** (postfix on `PWA.UpdateWeaponVariables`): Captures `_aimingSpeed` into `PwaOriginalAimSpeed` each frame

`StanceState.UpdateAimSpeed()` applies the aim speed multiplier from the active slot's curve to `StanceController.PwaAimSpeed` each frame.

### Input System
**Two-layer input architecture**:

1. **`StanceInputListener`** (reads raw input): Polls `UnityEngine.Input` every frame. Raises `StanceInputEvents` (e.g., `StanceInputEvents.RaiseToggleHighReady()`). Also handles scroll-wheel stance cycling. Blocks input when `AimStateInstance.IsAiming || PlayerStateInstance.IsSprinting || PlayerStateInstance.IsInventoryOpen`.

2. **`StanceInputHandler`** (responds to events): Subscribes to `StanceInputEvents` and `PlayerEvents`/`InputEvents`. Manages `_storedStance` (the stance to restore after ADS), `_wasInterruptedByADS`, `_aimedFromActiveAim`. Calls `_stanceState.RequestStance()` or `_stanceState.CancelAll()`.

3. **`InputHookPipeline`** (intercepts EFT input): Registers with `Pipelines.InputVetoPipeline` to suppress EFT's default fire/scroll commands when appropriate. Registers with `Pipelines.InputOverridePipeline` to override `ECommand.LeftStanceToggle` (routing it through `StanceInputEvents` instead of EFT's default) and optionally `ECommand.WeaponMounting`.

**`StanceInputEvents`** (in `Handlers/Input/`): Stance-specific static events separate from `RealismCommonLib.Events.InputEvents`:
- `ToggleHighReady`, `ToggleLowReady`, `ToggleShortStock`, `ToggleActiveAim`, `TogglePatrolStance`, `ToggleMelee`
- `ToggleOffAllStances`, `OnAttemptedToFireFromStance`

### Handler System (Gameplay Integration)
All handlers implement `IControllerHelper` and are owned by `StanceController`:

| Handler | Purpose |
|---------|---------|
| `StanceMovementHandler` | Registers `FloatMultiplierHandle`s on `StatModifiers.MaxWalkSpeedModifier` and sprint modifiers; updates multipliers each frame based on `CurrentStanceType` |
| `StanceAimHandler` | Registers a `BoolGateHandle` on `BoolHandlers.CanAim`; blocks ADS via NVG/thermal/collision/health conditions |
| `StanceInputHandler` | Manages stored stance, ADS interrupt/restore, firing cancellation logic |
| `StanceInputListener` | Raw input polling and scroll-wheel stance cycle |
| `InputHookPipeline` | Input veto/override registration with RealismCommonLib pipelines |
| `StanceHealthHandler` | (In-progress) Health interaction |
| `StanceStaminaHandler` | (In-progress) Stamina cost per stance |
| `CollisionHandler` | (In-progress) Weapon collision detection for bracing |
| `MountingHandler` | (In-progress) Surface detection for mounting/bracing |
| `PositionOffsetHandler` | Per-weapon-ID position offset adjustments |
| `TacSprintHandler` | Tactical sprint acceleration handling |

## Integration with RealismCommonLib

### Events Subscribed (by StanceInputHandler)
- `PlayerEvents.OnWeaponDraw` → cancel stances on weapon swap
- `PlayerEvents.AimStateChanged` → interrupt/restore stance on ADS toggle
- `PlayerEvents.OnShotFired` → cancel stance or keep based on `RememberStanceFiring` config

### Events Subscribed (by StanceBase)
- All `ReloadEvents` (`WeaponStateReset`, `CheckAmmo`, `ChamberCheck`, `MagReload`, etc.)
- Each stance can override the virtual handler methods for per-stance reload behavior

### State Accessed
```csharp
using static RealismCommonLib.Plugin;

bool isAiming      = AimStateInstance.IsAiming;
bool canAim        = AimStateInstance.PlayerCanAim;
bool isSprinting   = PlayerStateInstance.IsSprinting;
bool weaponReady   = PlayerStateInstance.WeaponIsReady;
bool treatAsPistol = WeaponStateInstance.TreatAsPistol;
float weaponWeight = WeaponStateInstance.TotalWeaponWeight;
bool armsInjured   = HealthStateInstance.ArmsAreIncapacitated;
float toxicity     = HazardsStateInstance.TotalToxicity;
```

### Data Provided to Other Modules
`Plugin.Awake` registers `Integration/StanceInfoProvider` as the common lib's `IStanceInfo` (`ModuleServices.Stance`). Read by FOV-Fix's `LerpCameraPatch`.
- `CanMoveGunToCamera` → `StanceController.CanMoveGunToCamera` (alt aim toggle && !mounting && !stationary weapon)
- `IsLeftShoulder` → `StanceController.CurrentStanceType == EStanceType.LeftShoulder`

The provider returns `false` when `StanceControllerInstance` is destroyed (Unity null check) or hasn't finished `Awake`.

### Modifiers Applied
- `StatModifiers.MaxWalkSpeedModifier` — walk speed per stance
- `StatModifiers.MaxSprintSpeedModifier`, `SprintAccelModifier`, `PreSprintAccelModifier` — sprint per stance
- `StatModifiers.MagReloadSpeed`, `QuickMagReloadSpeed`, `CheckAmmoSpeed`, etc. — registered in `StanceController.AssignReloadHandlers()`
- `BoolHandlers.CanAim` — gate handle managed by `StanceAimHandler`

### Patches (in StancePatches.cs)
- `UpdateWeaponVariablesPatch`: Captures `_aimingSpeed` from PWA on each frame
- `SpringUpdatePatch`: Drives cloned springs in `FixedUpdate`
- `SpringResetPatch`: Resets cloned springs
- `SpringGetPatch`: Adds stance offset to `Spring.Get()` result
- `SpringGetRelativePatch`: Adds stance offset to `Spring.GetRelative()` result
- `ZeroAdjustmentsPatch`: Zero adjustment handling
- `SetFireModePatch`, `OperateStationaryWeaponPatch`: Stance interaction with fire mode and stationary weapons

## Common Development Tasks
1. **Add new stance**: Create class inheriting `StanceBase` in `Stances/`, define `EnterPositionCurve`/`EnterRotationCurve`/`ExitPositionCurve`/`ExitRotationCurve`, override `StanceType`, add property + `InitStance(() => new MyStance())` in `StanceController.InitStances()`
2. **Add per-stance reload modifier**: Add a `FloatMultiplierHandle` field in `StanceController`, register it in `AssignReloadHandlers()`, update its `.Multiplier` based on `CurrentStanceType` on reload events
3. **Add stance gameplay handler**: Create class implementing `IControllerHelper` in `Handlers/`, instantiate via `InitStateController()` in `StanceController.InitStateControllers()`
4. **Add config option**: Add `ConfigEntry<T>` in `PluginConfig.cs`, reference via `PluginConfig.Name.Value`
5. **Debug stance transitions**: Log `_stanceState.ActiveStanceType`, check `_primary`/`_incoming` slot states, `IdleProximity`, `Direction`, `IsHeadingToIdle`
6. **Tweak animation feel**: Adjust `StancePositionSpring.ReturnSpeed`, `StancePositionSpring.Damping` (set from config in `StanceController.Update()`), or modify the `Vector3Curve` keyframes in the stance class
7. **Block ADS**: Get a `BoolGateHandle` from `BoolHandlers.CanAim.Add(true)`, set `.Allowed = false` to block

## State Machine Detail (StanceState)

```
No stance:
  RequestStance(A)  →  _primary = new StanceSlot(A, Enter, progress=0, dir=+1)
                        A.OnEnter()

Single stance holding:
  RequestStance(A)  →  switch to Exit curve, progress=0, dir=+1
                        A.OnExit()

Single stance heading to pose:
  RequestStance(A)  →  reverse Direction
                        A.OnExit()

Single stance heading to idle:
  RequestStance(A)  →  reverse Direction (heading back to pose)

Transition A→B (primary=A, no incoming):
  BeginExit(A)  →  A heads to idle
  _incoming = new StanceSlot(B, Enter, paused=true)
  B.OnEnter()
  [wait until A.IdleProximity >= A.BlendIntoThreshold(B)]
  _incoming unpaused → B begins entering
  [A reaches idle]  →  _primary = null, B promoted to primary

Third stance during blend (primary=A, incoming=B):
  _primary = B (incoming promoted), BeginExit(B)
  _incoming = new StanceSlot(C, Enter, paused=true)
  C.OnEnter()

CancelAll():
  BeginExit(_primary)
  If _incoming is paused → discard it
  If _incoming is active → BeginExit(_incoming)
```

## Stance Constants (StanceController)
```
STANCE_WEIGHT_LIMIT_KG = 8f        // "Chonker" threshold — heavy weapon modifier
IDLE_ADS_MULTI         = 1.5f      // ADS speed when no stance active
ACTIVE_AIM_ADS_MULTI   = 1.35f
HIGH_ADS_MULTI         = 1.25f
LOW_ADS_MULTI          = 1.25f
SHORT_STOCK_ADS_MULTI  = 0.9f
LEFT_SHOULDER_ADS_MULTI= 0.85f
PATROL_ADS_MULTI       = 0.9f
LEFT_SHOULDER_SWAY_MULTI = 1.3f
HIGH_READY_RELOAD_SPEED_BUFF      = 1.18f
ACTIVE_AIM_RELOAD_SPEED_BUFF      = 1.16f
LOW_READY_RELOAD_SPEED_BUFF       = 1.21f
ACTIVE_AIM_RECHAMBER_SPEED_BUFF   = 1.11f
HIGH_READY_RECHAMBER_SPEED_BUFF   = 1.15f
HIGH_READY_CHECK_AMMO_SPEED_BUFF  = 1.15f
```

## Known Limitations & TODOs
- Several `TODO` comments throughout for refactoring into stance classes or dedicated controllers
- `SpringAnimators/` classes (`AdsAnimator`, `WiggleAnimator`, `IdleAnimator`, `ExtraDetailsAnimator`) are stubs
- `PatchHooks/` is currently empty
- ADS speed modifier per stance (commented-out `StanceADSSpeedMulti` in `StanceAimHandler`) not yet implemented
- `EStanceState` enum exists but the state machine has moved into `StanceState` / `StanceSlot` logic
- `Melee` and `Mounting` stances are stubs
- Forced low ready / health-based stance forcing is TODO
- Bracing/collision/mounting handlers are in-progress
