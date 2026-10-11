using EFT.Animations;
using EFT;
using EFT.InventoryLogic;
using EFT.CameraControl;
using UnityEngine;
using System.Collections.Generic;
using StanceOverhaul.Enums;
using StanceOverhaul.Events;
using RealismCommonLib.Utils;
using RealismCommonLib.ModifierHandlers;
using static StanceOverhaul.Plugin;
using static RealismCommonLib.Plugin;

namespace StanceOverhaul.SubSystem.Animator
{
    internal class WeaponOffsetAnimator : ISubSystem
    {
        private const float StanceslessDamping = 0.82f;
        private const float StanceslessReturnSpeed = 0.06f;

        private Vector3 _targetPosOffest = Vector3.zero;
        private Vector3 _targetRotOffest = Vector3.zero;
        private Vector3 _baseWeaponOffsetPosition = Vector3.zero;


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

                return blockOffset ? Vector3.zero : _baseWeaponOffsetPosition;
            }

        }

        public Vector3 DetailsPositionOffset { get; private set; } = Vector3.zero;
        public Vector3 DetailsRotationOffset { get; private set; } = Vector3.zero;

        private DelayTimer _chamberCheckTimer = new DelayTimer(0.5f);
        private DelayTimer _ammoCheckTimer = new DelayTimer(0.5f);
        private DelayTimer _rechamberTimer = new DelayTimer(0.5f);

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
            {"5aafa857e5b5b00018480968", new Vector3(0f, 0f, 0f)}, //m1a
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
                    StanceControllerInstance.CurrentReloadType == EStanceReloadType.Magazine ||
                    StanceControllerInstance.CurrentReloadType == EStanceReloadType.QuickReload ||
                    StanceControllerInstance.CurrentReloadType == EStanceReloadType.RemoveMagazine ||
                    StanceControllerInstance.CurrentReloadType == EStanceReloadType.InsertMagazine ||
                    (StanceControllerInstance.CurrentReloadType == EStanceReloadType.CheckAmmo && _ammoCheckTimer.IsActive) ||
                    StanceControllerInstance.CurrentReloadType == EStanceReloadType.Tube;
            }
        }

        private bool RotateHighReadyManip
        {
            get
            {
                return
                    (StanceControllerInstance.CurrentReloadType == EStanceReloadType.CheckChamber && _chamberCheckTimer.IsActive) ||
                    (StanceControllerInstance.CurrentReloadType == EStanceReloadType.Rechamber && _rechamberTimer.IsActive) ||
                    (StanceControllerInstance.CurrentReloadType == EStanceReloadType.ClearMalfunction && _rechamberTimer.IsActive);
            }
        }

        private bool RotateActiveAimReload
        {
            get
            {
                return
                    StanceControllerInstance.CurrentReloadType == EStanceReloadType.Magazine ||
                    StanceControllerInstance.CurrentReloadType == EStanceReloadType.QuickReload ||
                    StanceControllerInstance.CurrentReloadType == EStanceReloadType.RemoveMagazine ||
                    StanceControllerInstance.CurrentReloadType == EStanceReloadType.InsertMagazine ||
                    (StanceControllerInstance.CurrentReloadType == EStanceReloadType.CheckAmmo && _ammoCheckTimer.IsActive) ||
                    StanceControllerInstance.CurrentReloadType == EStanceReloadType.Tube;
            }
        }

        private bool RotateActiveAimManip
        {
            get
            {
                return
                    (StanceControllerInstance.CurrentReloadType == EStanceReloadType.CheckChamber && _chamberCheckTimer.IsActive) ||
                    (StanceControllerInstance.CurrentReloadType == EStanceReloadType.Rechamber && _rechamberTimer.IsActive) ||
                    (StanceControllerInstance.CurrentReloadType == EStanceReloadType.ClearMalfunction && _rechamberTimer.IsActive);
            }
        }

        private bool RotateLowReadyReload
        {
            get
            {
                return
                    StanceControllerInstance.CurrentReloadType == EStanceReloadType.Magazine ||
                    StanceControllerInstance.CurrentReloadType == EStanceReloadType.QuickReload ||
                    StanceControllerInstance.CurrentReloadType == EStanceReloadType.RemoveMagazine ||
                    StanceControllerInstance.CurrentReloadType == EStanceReloadType.InsertMagazine ||
                    (StanceControllerInstance.CurrentReloadType == EStanceReloadType.CheckAmmo && _ammoCheckTimer.IsActive) ||
                    StanceControllerInstance.CurrentReloadType == EStanceReloadType.Tube;
            }
        }

        private bool RotateLowReadyManip
        {
            get
            {
                return
                    (StanceControllerInstance.CurrentReloadType == EStanceReloadType.CheckChamber && _chamberCheckTimer.IsActive) ||
                    (StanceControllerInstance.CurrentReloadType == EStanceReloadType.Rechamber && _rechamberTimer.IsActive) ||
                    (StanceControllerInstance.CurrentReloadType == EStanceReloadType.ClearMalfunction && _rechamberTimer.IsActive);
            }
        }

        /// <summary>
        /// For guns that have very quick chamber/ammo check, need faster time
        /// </summary>
        private bool IsGunThatDoesInternalReloads
        {
            get
            {
                return WeaponStateInstance.WeaponReloadType == Weapon.EReloadMode.InternalMagazine || WeaponStateInstance.WeaponReloadType == Weapon.EReloadMode.OnlyBarrel;
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
            StanceEvents.OnStanceReloadReset += ResetTimers;
            StanceEvents.OnStanceCheckAmmo += StartCheckAmmoTimer;
            StanceEvents.OnStanceChamberCheck += StartChamberCheckTimer;
            StanceEvents.OnStanceChamber += StartRechamberTimer;
            StanceEvents.OnTransformsInitFC += SetBaseWeaponOffsetPosition;
        }

        public void RunOnDestroy()
        {
            StanceEvents.OnStanceReloadReset -= ResetTimers;
            StanceEvents.OnStanceCheckAmmo -= StartCheckAmmoTimer;
            StanceEvents.OnStanceChamberCheck -= StartChamberCheckTimer;
            StanceEvents.OnStanceChamber -= StartRechamberTimer;
            StanceEvents.OnTransformsInitFC -= SetBaseWeaponOffsetPosition;
        }

        public void RunOnUpdate(float deltaTime)
        {
            UpdateTimers();
            UpdateSpringValues();
            DoExtraPosAndRot(PlayerStateInstance.PWA, PlayerStateInstance.Player);
        }

        private void UpdateTimers()
        {
            _chamberCheckTimer.Update();
            _ammoCheckTimer.Update();
            _rechamberTimer.Update();
        }

        private void ResetTimers()
        {
            _chamberCheckTimer.Stop();
            _ammoCheckTimer.Stop();
            _rechamberTimer.Stop();
        }

        private void StartRechamberTimer()
        {
            var modifier = IsGunThatDoesInternalReloads ? 1f : 1f;
            _rechamberTimer.Duration = (1f / StatModifiers.RechamberSpeed.Apply(1f)) * modifier;
            _rechamberTimer.Start();
        }

        private void StartChamberCheckTimer()
        {
            var modifier = IsGunThatDoesInternalReloads ? 1f : 1.6f;
            _chamberCheckTimer.Duration = (1f / StatModifiers.CheckChamberSpeed.Apply(1f)) * modifier;
            _chamberCheckTimer.Start();
        }

        private void StartCheckAmmoTimer()
        {
            var modifier = IsGunThatDoesInternalReloads ? 1f : 3.15f;
            _ammoCheckTimer.Duration = (1f / StatModifiers.CheckAmmoSpeed.Apply(1f)) * modifier;
            _ammoCheckTimer.Start();
        }

        private void SetBaseWeaponOffsetPosition(Player.FirearmController fc)
        {
            Vector3 baseOffset = GetWeaponOffset(fc.Weapon?.TemplateId) ?? Vector3.zero;
            Vector3 configOffset = WeaponStateInstance.TreatAsPistol ?
                new Vector3(PluginConfig.PistolOffsetX.Value, PluginConfig.PistolOffsetY.Value, PluginConfig.PistolOffsetZ.Value) :
                new Vector3(PluginConfig.RifleOffsetX.Value, PluginConfig.RifleOffsetY.Value, PluginConfig.RifleOffsetZ.Value);

            _baseWeaponOffsetPosition = configOffset + baseOffset;
        }

        //todo: do this once per weapon variable update, cache result
        private void UpdateSpringValues()
        {
            var damping = StanceControllerInstance.StatsHandlerInstance.GetSpringDamping(StanceControllerInstance.CurrentStance?.StanceDampingModifier * 1.025f ?? StanceslessDamping);
            var returnSpeed = StanceControllerInstance.StatsHandlerInstance.GetSpringReturnSpeed(StanceControllerInstance.CurrentStance?.StanceReturnSpeedModifier * 0.6f ?? StanceslessReturnSpeed);

            StanceControllerInstance.OffsetPositionSpring.ReturnSpeed = StanceControllerInstance.StatsHandlerInstance.GetSpringReturnSpeed(returnSpeed);
            StanceControllerInstance.OffsetRotationSpring.ReturnSpeed = StanceControllerInstance.StatsHandlerInstance.GetSpringReturnSpeed(returnSpeed);

            StanceControllerInstance.OffsetPositionSpring.Damping = StanceControllerInstance.StatsHandlerInstance.GetSpringDamping(damping);
            StanceControllerInstance.OffsetRotationSpring.Damping = StanceControllerInstance.StatsHandlerInstance.GetSpringDamping(damping);
        }

        private void HandleReloadOffsets()
        {
            if (StanceControllerInstance.CurrentReloadType == EStanceReloadType.None || WeaponStateInstance.TreatAsPistol)
                return;

            if (StanceControllerInstance.CurrentStanceType == EStanceType.HighReady)
            {
                var rotationOffset = RotateHighReadyReload ? 20f : RotateHighReadyManip ? 5f : 0f;
                _targetRotOffest += new Vector3(0f, rotationOffset, 0f);

                var yOffset = RotateHighReadyReload ? -0.05f : RotateHighReadyManip ? -0.025f : 0f;
                var zOffset = RotateHighReadyReload ? 0.05f : RotateHighReadyManip ? 0.025f : 0f;
                _targetPosOffest += new Vector3(0f, yOffset, zOffset);
            }

            if (StanceControllerInstance.CurrentStanceType == EStanceType.ActiveAiming)
            {
                var rotationOffset = RotateActiveAimReload ? 30f : RotateActiveAimManip ? 10f : 0f;
                _targetRotOffest += new Vector3(0f, rotationOffset, 0f);

                var yOffset = RotateActiveAimReload || RotateActiveAimManip ? -0.01f : 0f;
                _targetPosOffest += new Vector3(0f, yOffset, 0f);
            }

            if (StanceControllerInstance.CurrentStanceType == EStanceType.LowReady)
            {
                var rotationOffsetX = RotateLowReadyReload ? -5f : RotateLowReadyManip ? 0f : 0f;
                var rotationOffsetY = RotateLowReadyReload ? 10f : RotateLowReadyManip ? 10f : 0f;
                _targetRotOffest += new Vector3(rotationOffsetX, rotationOffsetY, 0f);

                var yOffset = RotateLowReadyReload || RotateLowReadyManip ? 0.01f : 0f;
                var zOffset = RotateLowReadyReload || RotateLowReadyManip ? 0.005f : 0f;
                _targetPosOffest += new Vector3(0f, yOffset, zOffset);
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

            float smoothFactorRot = Mathf.Clamp01(Time.deltaTime * 5f); //TODO speed needs to be factored by reload type modified speed from all modifiers ( StatModifiers.RechamberSpeed etc)
            float smoothFactorPos = Mathf.Clamp01(Time.deltaTime * 4f);

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
