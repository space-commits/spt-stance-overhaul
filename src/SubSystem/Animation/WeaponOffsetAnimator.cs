using EFT.Animations;
using EFT;
using EFT.InventoryLogic;
using EFT.CameraControl;
using UnityEngine;
using System.Collections.Generic;
using StanceOverhaul.Enums;
using StanceOverhaul.Events;
using AnimationSystem;
using HarmonyLib;
using static StanceOverhaul.Plugin;
using static RealismCommonLib.Plugin;

namespace StanceOverhaul.SubSystem.Animator
{
    internal class WeaponOffsetAnimator : ISubSystem
    {
        private const float StanceslessDamping = 0.875f;
        private const float StanceslessReturnSpeed = 0.07f;
        // eases the base offset in/out (weapon ready, stationary weapon) without a spring: no overshoot, no 2x rest value
        private const float BaseOffsetEaseSpeed = 8f;
        private const float BaseOffsetSnapThresholdSqr = 1e-10f;
        private const float ExtraRotSmoothSpeed = 10f;
        private const float ExtraPosSmoothSpeed = 7f;

        private Vector3 _targetPosOffest = Vector3.zero;
        private Vector3 _targetRotOffest = Vector3.zero;
        // Offset for this specific weapon from the table below, looked up once per equip
        private Vector3 _weaponSpecificOffset = Vector3.zero;


        private Vector3 _originalPos = Vector3.zero;

        /// <summary>
        /// The base offset for weapon root position + weapon-specific base offsets if present. Used as the baseline for weapon offsets.
        /// </summary>
        public Vector3 BaseWeaponOffsetPosition
        {
            get
            {
                bool blockOffset = !PlayerStateInstance.WeaponIsReady ||
                    PlayerStateInstance.IsUsingStationaryWeapon;

                if (blockOffset)
                    return Vector3.zero;

                // config and pistol handling are read here, not cached per equip, so a config change or a stock change applies straight away
                Vector3 configOffset = WeaponStateInstance.TreatAsPistol ?
                    new Vector3(PluginConfig.PistolOffsetX.Value, PluginConfig.PistolOffsetY.Value, PluginConfig.PistolOffsetZ.Value) :
                    new Vector3(PluginConfig.RifleOffsetX.Value, PluginConfig.RifleOffsetY.Value, PluginConfig.RifleOffsetZ.Value);

                return configOffset + _weaponSpecificOffset;
            }

        }

        /// <summary>
        /// BaseWeaponOffsetPosition eased over time, so it doesn't pop when it switches on or off. This is what gets applied to the weapon.
        /// </summary>
        public Vector3 EasedBaseWeaponOffsetPosition { get; private set; } = Vector3.zero;

        public Vector3 DetailsPositionOffset { get; private set; } = Vector3.zero;
        public Vector3 DetailsRotationOffset { get; private set; } = Vector3.zero;

        // EFT ends a reload operation at a gameplay event (mag seated, round chambered...) while the hands clip keeps playing,
        // so the reload offset follows the hands animator instead: it holds the reload type until layer 1 is back in idle.
        // See Realism docs/Assembly/Hands-Animation-Completion.md.
        private const int HandsLayer = FirearmsAnimator.HANDS_LAYER_INDEX;
        // the hands get this long in idle to start the animation, otherwise the action never started (e.g. check ammo with no mag)
        private const float HandsStartGrace = 0.6f;
        // idle this long before letting go, so a pass through idle partway through a reload doesn't drop the offset
        private const float HandsIdleConfirm = 0.1f;
        private const float ReloadLatchTimeout = 15f;

        private EStanceReloadType _latchedReloadType = EStanceReloadType.None;
        private float _latchStartTime;
        private int _latchStartStateHash;
        private bool _handsStarted;
        private float _handsIdleTime;
        // 1 while the reload animation is visibly playing, falls to 0 as the hands get back to their idle pose
        private float _reloadWeight;

