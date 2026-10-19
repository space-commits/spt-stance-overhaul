using EFT;
using EFT.Animations;
using UnityEngine;
using RealismCommonLib.ModifierHandlers;
using static RealismCommonLib.Plugin;
using static StanceOverhaul.Plugin;

namespace StanceOverhaul.SubSystem.Aiming
{
    public class StanceAimSystem : ISubSystem
    {
        private const float AdsInSpeed = 9.5f;
        private const float AdsOutSpeed = 4.15f;

        private BoolGateHandle _canAim;
        private bool _blockerInterruptedAim;
        private static Vector3 _smoothedOffset;

        public void RunOnAwake()
        {
            RealismCommonLib.Events.PlayerEvents.OnPlayerInit += CheckForAimBlockers;
            RealismCommonLib.Events.PlayerEvents.AimStateChanged += CheckForAimBlockers;
            RealismCommonLib.Events.PlayerEvents.ToggleHeadDevice += CheckForAimBlockers;
            WeaponStateInstance.OnWeaponStateChanged += CheckForAimBlockers;
            RealismCommonLib.Events.PlayerEvents.OnWeaponEquipped += CheckForAimBlockers;
            _canAim = BoolHandlers.CanAim.Add(true);
        }

        public void RunOnDestroy()
        {
            RealismCommonLib.Events.PlayerEvents.OnPlayerInit -= CheckForAimBlockers;
            RealismCommonLib.Events.PlayerEvents.AimStateChanged -= CheckForAimBlockers;
            RealismCommonLib.Events.PlayerEvents.ToggleHeadDevice -= CheckForAimBlockers;
            WeaponStateInstance.OnWeaponStateChanged -= CheckForAimBlockers;
            RealismCommonLib.Events.PlayerEvents.OnWeaponEquipped -= CheckForAimBlockers;
            BoolHandlers.CanAim.Remove(_canAim);
        }

        public void RunOnUpdate(float deltaTime)
        {
        }

        private void CheckForAimBlockers()
        {
            bool nvgBlocksAds =
                PluginConfig.EnableNVGAimBlock.Value
                && GearStateInstance.NVGIsActive
                && WeaponStateInstance.HasOptic;

            bool thermalBlocksAds =
                PluginConfig.EnableThermalAimBlock.Value
                && GearStateInstance.ThermalIsActive;

            bool faceshieldBlocksADS =
                PluginConfig.EnableFSAimBlock.Value
                && GearStateInstance.FaceShieldIsActive
                && WeaponStateInstance.HasShoulderContact;

            bool blocked = nvgBlocksAds || faceshieldBlocksADS || thermalBlocksAds;
            _canAim.Allowed = !blocked;

            if (blocked)
            {
                // Force-exit ADS once if a blocker became active while already aiming
                if (!_blockerInterruptedAim && AimStateInstance.IsAiming)
                {
                    PlayerStateInstance?.FirearmController?.ToggleAim();
                    _blockerInterruptedAim = true;
                }
            }
            else
            {
                _blockerInterruptedAim = false;
            }
        }

        public void MoveGunToCamera(ProceduralWeaponAnimation pwa, float dt)
        {
            if (!PlayerStateInstance.WeaponIsReady
                  || !StanceControllerInstance.CanMoveGunToCamera
                  || pwa == null || pwa.HandsContainer == null
                  || pwa.HandsContainer.WeaponRootAnim == null
                  || pwa.HandsContainer.CameraTransform == null)
                return;

            Transform root = pwa.HandsContainer.WeaponRootAnim;
            Transform camera = pwa.HandsContainer.CameraTransform;
            var scope = pwa?.CurrentScope;

            if (scope == null || scope?.Bone == null)
                return;

            var correction = Vector3.zero;

            if (pwa.IsAiming)
            {
                Transform parent = root.parent;
                Transform sight = scope.Bone;

                //put both points into the same coordinate space
                Vector3 cameraInParent = parent.InverseTransformPoint(camera.position);

                Vector3 sightInParent = parent.InverseTransformPoint(sight.position);

                //correct = offset from where the weapon is to where it should be
                correction = cameraInParent - sightInParent;

                // reproduce the additional offsets used by games's CalculateCameraPosition() in world space, then convert to weapon parent space

                Vector3 shiftInCameraSpace = new Vector3(0f, pwa._cameraShiftToLineOfSight.y, pwa._cameraShiftToLineOfSight.x);
                Vector3 shiftWorld = camera.parent.TransformDirection(shiftInCameraSpace);
                Vector3 shiftInWeaponParent = parent.InverseTransformDirection(shiftWorld);

                correction -= shiftInWeaponParent;

                // X = left/right
                // Y = forward/back
                // Z = up/down
                // Ignore forward/back, let camera handle it
                correction.y = 0f;
            }

            float speed = (pwa.IsAiming ? AdsInSpeed : AdsOutSpeed) * StanceControllerInstance.PwaAimSpeed;
            float t = 1f - Mathf.Exp(-speed * dt);
            _smoothedOffset = Vector3.Lerp(_smoothedOffset, correction, t);

            // float smoothTime = pwa.IsAiming ? PluginConfig.test3.Value : PluginConfig.test4.Value;
            // smoothTime /= Mathf.Max(StanceControllerInstance.PwaAimSpeed, 0.01f);
            // _smoothedOffset = Vector3.SmoothDamp(_smoothedOffset, correction, ref _offsetVelocity, smoothTime, Mathf.Infinity, dt);

            Vector3 basePosition = root.localPosition;
            root.localPosition = basePosition + _smoothedOffset;
        }
    }
}
