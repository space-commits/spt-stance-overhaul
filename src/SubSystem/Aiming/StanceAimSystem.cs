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
        private const float AdsInSpeedRecoilModifier = 0.4f;
        private const float AdsOutSpeedRecoilModifer = 0.5f;

        // alt aim speeds are all scaled by the weapon's original aim speed, not the stance-eased PwaAimSpeed. The offset speed is eased by AltAimSpeedModifier instead
        private const float AltAimBlendSpeed = 4.5f;
        private const float WeightSnapThreshold = 0.001f;
        private const float OffsetSnapThresholdSqr = 1e-10f;
        private const float CantedRollSnapThreshold = 0.01f;

        // multiplier on the roll speed when switching between a canted and an upright sight while already aimed, lower is slower
        private const float SightSwitchRollSpeedMulti = 0.5f;

        // exponent on left shoulder stance progress: 1 = linear with the stance,
        // >1 = camera hands over later in the transition, <1 = earlier
        private const float LeftShoulderBlendTiming = 0.75f;

        private BoolGateHandle _canAim;
        private bool _blockerInterruptedAim;
        private static Vector3 _smoothedOffset;
        // recoil part of the correction, eased at the vanilla camera's rate (see MoveGunToCamera)
        private static Vector3 _smoothedRecoilFollow;
        private float _gateWeight;
        // canted roll added on top of EFT's pose, and its pivot in root space
        private float _cantedRoll;
        private Vector3 _rollPivotLocal;
        // sight switch while aimed: the roll eases at SightSwitchRollSpeedMulti until it reaches its new target
        private Transform? _lastSightBone;
        private bool _sightSwitchRoll;

        // 1 = alt aim (gun moves to camera), 0 = vanilla (camera moves to sights)
        public float AltAimWeight { get; private set; }

        public void RunOnAwake()
        {
            _smoothedOffset = Vector3.zero;
            _smoothedRecoilFollow = Vector3.zero;
            AltAimWeight = 0f;
            _gateWeight = 0f;
            ResetCantedRoll();

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

        public float CalAimSpeed(ProceduralWeaponAnimation pwa)
        {
            bool treatAsPistol = WeaponStateInstance.TreatAsPistol;

            float recoilModifierIn = FiringStateInstance.IsFiring ? AdsInSpeedRecoilModifier : 1f;
            float recoilModifierOut = FiringStateInstance.IsFiring ? AdsOutSpeedRecoilModifer : 1f;

            float adsInSpeed = treatAsPistol ? PluginConfig.PistolAdsInSpeed.Value : PluginConfig.AdsInSpeed.Value;
            float adsOutSpeed = treatAsPistol ? PluginConfig.PistolAdsOutSpeed.Value : PluginConfig.AdsOutSpeed.Value;

            float baseSpeed = (pwa.IsAiming ? adsInSpeed * recoilModifierIn : adsOutSpeed * recoilModifierOut) * StanceControllerInstance.AltAimSpeedModifier;

            // rolling between a canted and an upright sight while already aimed
            if (_sightSwitchRoll)
                baseSpeed *= 0.75f;

            return StanceControllerInstance.StatsHandlerInstance.GetAltAimSpeedModifier(baseSpeed);
        }

        public void MoveGunToCamera(ProceduralWeaponAnimation pwa, float dt)
        {
            if (pwa == null || pwa.HandsContainer == null
                  || pwa.HandsContainer.WeaponRootAnim == null
                  || pwa.HandsContainer.CameraTransform == null)
                return;

            // before the gate/early returns: vanilla's roll is off whenever alt aim is enabled, not only while AltAimWeight > 0
            ApplyCantedRoll(pwa, pwa.HandsContainer.WeaponRootAnim, dt);

            // shared blend between alt aim (gun to camera) and vanilla (camera to sights), also read by CalculateCameraPositionPatch
            // so both sides hand over together instead of hard switching, e.g. when toggling left shoulder while aiming
            // config/mount/stationary/weapon gates have no progress of their own, so ease them over time
            bool altAimAllowed = PlayerStateInstance.WeaponIsReady && StanceControllerInstance.CanMoveGunToCamera;
            float gateSpeed = 1f - Mathf.Exp(-AltAimBlendSpeed * StanceControllerInstance.PwaOriginalAimSpeed * dt);
            _gateWeight = Mathf.Lerp(_gateWeight, altAimAllowed ? 1f : 0f, gateSpeed);

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
                _smoothedRecoilFollow = Vector3.zero;
                return;
            }

            Transform root = pwa.HandsContainer.WeaponRootAnim;
            Transform camera = pwa.HandsContainer.CameraTransform;
            var scope = GetCurrentScope(pwa);

            var correction = Vector3.zero;
            var recoilFollow = Vector3.zero;

            // when alt aim is inactive the target is zero, so the offset bleeds out instead of vanishing in one frame
            if (pwa.IsAiming && AltAimWeight > 0f && scope != null && scope.Bone != null)
            {
                Transform parent = root.parent;
                Transform sight = scope.Bone;

                //put both points into the same coordinate space
                Vector3 cameraInParent = parent.InverseTransformPoint(camera.position);

                Vector3 sightActual = parent.InverseTransformPoint(sight.position);

                // target where the sight would be without recoil, so recoil plays out instead of being pulled back to the camera
                Vector3 sightInParent = RecoilPoseCapture.RemoveRecoil(pwa, sightActual);

                // recoil is followed separately below, at the vanilla camera's rate. Vanilla's aiming camera targets the recoiling sight
                // (PWA L1868), and under auto-fire slides that target along the sight's own backward axis to the aim depth (L1883-1893),
                // keeping the eye on the rotated line of sight. Mirror it: move the gun so that point lands where the clean sight is brought
                Vector3 eyePoint = sightActual;
                if (pwa.CheckAutoFire())
                {
                    Vector3 back = parent.InverseTransformDirection(sight.name == "aim_camera" ? -sight.up : -sight.forward);
                    // parent y = forward/back, slide to the camera's depth
                    if (Mathf.Abs(back.y) > 1e-4f)
                        eyePoint += back * ((cameraInParent.y - sightActual.y) / back.y);
                }
                recoilFollow = sightInParent - eyePoint;
                recoilFollow.y = 0f;
                recoilFollow *= AltAimWeight;

                // target where the sight would be without stance and detail offset positions, so they play out as authored and only the base is brought to the camera.
                // they go straight into WeaponRootAnim.localPosition (same space), and later steps rotate about weapon-attached points, so subtracting is exact.
                // their rotation's positional side effect is still corrected.
                // Offsets a stance holds while aiming (AltAimCorrectedPositionAxes, e.g. active aim's left/right) are part of the aimed pose, so they're
                // left in and the sight is still brought to the camera. The springs settle at Get() = 2 × Zero, so the held part on the gun is
                // 2 × its pose value; only spring motion around it is excluded
                Vector3 heldOnGun = 2f * StanceControllerInstance.AltAimHeldPosition;
                sightInParent -= StanceControllerInstance.StanceAndOffsetPosition - heldOnGun;

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

            float t = 1f - Mathf.Exp(-CalAimSpeed(pwa) * dt);

            _smoothedOffset = Vector3.Lerp(_smoothedOffset, correction, t);

            // same rate LerpCamera moves the vanilla camera's xy by (raised while firing by CameraSmoothBlender), so the kick and catch-up match vanilla
            float recoilT = 1f - Mathf.Exp(-Patches.CameraDeltaTimePatch.CameraLerpRate(pwa) * dt);
            _smoothedRecoilFollow = Vector3.Lerp(_smoothedRecoilFollow, recoilFollow, recoilT);

            // fully handed back to vanilla, stop touching the weapon root
            if (AltAimWeight <= 0f && _smoothedOffset.sqrMagnitude < OffsetSnapThresholdSqr && _smoothedRecoilFollow.sqrMagnitude < OffsetSnapThresholdSqr)
            {
                _smoothedOffset = Vector3.zero;
                _smoothedRecoilFollow = Vector3.zero;
                return;
            }

            // float smoothTime = pwa.IsAiming ? PluginConfig.test3.Value : PluginConfig.test4.Value;
            // smoothTime /= Mathf.Max(StanceControllerInstance.PwaOriginalAimSpeed, 0.01f);
            // _smoothedOffset = Vector3.SmoothDamp(_smoothedOffset, correction, ref _offsetVelocity, smoothTime, Mathf.Infinity, dt);

            Vector3 basePosition = root.localPosition;
            root.localPosition = basePosition + _smoothedOffset + _smoothedRecoilFollow;
        }

        // a canted sight is the selected sight and alt aim handles its roll. Deliberately "selected", not "aiming",
        // so stance aim scales that read it don't flip on ADS out
        public bool IsCantedSightSelected
        {
            get
            {
                if (!PluginConfig.EnableAltAim.Value || !PlayerStateInstance.WeaponIsReady) return false;
                var scope = GetCurrentScope(PlayerStateInstance.PWA);
                return scope != null && Mathf.Abs(scope.Rotation) >= EFTHardSettings.Instance.SCOPE_ROTATION_THRESHOLD;
            }
        }

        // EFT's CurrentScope indexes ScopeAimTransforms[0] when the list is empty and throws (no weapon, or a weapon with no aim points)
        private static ProceduralWeaponAnimation.SightNBone? GetCurrentScope(ProceduralWeaponAnimation pwa)
        {
            var aimTransforms = pwa.ScopeAimTransforms;
            return aimTransforms != null && aimTransforms.Count > 0 ? pwa.CurrentScope : null;
        }

        private void ResetCantedRoll()
        {
            _cantedRoll = 0f;
            _rollPivotLocal = Vector3.zero;
            _lastSightBone = null;
            _sightSwitchRoll = false;
        }

        // replaces EFT's canted sight roll, which UpdateCurrentScopeRotationPatch disables for the player while alt aim is enabled.
        // same fixed roll about the bore as vanilla, after recoil and sway (so recoil climbs as vanilla), but pivoted on the sight instead of
        // the root origin, so the sight doesn't move. The camera target (already final this frame) and the alt aim correction stay valid at any AltAimWeight.
        // Only adds what the stance isn't already holding: e.g. active aim's roll fading out on ADS, so the total doesn't dip toward upright or overshoot
        private void ApplyCantedRoll(ProceduralWeaponAnimation pwa, Transform root, float dt)
        {
            var scope = PlayerStateInstance.WeaponIsReady ? GetCurrentScope(pwa) : null;

            // weapon swap / no weapon / no aim points: roll belongs to the old weapon
            if (scope == null || scope.Bone == null)
            {
                ResetCantedRoll();
                return;
            }

            // sight switched while aimed. Compare bones, not SightNBone objects: FindAimTransforms rebuilds the list (new objects) on weapon changes
            if (pwa.IsAiming && _lastSightBone != null && scope.Bone != _lastSightBone)
                _sightSwitchRoll = true;
            if (!pwa.IsAiming)
                _sightSwitchRoll = false;
            _lastSightBone = scope.Bone;

            bool canted = Mathf.Abs(scope.Rotation) >= EFTHardSettings.Instance.SCOPE_ROTATION_THRESHOLD;
            bool engaged = PluginConfig.EnableAltAim.Value && pwa.IsAiming && canted;

            // stance + detail offset roll the springs hold at rest (settled Spring.Get() = 2 × Zero). Pose level only, not spring motion,
            // so the stance's own movement still plays out and nothing has to be handed back
            float stanceRestRoll = 2f * (StanceControllerInstance.StanceRotationSpring.Zero.y + StanceControllerInstance.OffsetRotationSpring.Zero.y);

            float target = engaged ? scope.Rotation - stanceRestRoll : 0f;

            float t = 1f - Mathf.Exp(-CalAimSpeed(pwa) * dt);
            _cantedRoll = Mathf.Lerp(_cantedRoll, target, t);

            if (Mathf.Abs(_cantedRoll - target) < CantedRollSnapThreshold)
            {
                _cantedRoll = target;
                _sightSwitchRoll = false; // switch roll finished, back to normal ADS speed
            }

            // sight is a child of root, so this is fixed prefab geometry per sight, measured before this layer touches the root
            Vector3 sightLocal = root.InverseTransformPoint(scope.Bone.position);

            if (_cantedRoll == 0f)
            {
                _rollPivotLocal = sightLocal;
                return;
            }

            // EFT switches CurrentScope instantly. Jumping the pivot to the new sight while rolled would snap the gun by the lever arm
            // between the sights, so the pivot eases to the new sight in root space at the roll's rate instead
            _rollPivotLocal = Vector3.Lerp(_rollPivotLocal, sightLocal, t);

            // rotation part equals root.localRotation *= Quaternion.Euler(0, _cantedRoll, 0), vanilla's order and sign:
            // AngleAxis(a, root.up) * root.rotation == root.rotation * AngleAxis(a, Vector3.up).
            // Only the pivot differs, the sight sits on the roll axis so it stays where it is
            root.RotateAround(root.TransformPoint(_rollPivotLocal), root.up, _cantedRoll);
        }
    }
}
