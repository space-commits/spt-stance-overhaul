# Stance-Overhaul Project Instructions

## Purpose
Implements procedural stance system for SPT — adds animated weapon position/rotation offsets to the player's first-person view. Stances enable tactical variety (high/low ready, active aim/canted, left shoulder, short stock, patrol) with gameplay benefits. Animation is driven by injecting into the game's existing `Spring` system inside `ProceduralWeaponAnimations`.

## Key Features
- **Multiple stance types**: High ready, low ready, active aim (canting), left shoulder, short stock, patrol
- **Curve-driven animation**: Each stance defines Enter/Exit `Vector3Curve` for position and rotation; a `StanceSlot` drives progress 0→1 along the curve
- **Dual-slot blend system**: `StanceState` manages a `_primary` and `_incoming` slot, blending smoothly when transitioning between stances
- **Spring injection**: Clones the game's `HandsPosition`/`HandsRotation` `Spring` objects, wraps them in `InterpolatedSpring`, and patches `Spring.Get`, `Spring.FixedUpdate`, and `Spring.Reset` to inject stance offsets
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
│   │   ├── StanceInputHandler.cs    # Subscribes to events, manages _chosen, ADS restore logic, calls StanceState.RequestStance
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
│   ├── CameraDeltaTimePatch.cs  # Gives LerpCamera a frame-rate-independent delta time
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
- `PrimarySlot` (`_primarySlot`): The currently active or exiting stance. Setting it raises `StanceEvents.OnPrimaryStanceChanged`
- `_incoming`: The next stance, paused (`_incomingPaused`) until the primary passes the blend threshold. After a cancel, it can also be an incoming that is fading out

`RequestStance` never toggles a stance off; switching off is `CancelAll()`. `StanceInputHandler.Reconcile()` is the only caller of either.

**Transition logic** in `RequestStance(IStance)`, checked in this order:
1. No slots → create the primary on its Enter curve, call `stance.OnEnter()`
2. Same stance as the primary, with no incoming **or an incoming that is fading out** → if the primary is heading to idle, reverse it towards its pose (`OnEnter`). Otherwise nothing. A fading incoming is left to finish fading
3. Same stance as the incoming → if it's fading out after a cancel, reverse it back towards its pose (`OnEnter`). Otherwise it's already on its way in, so nothing
4. A third stance while an incoming exists → collapse: the old primary is **dropped**, the incoming becomes primary and starts its exit (`BeginExit`), and the new stance is queued as a paused incoming. The dropped primary's share of the pose disappears in one frame (investigation H3, not fixed)
5. Normal A→B → `BeginExit(primary)`, then create B as a paused incoming (`OnEnter`)

**`BeginExit(slot, transition)`**:
- Already heading to idle → only updates `Transition` if it targets another stance, so a stance queued behind it can slow the exit, but a cancel can't speed it back up. Then returns
- Holding (`Direction == 0`) → switch to the Exit curve at progress 0, `Direction = +1`
- Heading to pose → reverse on the same curve
- Calls `OnExit()` in the last two cases

**`CancelAll()`**: `BeginExit` on the primary. A paused incoming has never been drawn, so it is discarded (`OnExit`). An incoming that is blending gets `BeginExit` and fades out from where it is, instead of losing its weight in one frame.

**Per-frame update** (`UpdateStanceState`):
1. Update the primary
2. Unpause the incoming once `PrimarySlot.IsHeadingToIdle && PrimarySlot.IdleProximity >= _incoming.Stance.BlendIntoThreshold(PrimarySlot.Stance.StanceType)`. The method is called on the **incoming** stance with the **outgoing** stance's type, so a stance's "Blend Threshold: X" setting controls the X → this stance transition
3. Update the incoming if it isn't paused
4. Cleanup: remove an incoming that is at idle (faded out) **first**. Then remove a primary at idle, **unless the incoming is still blending** (unpaused and not at its pose). An idle primary contributes nothing to the pose, so keeping it lets the cross-fade finish as `Lerp(0, I(w), w)` instead of the incoming suddenly being drawn at full weight
5. Promote the incoming if there's no primary

**Output**: `StancePosition` and `StanceRotation`. While both slots are drawn, the result is `Lerp(primary, incoming, _incoming.DistanceToPose)`. That's `DistanceToPose`, not `Progress`, because a cancelled incoming can be on its Exit curve, where `Progress` counts up as it leaves. Each slot is scaled by its `AimPositionScale`/`AimRotationScale` (faded with ADS) before blending. The result is then smoothed at `StanceBlendSpeed`, and enters the game through `StancePositionSpring`/`StanceRotationSpring`.