        // Clips can hold a settled pose for their last 0.1-0.3 s before the animator state ends, so the pose decides the visual end:
        // each hand IK marker's distance to its grip marker, and the weapon bone's rotation in WeaponRootAnim, against the idle pose.
        // Same 10 cm range vanilla IkProcess uses for GripWeight.
        private const float HandTolerance = 0.005f;
        private const float HandReturnRange = 0.1f;
        // end-of-reload clips sit ~2 deg off idle until they cut to IDLE
        private const float WeaponAngleTolerance = 3f;
        private const float WeaponReturnAngle = 15f;

        private bool _hasIdlePose;
        private float _idleLeftDist;
        private float _idleRightDist;
        private Quaternion _idleWeaponRot;

        // Player._markers (animated weapon_L/R_IK_marker) and Player._gripReferences (weapon_L/R_hand_marker)
        private static AccessTools.FieldRef<Player, Transform[]>? _markersRef;
        private static AccessTools.FieldRef<Player, Transform[]>? _gripRefsRef;
        private static bool _poseReadFailed;

        //TODO: this needs to move to FOV Fix and/or JSON file, or wherever weapon POS will be handled (common lib?)
        //Common lib can have functionality for it, but modules need to apply offsets themeselves
        //Common lib can then surface the starting weapon positions for PID
        public Vector3? GetWeaponOffset(string? weaponId)
        {
            if (weaponId == null)
                return null;

            if (_baseWeaponOffsets.TryGetValue(weaponId, out var offset))
            {
                return offset;
            }
            return null;
        }

        //TODO: get from JSON data that users can edit/version
        private Dictionary<string, Vector3> _baseWeaponOffsets = new Dictionary<string, Vector3>
        {
            {"5aafa857e5b5b00018480968", new Vector3(0f, 0.05f, -0.01f)}, //m1a
            { "5b0bbe4e5acfc40dc528a72d", new Vector3(0f, 0f, 0f)}, //sa58
            { "676176d362e0497044079f4c", new Vector3(0f, -0.01f, -0.01f)}, //x17
            { "6183afd850224f204c1da514", new Vector3(0f, -0.01f, -0.01f)}, //mk17
            { "6165ac306ef05c2ce828ef74", new Vector3(0f, -0.01f, -0.01f)}, //mk17 fde
            { "6184055050224f204c1da540", new Vector3(0f, -0.01f, -0.01f)}, //mk16
            { "618428466ef05c2ce828f218", new Vector3(0f, -0.01f, -0.01f)}, //mk16 fde
            { "5ae08f0a5acfc408fb1398a1", new Vector3(0f, 0.02f, 0f)}, //mosin 
            { "5bfd297f0db834001a669119", new Vector3(0f, 0.02f, 0f)}, //mosin sniper
            { "54491c4f4bdc2db1078b4568", new Vector3(0f, 0.02f, 0f)}, //mp133
            { "56dee2bdd2720bc8328b4567", new Vector3(0f, 0.02f, 0f)}, //mp153
            { "606dae0ab0e443224b421bb7", new Vector3(0f, 0.02f, 0f)}, //mp155
            { "6259b864ebedf17603599e88", new Vector3(0f, 0.02f, 0f)}, //M3
            { "6783ae5bb52da6ed912e3d01", new Vector3(0f, 0.02f, 0f)}, //M3 mechanic         
            { "5a7828548dc32e5a9c28b516", new Vector3(0f, 0.02f, 0f)}, //M870   
            { "633ec7c2a6918cb895019c6c", new Vector3(0f, 0f, -0.016f)} //rsh-12
        };

        private bool ShouldDoFaceGearCantedOffset
        {
            get
            {
                return GearStateInstance.HasGasMask || (GearStateInstance.FaceShieldIsActive && GearStateInstance.GearBlocksMouth);
            }
        }

        private bool RotateHighReadyReload
        {
            get
            {
                return
                    _latchedReloadType == EStanceReloadType.Magazine ||
                    _latchedReloadType == EStanceReloadType.QuickReload ||
                    _latchedReloadType == EStanceReloadType.RemoveMagazine ||
                    _latchedReloadType == EStanceReloadType.InsertMagazine ||
                    (_latchedReloadType == EStanceReloadType.CheckAmmo) ||
                    _latchedReloadType == EStanceReloadType.Tube;
            }
        }

