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
        // scaled by PWA aim speed, same as the camera's own aim lerp
        private const float AltAimBlendSpeed = 4.5f;
        private const float WeightSnapThreshold = 0.001f;
        private const float OffsetSnapThresholdSqr = 1e-10f;

        // exponent on left shoulder stance progress: 1 = linear with the stance,
        // >1 = camera hands over later in the transition, <1 = earlier
        private const float LeftShoulderBlendTiming = 0.75f;

        private BoolGateHandle _canAim;
        private bool _blockerInterruptedAim;
        private static Vector3 _smoothedOffset;
        private float _gateWeight;

        // 1 = alt aim (gun moves to camera), 0 = vanilla (camera moves to sights)
        public float AltAimWeight { get; private set; }

        public void RunOnAwake()
        {
            _smoothedOffset = Vector3.zero;
            AltAimWeight = 0f;
            _gateWeight = 0f;

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
            if (pwa == null || pwa.HandsContainer == null
                  || pwa.HandsContainer.WeaponRootAnim == null
                  || pwa.HandsContainer.CameraTransform == null)
                return;

            // shared blend between alt aim (gun to camera) and vanilla (camera to sights), also read by CalculateCameraPositionPatch
            // so both sides hand over together instead of hard switching, e.g. when toggling left shoulder while aiming
            // config/mount/stationary/weapon gates have no progress of their own, so ease them over time
            bool altAimAllowed = PlayerStateInstance.WeaponIsReady && StanceControllerInstance.CanMoveGunToCamera;
            float gateT = 1f - Mathf.Exp(-AltAimBlendSpeed * StanceControllerInstance.PwaAimSpeed * dt);
            _gateWeight = Mathf.Lerp(_gateWeight, altAimAllowed ? 1f : 0f, gateT);
            
            if (Mathf.Abs(_gateWeight - (altAimAllowed ? 1f : 0f)) < WeightSnapThreshold)
                _gateWeight = altAimAllowed ? 1f : 0f;

            // left shoulder hands over in step with the stance's own progress, so the camera moves with the gun
            float leftShoulderPose = StanceControllerInstance.LeftShoulderPoseWeight;
            float stanceWeight = 1f - Mathf.Pow(leftShoulderPose, LeftShoulderBlendTiming);

            AltAimWeight = _gateWeight * stanceWeight;

            // weapon swap: offset belongs to the old weapon, don't carry it over
            if (!PlayerStateInstance.WeaponIsReady)
            {
                _smoothedOffset = Vector3.zero;
                return;
            }

            Transform root = pwa.HandsContainer.WeaponRootAnim;
            Transform camera = pwa.HandsContainer.CameraTransform;
            var scope = pwa.CurrentScope;

            var correction = Vector3.zero;

            // when alt aim is inactive the target is zero, so the offset bleeds out instead of vanishing in one frame
            if (pwa.IsAiming && AltAimWeight > 0f && scope != null && scope.Bone != null)
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

                correction *= AltAimWeight;
            }

            float speed = (pwa.IsAiming ? AdsInSpeed : AdsOutSpeed) * StanceControllerInstance.PwaAimSpeed;
            float t = 1f - Mathf.Exp(-speed * dt);
            
            _smoothedOffset = Vector3.Lerp(_smoothedOffset, correction, t);

            // fully handed back to vanilla, stop touching the weapon root
            if (AltAimWeight <= 0f && _smoothedOffset.sqrMagnitude < OffsetSnapThresholdSqr)
            {
                _smoothedOffset = Vector3.zero;
                return;
            }

            // float smoothTime = pwa.IsAiming ? PluginConfig.test3.Value : PluginConfig.test4.Value;
            // smoothTime /= Mathf.Max(StanceControllerInstance.PwaAimSpeed, 0.01f);
            // _smoothedOffset = Vector3.SmoothDamp(_smoothedOffset, correction, ref _offsetVelocity, smoothTime, Mathf.Infinity, dt);

            Vector3 basePosition = root.localPosition;
            root.localPosition = basePosition + _smoothedOffset;
        }
    }
}