**Queries**:
- `ActiveStance`: the incoming if it's unpaused and heading to or at its pose, else the primary if it's heading to or at its pose, else null. Returns **null** while the primary is exiting and the incoming is still paused, or while both are fading out. `StanceInputHandler.ToggleStance` uses it to decide on/off, so a key press in that window counts as "on" (see the investigation's key-clash repro)
- `GetPoseWeight(type)`: how much of a stance's pose is applied (0–1), using the same blend weight as the output
- `IsTransitioning`, `IsTransitioningBetweenStances` (both slots drawn and the primary not yet idle)

### StanceSlot (Animation Cursor)
Tracks a single stance's animation progress:
- `Progress` (0..1): Position along the curve
- `Direction` (+1 / -1 / 0): 0 = holding at pose, +1 = forward, -1 = reverse
- `ActiveCurve` (`ECurveType.Enter` or `ECurveType.Exit`): Which curve to evaluate
- `IsAtIdle`, `IsAtPose`, `IsHeadingToIdle`, `IsHeadingToPose`, `IsAtOrHeadingToPose`
- `IdleProximity`: How close to idle (0 = at pose, 1 = at idle). On the Enter curve this is `1 − Progress`, on the Exit curve it's `Progress`
- `DistanceToPose`: `1 − IdleProximity`, i.e. how much of the pose is applied, whichever curve is active. `StanceState` uses this as the blend weight
- `MovementProgress`: 0 = start of the current movement, 1 = reached idle or pose
- `Transition` (`StanceTransitionContext` From/To): read every frame for the speed, so changing it changes a moving slot's speed
- `EvaluatePosition()` / `EvaluateRotation()` / `EvaluateAimSpeed()` / `EvaluateAltAimSpeed()` / `EvaluateAltAimReturnSpeed()`: Sample the active curve at `Progress` (position and rotation) or `MovementProgress` (speed curves)
- `StanceSlotUpdate(deltaTime)`: Advances `Progress` by `Direction × dt × speed`, where speed = `TransitionFromModifier(From)` × `TransitionToSpeedModifier(To)` × `GlobalStanceSpeed`, then weapon-weight scaled by `StatsSystem.GetStanceSpeedModifier` and clamped to `[1, speed]`. Because of the clamp, a speed below 1 always runs at 1. Sets `Direction = 0` (holding) on reaching the pose; a slot reaching idle is removed by `StanceState`

### IStance / StanceBase (Stance Definition)
`IStance` defines what a stance provides:
- `EnterPositionCurve`, `EnterRotationCurve` — `Vector3Curve` played when entering
- `ExitPositionCurve`, `ExitRotationCurve` — `Vector3Curve` played when exiting
- `AimSpeedCurve` — `AnimationCurve` multiplier on EFT's aim speed (`_aimingSpeed`) while aiming during a transition, evaluated at `StanceSlot.MovementProgress` (0 = start of the current movement, 1 = idle or pose). Eases vanilla camera-to-sights only
- `AltAimSpeedCurve` — `AnimationCurve` multiplier on alt aim (gun-to-camera) speed, same timing and conditions as `AimSpeedCurve`. Named `<stance>_alt_aim_speed` in `curves.json`
- `AltAimReturnSpeedCurve` — `AnimationCurve` multiplier on alt aim speed out of ADS, at the same `MovementProgress`. `StanceBase` uses `alt_aim_return_speed` for all stances, `PistolCompress` overrides it with `pistol_compress_alt_aim_return_speed`. With no active slot the multiplier is 1. Keep the end value above 0, since a stance held through ADS uses it for the whole return
- `BlendIntoThreshold(EStanceType)` — idle proximity the outgoing stance must reach before this stance (as the incoming) starts. Despite the parameter name `nextStance`, it is called on the **incoming** stance with the **outgoing** stance's type (default 0.5)
- `TransitionFromModifier(EStanceType? previousStance)` — speed modifier based on what stance we came from
- `TransitionToSpeedModifier(EStanceType? nextStance)` — speed modifier based on where we are going
- `OnEnter()`, `OnExit()`, `OnHoldUpdate(float deltaTime)` — lifecycle hooks

`StanceBase` provides:
- Subscribes to `ReloadEvents` in constructor; unsubscribes in `Dispose()`
- Override `OnMagReload()`, `OnCheckAmmo()`, `OnRechamber()`, etc. for per-stance reload behavior

### Spring Injection System
Stance offsets are applied by piggybacking on the game's `Spring` system:

1. `InitSprings()`: `StancePositionSpring = new InterpolatedSpring(Cloner.ShallowClone(PWA.HandsContainer.HandsPosition))` — shallow clone preserves spring settings but is a separate instance. The wrapper interpolates reads between the last two tick states, so its output changes every frame above 60 fps
2. **`SpringUpdatePatch`** (prefix on `Spring.FixedUpdate`): When the game updates `HandsPosition` or `HandsRotation`, also calls `FixedUpdate` on the cloned spring
3. **`SpringResetPatch`** (postfix on `Spring.Reset`): Resets the cloned spring when the game resets
4. **`SpringGetPatch`** (postfix on `Spring.Get`): Adds the cloned spring's value to the result → position/rotation offset applied
5. **`SpringGetRelativePatch`**: Same for `Spring.GetRelative`. Currently not registered in `Plugin.cs`
6. **`UpdateWeaponVariablesPatch`** (postfix on `PWA.UpdateWeaponVariables`): Captures `_aimingSpeed` into `PwaOriginalAimSpeed` each frame

`StanceState.UpdateAimSpeed()` applies the aim speed multiplier from the active slot's curve to `StanceController.PwaAimSpeed` each frame.

### Alt Aim Speed
`StanceAimSystem.MoveGunToCamera` eases the gun offset toward its target at:

```
speed = ADS speed × firing modifier × PwaOriginalAimSpeed × AltAimSpeedModifier
```

- **ADS speed**: chosen by direction and `WeaponStateInstance.TreatAsPistol`.

| | Entering ADS | Leaving ADS |
|---|---|---|
| Pistol | `PistolAdsInSpeed` | `PistolAdsOutSpeed` |
| Everything else | `AdsInSpeed` | `AdsOutSpeed` |

- **Firing modifier**: `AdsInSpeedRecoilModifier` or `AdsOutSpeedRecoilModifer` while firing, otherwise 1.
- **`AltAimSpeedModifier`**: the active slot's `AltAimSpeedCurve` while aiming, or its `AltAimReturnSpeedCurve` when not aiming. It's 1 with no slot, so a plain ADS exit with no stance to restore isn't affected.

Two things keep the curve applied from the first frame after ADS ends:
- `AltAimSpeedModifier` is evaluated when `MoveGunToCamera` reads it, in the `IkProcess` prefix. It isn't cached in `Update`, because `IsAiming` can change between the two.
- `StanceInputHandler.OnAimStateChanged` restores the stance right away, so its slot already exists. The exception is ADS ending during a stance cross-fade, which waits `RestoreAfterADSDelay` (0.05 s). Slots exist during that wait, so the modifier doesn't fall back to 1.

Reversing a stance that's still exiting (a short ADS) also happens right away, so `MovementProgress` is correct from the first frame.

**When tuning curves:**
- A very low stance speed doesn't freeze the stance. `StanceSlot.StanceSlotUpdate` clamps the result to at least 1, so a transition never takes longer than about 1 second, and curve time T is then about T seconds.
- Short ADS taps read the curve partway along, because the exit is reversed rather than restarted. Hold ADS until the stance has fully exited to see a curve from its start.
- Slot progress can still jump mid-return: a paused incoming stance starting, or a slot finishing, which takes the modifier back to 1. Flat curves hide this.
- Since 2026-11-05, a cancelled incoming fades out instead of being dropped, and while it fades it is still the slot the speed curves read (`AimSpeedSlot`), at its `MovementProgress` towards idle. An idle primary kept for the end of a cross-fade is never read, because the incoming takes precedence.

Because the restore runs immediately, a same-frame `IsAiming` flicker, or a forced ADS exit followed by a reload that pauses the stance, starts and cancels the stance at progress 0. That shouldn't show on screen, but `OnEnter`/`OnExit` still fire, and pistol Patrol's left-hand change reacts to them.

### Input System
**Two-layer input architecture**:

1. **`StanceInputListener`** (reads raw input): Polls `UnityEngine.Input` every frame. Raises `StanceInputEvents` (e.g., `StanceInputEvents.RaiseToggleHighReady()`). Also handles scroll-wheel stance cycling. Blocks input when `AimStateInstance.IsAiming || PlayerStateInstance.IsSprinting || PlayerStateInstance.IsInventoryOpen`.

2. **`StanceInputHandler`** (responds to events): Subscribes to `StanceInputEvents` and `PlayerEvents`/`InputEvents`. Holds the player's intent in `_chosen` (plus the default stance, Pistol Compress for pistols) and what's suppressing it (`_isAiming`, `_reloadPausesStance`, `_itemInHand`). `Reconcile()` makes `StanceState` match that, through `RequestStance()` or `CancelAll()`. ADS start and normal ADS end both reconcile immediately; see [Alt Aim Speed](#alt-aim-speed) for why.

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
| `StanceInputHandler` | Manages the chosen stance, ADS interrupt/restore, firing cancellation logic |
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

### Patches
In `CameraDeltaTimePatch.cs`:
- `CameraDeltaTimePatch`: Replaces the `dt` passed to `LerpCamera` so the camera moves evenly and ADS speed matches vanilla at 60 fps, at any frame rate

In `StancePatches.cs`:
- `UpdateWeaponVariablesPatch`: Captures `_aimingSpeed` from PWA on each frame
- `SpringUpdatePatch`: Drives cloned springs in `FixedUpdate`
- `SpringResetPatch`: Resets cloned springs
- `SpringGetPatch`: Adds stance offset to `Spring.Get()` result
- `SpringGetRelativePatch`: Adds stance offset to `Spring.GetRelative()` result (not registered in `Plugin.cs`)
- `ZeroAdjustmentsPatch`: Zero adjustment handling
- `SetFireModePatch`, `OperateStationaryWeaponPatch`: Stance interaction with fire mode and stationary weapons

## Common Development Tasks
1. **Add new stance**: Create class inheriting `StanceBase` in `Stances/`, define `EnterPositionCurve`/`EnterRotationCurve`/`ExitPositionCurve`/`ExitRotationCurve`, override `StanceType`, add property + `InitStance(() => new MyStance())` in `StanceController.InitStances()`
2. **Add per-stance reload modifier**: Add a `FloatMultiplierHandle` field in `StanceController`, register it in `AssignReloadHandlers()`, update its `.Multiplier` based on `CurrentStanceType` on reload events
3. **Add stance gameplay handler**: Create class implementing `IControllerHelper` in `Handlers/`, instantiate via `InitStateController()` in `StanceController.InitStateControllers()`
4. **Add config option**: Add `ConfigEntry<T>` in `PluginConfig.cs`, reference via `PluginConfig.Name.Value`
5. **Debug stance transitions**: Log `_stanceState.ActiveStanceType`, check `PrimarySlot`/`_incoming` slot states (`_incomingPaused`, `ActiveCurveType`, `Progress`, `Direction`, `Transition`), `IdleProximity`, `DistanceToPose`, `IsHeadingToIdle`
6. **Tweak animation feel**: Adjust `StancePositionSpring.ReturnSpeed`, `StancePositionSpring.Damping` (set from config in `StanceController.Update()`), or modify the `Vector3Curve` keyframes in the stance class
7. **Block ADS**: Get a `BoolGateHandle` from `BoolHandlers.CanAim.Add(true)`, set `.Allowed = false` to block

## State Machine Detail (StanceState)

```
No stance:
  RequestStance(A)  →  primary = new StanceSlot(A, Enter, progress=0, dir=+1)
                        A.OnEnter()

Single stance A holding / heading to pose:
  RequestStance(A)  →  nothing (already active)
  CancelAll()       →  holding: switch to Exit curve, progress=0, dir=+1
                       heading to pose: reverse Direction
                       A.OnExit()

Single stance A heading to idle:
  RequestStance(A)  →  reverse Direction (back to pose), A.OnEnter()
  CancelAll()       →  nothing (BeginExit early-returns)

Transition A→B (primary=A, no incoming):
  BeginExit(A)  →  A heads to idle, Transition = A→B (may slow A's exit)
  incoming = new StanceSlot(B, Enter, paused=true), B.OnEnter()
  [wait until A.IdleProximity >= B.BlendIntoThreshold(A)]
  incoming unpaused → B enters, output = Lerp(A, B, B.DistanceToPose)
  [A reaches idle while B still blending]  →  A kept (draws nothing)
  [B reaches pose]  →  primary = null, B promoted to primary

CancelAll() during A→B:
  BeginExit(A)  →  already heading to idle, early return
  B paused   →  discarded, B.OnExit()
  B blending →  BeginExit(B): B fades out from its current weight, B.OnExit()
  [B reaches idle]  →  incoming = null, then A removed once idle

Re-request while B is fading out (after CancelAll):
  RequestStance(B)  →  B reversed back to its pose, B.OnEnter()
  RequestStance(A)  →  A reversed back to its pose, A.OnEnter(); B keeps fading

Third stance C while incoming B exists (and isn't B or a fade-out case above):
  primary = B (A dropped, its pose share vanishes in one frame), BeginExit(B)
  incoming = new StanceSlot(C, Enter, paused=true), C.OnEnter()
```

Known gaps: see `docs/implementation-plans/Stance-Transition-Speedup-Investigation.md`. Collapse (H3) and `ActiveStance` returning null in the in-between states are still open.

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