        private bool RotateHighReadyManip
        {
            get
            {
                return
                    !WeaponStateInstance.IsShotgun &&
                    (_latchedReloadType == EStanceReloadType.CheckChamber) ||
                    (_latchedReloadType == EStanceReloadType.Rechamber) ||
                    (_latchedReloadType == EStanceReloadType.ClearMalfunction);
            }
        }

        private bool RotateActiveAimReload
        {
            get
            {
                return
                    _latchedReloadType == EStanceReloadType.Magazine ||
                    _latchedReloadType == EStanceReloadType.QuickReload ||
                    _latchedReloadType == EStanceReloadType.RemoveMagazine ||
                    _latchedReloadType == EStanceReloadType.InsertMagazine ||
                    (_latchedReloadType == EStanceReloadType.CheckAmmo) ||
                    _latchedReloadType == EStanceReloadType.Tube;
            }
        }

        private bool RotateActiveAimManip
        {
            get
            {
                return
                    (_latchedReloadType == EStanceReloadType.CheckChamber) ||
                    (_latchedReloadType == EStanceReloadType.Rechamber) ||
                    (_latchedReloadType == EStanceReloadType.ClearMalfunction);
            }
        }

        private bool RotateLowReadyReload
        {
            get
            {
                return
                    _latchedReloadType == EStanceReloadType.Magazine ||
                    _latchedReloadType == EStanceReloadType.QuickReload ||
                    _latchedReloadType == EStanceReloadType.RemoveMagazine ||
                    _latchedReloadType == EStanceReloadType.InsertMagazine ||
                    (_latchedReloadType == EStanceReloadType.CheckAmmo) ||
                    _latchedReloadType == EStanceReloadType.Tube;
            }
        }

        private bool RotateLowReadyManip
        {
            get
            {
                return
                    (_latchedReloadType == EStanceReloadType.CheckChamber) ||
                    (_latchedReloadType == EStanceReloadType.Rechamber) ||
                    (_latchedReloadType == EStanceReloadType.ClearMalfunction);
            }
        }

        private bool IsUsingCantedSight(bool checkifAiming)
        {
            bool isCanted = Mathf.Abs(PlayerStateInstance.PWA.CurrentScope.Rotation) >= EFTHardSettings.Instance.SCOPE_ROTATION_THRESHOLD;
            bool isAiming = !checkifAiming || AimStateInstance.IsAiming;
            return isCanted && isAiming;
        }

        private bool DoOffsets
        {
            get
            {
                return
                    PlayerStateInstance.WeaponIsReady &&
                    !AimStateInstance.IsAiming &&
                    !PlayerStateInstance.IsMounting &&
                    !PlayerStateInstance.IsSprinting &&
                    !PlayerStateInstance.IsUsingStationaryWeapon;
            }
        }



        public void RunOnAwake()
        {
            StanceEvents.OnStanceReload += StartReloadLatch;
            StanceEvents.OnStanceReloadReset += OnReloadOperationEnded;
            StanceEvents.OnTransformsInitFC += SetBaseWeaponOffsetPosition;
        }

        public void RunOnDestroy()
        {
            StanceEvents.OnStanceReload -= StartReloadLatch;
            StanceEvents.OnStanceReloadReset -= OnReloadOperationEnded;
            StanceEvents.OnTransformsInitFC -= SetBaseWeaponOffsetPosition;
        }

        // EFT ends a rechamber as the new round goes in, while the hand is still heading back. Returning from there
        // overlaps the hand's return and looks better than waiting for it to reach the grip, so rechamber ends here.
        private void OnReloadOperationEnded()
        {
            if (_latchedReloadType == EStanceReloadType.Rechamber)
                ClearReloadLatch();
        }

        public void RunOnUpdate(float deltaTime)
        {
            UpdateReloadLatch(deltaTime);
            UpdateSpringValues();
            UpdateBaseOffset(deltaTime);
            DoExtraPosAndRot(PlayerStateInstance.PWA, PlayerStateInstance.Player);
        }

