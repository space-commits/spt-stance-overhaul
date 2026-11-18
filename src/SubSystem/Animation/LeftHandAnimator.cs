using EFT;
using UnityEngine;
using StanceOverhaul.Enums;
using RealismCommonLib.Events;
using StanceOverhaul.Events;
using RealismCommonLib.Utils;
using static RealismCommonLib.Plugin;
using static StanceOverhaul.Plugin;
using StanceOverhaul.Stances;

namespace StanceOverhaul.SubSystem.Animator
{
    internal class LeftHandAnimaor : ISubSystem
    {
        private DelayTimer _leftHandTimer = new DelayTimer(0.8f);
        private DelayTimer _propSoundBlockTimer = new DelayTimer(2f);
        private bool _overrideLeftHand = false;
        private bool _blockSoundEffect = false;
        private bool _updateOverride = false;

        // Hand speed is a multiple of the current stance transition speed, so it keeps up with config, weight and strength
        private const float HAND_IN_RATE_SCALE = 1.25f;
        private const float HAND_OUT_RATE_SCALE = 1.25f;
        private const float HAND_DELAY_FRACTION = 0f; // delay before the hand goes back on the pistol, as a fraction of the transition

        // Hand let go for doors, pickups etc, taken back once EFT is done with it
        private const float RESUME_MIN_DELAY = 0.3f;
        private bool _suspended = false;
        private float _suspendTime;

        private GameObject _leftHandMarkerGO;
        private GripPose _leftHandMarkerGrip;

        // Marker pose before the clearance offset is added, so the offset doesn't get smoothed
        private Vector3 _markerBasePos = Vector3.zero;
        private Quaternion _markerBaseRot = Quaternion.identity;

        // Past 180 the IK slerp flips and swings the hand the wrong way
        private const float MAX_CLEARANCE_MARKER_ANGLE = 170f;

        // How far the hand moves out of the way when leaving the pistol, so the fingers don't clip through the right hand
        private static readonly Vector3 RELEASE_CLEARANCE_POSITION = new Vector3(-0.075f, 0f, 0f); // metres, marker local, -x is left
        private static readonly Vector3 RELEASE_CLEARANCE_ROTATION = new Vector3(0f, -20f, 0f);    // degrees
        private const float RELEASE_CLEARANCE_EXPONENT = 1f; // peaks at IK weight 1 / (n + 1), keep >= 0.5

        public bool BlockLeftHandSFX
        {
            get
            {
                return _blockSoundEffect;
            }
        }

        public bool IsDoingOverride
        {
            get
            {
                return _updateOverride;
            }
        }

        public Vector3 LeftHandMarkerPosition
        {
            get
            {
                return _leftHandMarkerGrip.transform.position;
            }
        }
        public Quaternion LeftHandMarkerRotation
        {
            get
            {
                return _leftHandMarkerGrip.transform.rotation;
            }
        }
        public Vector3 LeftHandPositionTargetOffset { get; private set; } = Vector3.zero;
        public Vector3 LeftHandRotationTargetOffset { get; private set; } = Vector3.zero;

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
            StanceEvents.OnStanceEntered += StanceRemoveLeftHand;
            StanceEvents.OnStanceExitedRef += StanceAttachLeftHand;
            StanceEvents.OnPlayerLoadRef += OnPlayerLoad;
            PlayerEvents.OnWeaponSwap += ResetLeftHand;
            PlayerEvents.OnSwappedFromItemToGun += ResetLeftHand;
            PlayerEvents.OnSwappedFromGunToItem += ResetLeftHand;
            StanceEvents.OnTransformsInit += ResetLeftHand;
            StanceEvents.OnHandsInteraction += SuspendForInteraction;
        }

        public void RunOnDestroy()
        {
            StanceEvents.OnStanceEntered -= StanceRemoveLeftHand;
            StanceEvents.OnStanceExitedRef -= StanceAttachLeftHand;
            StanceEvents.OnPlayerLoadRef -= OnPlayerLoad;
            PlayerEvents.OnSwappedFromItemToGun -= ResetLeftHand;
            PlayerEvents.OnSwappedFromGunToItem -= ResetLeftHand;
            StanceEvents.OnTransformsInit -= ResetLeftHand;
            StanceEvents.OnHandsInteraction -= SuspendForInteraction;
        }