        private void UpdateBaseOffset(float deltaTime)
        {
            Vector3 target = BaseWeaponOffsetPosition;
            float t = 1f - Mathf.Exp(-BaseOffsetEaseSpeed * deltaTime);
            EasedBaseWeaponOffsetPosition = Vector3.Lerp(EasedBaseWeaponOffsetPosition, target, t);

            if ((EasedBaseWeaponOffsetPosition - target).sqrMagnitude < BaseOffsetSnapThresholdSqr)
                EasedBaseWeaponOffsetPosition = target;
        }

        // raised once per reload or manipulation, after StanceReloadSpeedSystem has set the reload type
        private void StartReloadLatch()
        {
            var fc = PlayerStateInstance.FirearmController;
            var type = StanceControllerInstance.CurrentReloadType;
            if (fc == null || type == EStanceReloadType.None)
                return;

            _latchedReloadType = type;
            _latchStartTime = Time.time;
            // the hands may not be idle yet (e.g. reload queued during a burst), so "started" means reaching a new non-idle state
            _latchStartStateHash = fc.FirearmsAnimator.Animator.GetCurrentAnimatorStateInfo(HandsLayer).fullPathHash;
            _handsStarted = false;
            _handsIdleTime = 0f;
        }

        private void ClearReloadLatch()
        {
            _latchedReloadType = EStanceReloadType.None;
            _reloadWeight = 0f;
        }

        private void UpdateReloadLatch(float deltaTime)
        {
            var fc = PlayerStateInstance.FirearmController;
            if (fc == null)
            {
                _hasIdlePose = false;
                ClearReloadLatch();
                return;
            }

            float stateWeight = GetHandsBusyWeight(fc.FirearmsAnimator, out int stateHash, out int nextHash, out bool nextIsIdle);
            bool hasPose = TryReadHandsPose(out float left, out float right, out Quaternion weaponRot);

            if (_latchedReloadType == EStanceReloadType.None)
            {
                // kept fresh while idle, it's what "hands home" is measured against
                if (stateWeight <= 0f && hasPose)
                {
                    _idleLeftDist = left;
                    _idleRightDist = right;
                    _idleWeaponRot = weaponRot;
                    _hasIdlePose = true;
                }
                return;
            }

            if (Time.time - _latchStartTime > ReloadLatchTimeout)
            {
                ClearReloadLatch();
                return;
            }

            // without an idle pose to compare against, fall back to the animator state alone
            float poseWeight = hasPose && _hasIdlePose ? GetPoseWeight(left, right, weaponRot) : 1f;

            if (!_handsStarted)
            {
                bool enteredNewAction = nextHash != 0 ? !nextIsIdle && nextHash != _latchStartStateHash : stateWeight > 0f && stateHash != _latchStartStateHash;
                if (enteredNewAction)
                    _handsStarted = true;
            }

            // the state still decides when the latch ends, so a hand passing near its grip mid-reload only dips the offset
            if (stateWeight > 0f)
            {
                _handsIdleTime = 0f;
            }
            else
            {
                _handsIdleTime += deltaTime;
                if (!_handsStarted && _handsIdleTime > HandsStartGrace)
                {
                    ClearReloadLatch();
                    return;
                }
                if (_handsStarted && _handsIdleTime > HandsIdleConfirm)
                {
                    ClearReloadLatch();
                    return;
                }
            }

            _reloadWeight = _handsStarted ? Mathf.Min(stateWeight, poseWeight) : 0f;
        }

        // 0 with both hands at their idle grip distance and the weapon at its idle rotation, 1 once a hand is 10 cm further off or the weapon 15 deg
        private float GetPoseWeight(float left, float right, Quaternion weaponRot)
        {
            float handOff = Mathf.Max(left - _idleLeftDist, right - _idleRightDist);
            float handWeight = Mathf.InverseLerp(HandTolerance, HandReturnRange, handOff);
            float weaponWeight = Mathf.InverseLerp(WeaponAngleTolerance, WeaponReturnAngle, Quaternion.Angle(weaponRot, _idleWeaponRot));
            return Mathf.Max(handWeight, weaponWeight);
        }

        /// <summary>
        /// Distance of each hand's animated IK marker to its grip marker (what vanilla IkProcess compares for GripWeight), and the
        /// weapon bone's rotation inside WeaponRootAnim. Both come from the weapon animator only, procedural offsets don't change them.
        /// </summary>
        private static bool TryReadHandsPose(out float left, out float right, out Quaternion weaponRot)
        {
            left = right = -1f;
            weaponRot = Quaternion.identity;
            if (_poseReadFailed) return false;

            var player = PlayerStateInstance.Player;
            var container = PlayerStateInstance.PWA?.HandsContainer;
            if (player == null || container?.WeaponRootAnim == null || container.Weapon == null) return false;

            try
            {
                _markersRef ??= AccessTools.FieldRefAccess<Player, Transform[]>("_markers");
                _gripRefsRef ??= AccessTools.FieldRefAccess<Player, Transform[]>("_gripReferences");
                var markers = _markersRef(player);
                var grips = _gripRefsRef(player);
                if (markers?[0] == null || markers[1] == null || grips?[0] == null || grips[1] == null) return false;

                left = Vector3.Distance(markers[0].position, grips[0].position);
                right = Vector3.Distance(markers[1].position, grips[1].position);
                weaponRot = Quaternion.Inverse(container.WeaponRootAnim.rotation) * container.Weapon.rotation;
                return true;
            }
            catch (System.Exception e)
            {
                _poseReadFailed = true;
                RealismCommonLib.Plugin.ModLogger.LogError($"[StanceOverhaul] can't read hand IK markers, reload offsets fall back to the animator state: {e.Message}");
                return false;
            }
        }

        private static bool IsHandsIdleState(FirearmsAnimator fa, AnimatorStateInfoWrapper state)
        {
            // same states as FirearmsAnimator.IsIdling
            return state.IsName("IDLE") || state.IsName(fa.FullIdleStateName) || state.IsName("IDLE WEAPON") || state.IsName("IDLE PLANT");
        }

        // 1 while the hands play something other than idle, 1 → 0 across the transition back to idle, 0 in idle.
        private static float GetHandsBusyWeight(FirearmsAnimator fa, out int stateHash, out int nextHash, out bool nextIsIdle)
        {
            var anim = fa.Animator;
            var current = anim.GetCurrentAnimatorStateInfo(HandsLayer);
            bool currentIsIdle = IsHandsIdleState(fa, current);
            stateHash = current.fullPathHash;
            nextHash = 0;
            nextIsIdle = false;

            if (!anim.IsInTransition(HandsLayer))
                return currentIsIdle ? 0f : 1f;

            var next = anim.GetNextAnimatorStateInfo(HandsLayer);
            nextHash = next.fullPathHash;
            nextIsIdle = IsHandsIdleState(fa, next);

            if (!nextIsIdle) return 1f;
            if (currentIsIdle) return 0f;

            // IAnimator.GetTransitionNormalizedTime throws on the hands animator (UnityAnimatorWrapper), so read the Unity Animator
            var raw = (anim as UnityAnimatorWrapper)?.Animator;
            return raw != null ? 1f - raw.GetAnimatorTransitionInfo(HandsLayer).normalizedTime : 1f;
        }

        private void SetBaseWeaponOffsetPosition(Player.FirearmController fc)
        {
            _weaponSpecificOffset = GetWeaponOffset(fc.Weapon?.TemplateId) ?? Vector3.zero;
            // new weapon rig, so a new idle pose
            _hasIdlePose = false;
            ClearReloadLatch();
        }