        public void RunOnUpdate(float deltaTime)
        {
            if (_leftHandMarkerGO == null)
                return;

            TryResumeAfterInteraction();
            UpdateLeftHandTargets();
            UpdateTransforms();
        }

        private void OnPlayerLoad(Player player)
        {
            _leftHandMarkerGO = new GameObject("ProceduralLeftHandTarget");
            _leftHandMarkerGO.transform.SetParent(player.gameObject.transform, false);

            _leftHandMarkerGrip = _leftHandMarkerGO.AddComponent<GripPose>();
            _leftHandMarkerGrip.Hand = GripPose.EHand.Left;
            _leftHandMarkerGrip.GripType = GripPose.EGripType.Common;
            _leftHandMarkerGrip.DontCache = true;
            _leftHandMarkerGrip.transform.localPosition = Vector3.zero;
            _leftHandMarkerGrip.transform.localRotation = Quaternion.identity;
            _markerBasePos = Vector3.zero;
            _markerBaseRot = Quaternion.identity;
        }

        private void StanceRemoveLeftHand(IStance stance)
        {
            if (stance.StanceType == EStanceType.PatrolStance && WeaponStateInstance.TreatAsPistol)
            {
                EngageLeftHand();
            }
        }

        private void EngageLeftHand()
        {
            PlayerStateInstance.Player.LeftHandInteractionTarget = _leftHandMarkerGrip;
            _leftHandTimer.Stop(); // in case patrol is re-entered during the exit delay
            PlayerStateInstance.Player.HandsAnimator.ShowCompass(true);
            _propSoundBlockTimer.Start();
            _overrideLeftHand = true;
            _updateOverride = true;
            _suspended = false;
        }

        // Exiting patrol, hand still held off the pistol
        private bool IsInReturnDelay => !_overrideLeftHand && _leftHandTimer.IsActive;

        // Doors check UseLeftHand in the same frame, so it has to be cleared here. ShowCompass(false) doesn't clear it.
        private void SuspendForInteraction()
        {
            bool returning = IsInReturnDelay; // the compass pose is still up during the exit delay, which blocks doors too
            if (!_overrideLeftHand && !returning)
                return;

            var player = PlayerStateInstance.Player;
            _leftHandTimer.Stop();
            player.HandsAnimator.ShowCompass(false);
            AnimationControllerParametersTable.SetUseLeftHand(player.HandsAnimator.Animator, false);
            player.LeftHandInteractionTarget = null;
            player.ThirdIkWeight.Value = 0f;
            _updateOverride = false;
            _suspended = !returning; // already leaving patrol, nothing to resume
            _suspendTime = Time.time;
        }

        // Door states own the IK target until they exit, so wait for those as well
        private void TryResumeAfterInteraction()
        {
            if (!_suspended || Time.time - _suspendTime < RESUME_MIN_DELAY)
                return;

            var player = PlayerStateInstance.Player;
            var mc = player.MovementContext;
            bool busy =
                player.HandsController.IsInInteraction() ||
                mc.GetHandProgress() > 0f ||
                mc.IsAnimatorInteractionOn ||
                player.CurrentStateName == EPlayerState.DoorInteraction ||
                player.CurrentStateName == EPlayerState.BreachDoor;

            if (!busy)
            {
                EngageLeftHand();
            }
        }

        // Instant return for swaps and weapon init, also cancels a pending exit delay
        private void ResetLeftHand()
        {
            _suspended = false;

            if (_overrideLeftHand || IsInReturnDelay)
            {
                _leftHandTimer.Stop();
                PlayerStateInstance.Player.HandsAnimator.ShowCompass(false);
                _propSoundBlockTimer.Start();
                _overrideLeftHand = false;
                _updateOverride = true;
            }
        }

        // Hold the hand where it is for the exit delay, then the timer fades it back onto the pistol
        private void StanceAttachLeftHand(IStance stance)
        {
            if (stance.StanceType == EStanceType.PatrolStance && WeaponStateInstance.TreatAsPistol && _overrideLeftHand)
            {
                _suspended = false;
                _overrideLeftHand = false;
                _updateOverride = false;
                _propSoundBlockTimer.Start();
                _leftHandTimer.Start();
            }
        }