        //todo: do this once per weapon variable update, cache result
        private void UpdateSpringValues()
        {
            var stance = StanceControllerInstance.CurrentStance;
            var stats = StanceControllerInstance.StatsHandlerInstance;

            // with no stance the constants are the spring values themselves, not modifiers
            var damping = stance != null ? stats.GetSpringDamping(stance.StanceDampingModifier) * 1.075f : StanceslessDamping;
            var returnSpeed = stance != null ? stats.GetSpringReturnSpeed(stance.StanceReturnSpeedModifier) : StanceslessReturnSpeed;

            StanceControllerInstance.OffsetPositionSpring.ReturnSpeed = returnSpeed;
            StanceControllerInstance.OffsetRotationSpring.ReturnSpeed = returnSpeed;

            StanceControllerInstance.OffsetPositionSpring.Damping = damping;
            StanceControllerInstance.OffsetRotationSpring.Damping = damping;
        }

        private void HandleReloadOffsets()
        {
            if (_latchedReloadType == EStanceReloadType.None || _reloadWeight <= 0f || WeaponStateInstance.TreatAsPistol)
                return;

            if (StanceControllerInstance.CurrentStanceType == EStanceType.HighReady)
            {
                var rotationOffset = RotateHighReadyReload ? 20f : RotateHighReadyManip ? 5f : 0f;
                _targetRotOffest += new Vector3(0f, rotationOffset, 0f) * _reloadWeight;

                var yOffset = RotateHighReadyReload ? -0.05f : RotateHighReadyManip ? -0.04f : 0f;
                var zOffset = RotateHighReadyReload ? 0.05f : RotateHighReadyManip ? 0.025f : 0f;
                _targetPosOffest += new Vector3(0f, yOffset, zOffset) * _reloadWeight;
            }

            if (StanceControllerInstance.CurrentStanceType == EStanceType.ActiveAiming)
            {
                var rotationOffset = RotateActiveAimReload ? 30f : RotateActiveAimManip ? 10f : 0f;
                _targetRotOffest += new Vector3(0f, rotationOffset, 0f) * _reloadWeight;

                var yOffset = RotateActiveAimReload || RotateActiveAimManip ? -0.01f : 0f;
                _targetPosOffest += new Vector3(0f, yOffset, 0f) * _reloadWeight;
            }

            if (StanceControllerInstance.CurrentStanceType == EStanceType.LowReady)
            {
                var rotationOffsetX = RotateLowReadyReload ? -5f : RotateLowReadyManip ? 0f : 0f;
                var rotationOffsetY = RotateLowReadyReload ? 10f : RotateLowReadyManip ? 10f : 0f;
                _targetRotOffest += new Vector3(rotationOffsetX, rotationOffsetY, 0f) * _reloadWeight;

                var yOffset = RotateLowReadyReload || RotateLowReadyManip ? 0.01f : 0f;
                var zOffset = RotateLowReadyReload || RotateLowReadyManip ? 0.005f : 0f;
                _targetPosOffest += new Vector3(0f, yOffset, zOffset) * _reloadWeight;
            }
        }

        private void HandleHeightStanceOffset()
        {
            float posYOffsetModifier = WeaponStateInstance.HasShoulderContact || WeaponStateInstance.TreatAsPistol ? -0.015f : 0.02f;
            float poseYPosOffset = (1f - PlayerStateInstance.Player.MovementContext.PoseLevel) * posYOffsetModifier;
            _targetPosOffest += new Vector3(0, poseYPosOffset, 0f);

            float poseRotOffset = (1f - PlayerStateInstance.Player.MovementContext.PoseLevel) * -3.5f;
            _targetRotOffest += new Vector3(0f, poseRotOffset, 0f);
        }

        private void HandleLeanOffset()
        {

        }

        private void HandleErgoOffset()
        {
            var offset = new Vector3(0f, StanceControllerInstance.StatsHandlerInstance.GetWeaponOffset(), 0f);
            _targetRotOffest += offset;
        }

        private void HandleThirdPersonOffsets()
        {

        }

        private void HandleMovementOffsets()
        {

        }

        //non-stance related rotational and postion changes for immersion
        private void DoExtraPosAndRot(ProceduralWeaponAnimation pwa, Player player)
        {
            _targetRotOffest = Vector3.zero;
            _targetPosOffest = Vector3.zero;

            if (DoOffsets)
            {
                HandleReloadOffsets();
                if (PluginConfig.EnableExtraProcEffects.Value) HandleHeightStanceOffset();
                if (PluginConfig.EnableExtraProcEffects.Value) HandleErgoOffset();
            }

            float smoothFactorRot = Mathf.Clamp01(Time.deltaTime * ExtraRotSmoothSpeed); //TODO speed needs to be factored by reload type modified speed from all modifiers ( StatModifiers.RechamberSpeed etc)
            float smoothFactorPos = Mathf.Clamp01(Time.deltaTime * ExtraPosSmoothSpeed);

            DetailsRotationOffset = Vector3.Lerp(DetailsRotationOffset, _targetRotOffest, smoothFactorRot);
            DetailsPositionOffset = Vector3.Lerp(DetailsPositionOffset, _targetPosOffest, smoothFactorPos);

            // //position
            // float stockOffset = !WeaponStateInstance.IsPistol && !WeaponStateInstance.HasShoulderContact ? -0.04f : 0f;
            // float stockPosOffset = WeaponStateInstance.StockPosition * 0.01f;
            // float posOffsetMulti = WeaponStateInstance.HasShoulderContact ? -0.04f : 0.04f;
            // float posePosOffset = (1f - player.MovementContext.PoseLevel) * posOffsetMulti;

            // float targetPosXOffset = pwa.IsAiming ? 0f : 0f;
            // float targetPosYOffset = pwa.IsAiming ? 0f : 0f;
            // float targetPosZOffset = pwa.IsAiming ? 0f : Mathf.Clamp(posePosOffset + stockOffset + stockPosOffset, -0.05f, 0.05f);
            // Vector3 targetPos = new Vector3(targetPosXOffset, targetPosYOffset, targetPosZOffset);

            // _posePosOffest = Vector3.Lerp(_posePosOffest, targetPos, 5f * Time.deltaTime);

            // //rotation
            // bool isMountedWithBipod = WeaponStateInstance.BipodIsDeployed && PlayerStateInstance.IsMounting;
            // bool doCantedSightOffset = IsCantedAiming(pwa, true);
            // bool doMaskOffset =
            //     !doCantedSightOffset &&
            //     !isMountedWithBipod &&
            //     ShouldDoFaceGearCantedOffset() &&
            //     !WeaponStateInstance.WeaponCanFSADS &&
            //     pwa.IsAiming &&
            //     WeaponStateInstance.HasShoulderContact &&
            //     WeaponStateInstance.TreatAsPistol;
            // bool doLongMagOffset = WeaponStateInstance.HasLongMag && player.IsInPronePose && !isMountedWithBipod;
            // float cantedOffsetBase = -0.41f;
            // float magOffset = doCantedSightOffset ? 0f : doLongMagOffset && !pwa.IsAiming ? -0.35f : doLongMagOffset && pwa.IsAiming ? -0.12f : 0f;
            // float ergoOffset = WeaponStateInstance.ErgoFactor * -0.001f;
            // float poseRotOffset = (1f - player.MovementContext.PoseLevel) * -0.03f;
            // poseRotOffset += player.IsInPronePose ? -0.03f : 0f;
            // float maskFactor = doMaskOffset ? -0.025f + ergoOffset : 0f;
            // float baseRotOffset = pwa.IsAiming || IsMounting || IsBracing ? 0f : poseRotOffset + ergoOffset;
            // float cantedSightOffset = doCantedSightOffset ? cantedOffsetBase : 0f;

            // float rotX = 0f;
            // float rotY = Mathf.Clamp(baseRotOffset + maskFactor + magOffset, -0.5f, 0f) + cantedSightOffset;
            // float rotZ = 0f;
            // Vector3 targetRot = new Vector3(rotX, rotY, rotZ);

            // _poseRotOffest = Vector3.Lerp(_poseRotOffest, targetRot, 5f * Time.deltaTime); //speeds should be affected by stance multi? or player crouch speed?

            // Quaternion newRot = Quaternion.identity;
            // newRot.x = _poseRotOffest.x;
            // newRot.y = _poseRotOffest.y;
            // newRot.z = _poseRotOffest.z;
            // _poseQuatOffset = newRot;
        }
    }
}