        private void SetDefaultPosition()
        {
            LeftHandPositionTargetOffset += new Vector3(-0.13f, -0.18f, 0.2f);
            LeftHandRotationTargetOffset += new Vector3(-60f, 190f, -20f);
        }

        private void UpdateLeftHandTargets()
        {
            LeftHandPositionTargetOffset = Vector3.zero;
            LeftHandRotationTargetOffset = Vector3.zero;

            if (DoOffsets)
            {
                SetDefaultPosition();
            }

            // 1/s if there's no stance slot, e.g. right after a swap
            float stanceSpeed = StanceControllerInstance.CurrentTransitionSpeed;
            float baseRate = stanceSpeed > 0f ? stanceSpeed : 1f;

            // set every frame so it picks up the pistol stance's speed once that starts blending in
            _leftHandTimer.Duration = HAND_DELAY_FRACTION / baseRate;

            if (_leftHandTimer.Update())
            {
                PlayerStateInstance.Player.HandsAnimator.ShowCompass(false);
                _updateOverride = true; // _overrideLeftHand is false here, so this fades the hand back onto the pistol
            }

            if (!_propSoundBlockTimer.Update())
            {
                _blockSoundEffect = true;
            }
            else
            {
                _blockSoundEffect = false;
            }

            if (_updateOverride)
            {
                var speed = baseRate * (_overrideLeftHand ? HAND_IN_RATE_SCALE : HAND_OUT_RATE_SCALE);
                PlayerStateInstance.Player.ThirdIkWeight.Value = Mathf.MoveTowards(PlayerStateInstance.Player.ThirdIkWeight.Value, _overrideLeftHand ? 1f : 0f, speed * Time.deltaTime);
            }
        }

        // EFT's IK moves the hand in a straight line from the pistol to the marker, which drags the fingers through the
        // right hand. Offsetting the marker by norm * (1 - w)^n gives a visible detour of w * norm * (1 - w)^n, which is 0
        // at both ends and hits the RELEASE_CLEARANCE values at w = 1 / (n + 1). Only used when leaving the pistol.
        private float GetClearanceScale(float ikWeight)
        {
            if (!_overrideLeftHand)
                return 0f;

            float n = RELEASE_CLEARANCE_EXPONENT;
            float peakWeight = 1f / (n + 1f);
            float norm = 1f / (peakWeight * Mathf.Pow(1f - peakWeight, n));
            return norm * Mathf.Pow(1f - Mathf.Clamp01(ikWeight), n);
        }

        private Quaternion GetClearanceRotation(float scale)
        {
            if (scale <= 0f)
                return Quaternion.identity;

            Quaternion.Euler(RELEASE_CLEARANCE_ROTATION).ToAngleAxis(out float angle, out Vector3 axis);

            // ToAngleAxis gives 0-360, take the short way round
            if (angle > 180f)
            {
                angle = 360f - angle;
                axis = -axis;
            }

            if (angle < 0.01f)
                return Quaternion.identity;

            return Quaternion.AngleAxis(Mathf.Min(angle * scale, MAX_CLEARANCE_MARKER_ANGLE), axis);
        }

        private void UpdateTransforms()
        {
            _markerBasePos = Vector3.Lerp(_markerBasePos, LeftHandPositionTargetOffset, Time.deltaTime * 3f);
            _markerBaseRot = Quaternion.Lerp(_markerBaseRot, Quaternion.Euler(LeftHandRotationTargetOffset), Time.deltaTime * 3f);

            float clearanceScale = GetClearanceScale(PlayerStateInstance.Player.ThirdIkWeight.Value);
            Vector3 clearancePos = RELEASE_CLEARANCE_POSITION * clearanceScale;

            _leftHandMarkerGrip.transform.localPosition = _markerBasePos + clearancePos;
            _leftHandMarkerGrip.transform.localRotation = _markerBaseRot * GetClearanceRotation(clearanceScale);

            if (PlayerStateInstance.PWA.HandsContainer.WeaponRoot != _leftHandMarkerGO.transform.parent)
            {
                _leftHandMarkerGO.transform.SetParent(PlayerStateInstance.PWA.HandsContainer.TrackingTransform, false);
            }

        }
    }
}
