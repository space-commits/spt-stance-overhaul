using RealismCommonLib.StateControllers.InstanceState;
using StanceOverhaul.Enums;
using StanceOverhaul.SubSystem;
using StanceOverhaul.Stances;
using StanceOverhaul.Events;
using UnityEngine;
using static RealismCommonLib.Plugin;
using static StanceOverhaul.Plugin;

namespace StanceOverhaul.State
{

    /// <summary>
    /// Represents a transition from one stance to another, including the source and target stances.
    /// Used to ensure that stance slots are aware of the context of the transition they are part of
    /// </summary>
    internal class StanceTransitionContext
    {
        /// <summary>
        /// The stance that is being transitioned from, if there is a stance transition occuring.
        /// </summary>
        public EStanceType From { get; }

        /// <summary>
        /// The stance that is being transitioned to, if there is a stance transition occuring.
        /// </summary>
        public EStanceType To { get; }

        public StanceTransitionContext(EStanceType from, EStanceType to)
        {
            From = from;
            To = to;
        }
    }

    internal class StanceState : ISubSystem
    {
        // scaled by the weapon's original aim speed, same as StanceAimSystem's ADS offset
        private const float AimFadeInSpeed = 14f;
        private const float AimFadeOutSpeed = 15f;
        private const float AimWeightSnapThreshold = 0.001f;

        private StanceSlot? _primarySlot;

        private StanceSlot? PrimarySlot
        {
            get
            {
                return _primarySlot;
            }
            set
            {
                _primarySlot = value;
                StanceEvents.RaiseOnPrimaryStanceChanged();
            }

        }
        private StanceSlot? _incoming;

        private bool _incomingPaused;

        private Vector3 _smoothedPosition;
        private Vector3 _smoothedRotation;
        private Vector3 _smoothedAltAimHeld;

        // 0 = not aiming, 1 = fully aimed, eased so each stance's AimPositionScale/AimRotationScale fades with ADS
        private float _aimWeight;

        public Vector3 StancePosition { get; private set; }
        public Vector3 StanceRotation { get; private set; }

        /// <summary>
        /// The part of StancePosition on axes a stance marks with AltAimCorrectedPositionAxes, smoothed the same way.
        /// Alt aim treats it as part of the aimed pose instead of excluding it like other stance motion.
        /// </summary>
        public Vector3 AltAimHeldPosition { get; private set; }

        public EStanceType ActiveStanceType
        {
            get
            {
                return ActiveStance?.StanceType ?? EStanceType.None;
            }
        }

        /// <summary>
        /// The stance that is active or being transitioned to.
        /// Use this for checking what the active or current stance is, not PrimaryStance, 
        /// as PrimaryStance can be heading to idle or being blended out to another stance.
        /// </summary>
        public IStance? ActiveStance
        {
            get
            {
                if (_incoming != null && !_incomingPaused && _incoming.IsAtOrHeadingToActivePose)
                    return _incoming.Stance;

                if (PrimarySlot != null && PrimarySlot.IsAtOrHeadingToActivePose)
                    return PrimarySlot.Stance;

                return null;
            }
        }

        public bool IsTransitioning
        {
            get
            {
                return (_incoming != null && !_incomingPaused) || (PrimarySlot != null && !PrimarySlot.IsAtIdle);
            }
        }

        public bool IsTransitioningBetweenStances
        {
            get
            {
                return _incoming != null && !_incomingPaused && PrimarySlot != null && !PrimarySlot.IsAtIdle;
            }
        }

        public bool IsIdle => ActiveStance == null;

        /// <summary>
        /// How much of the given stance's pose is currently applied, 0 = none/idle, 1 = fully at pose.
        /// Follows the stance whether it's primary or incoming, entering or exiting.
        /// </summary>
        public float GetPoseWeight(EStanceType stanceType)
        {
            StanceSlot? incoming = _incomingPaused ? null : _incoming;
            bool isBlending = PrimarySlot != null && incoming != null;
            float incomingBlend = isBlending ? incoming!.DistanceToPose : 0f;
            float weight = 0f;

            if (PrimarySlot != null && PrimarySlot.Stance.StanceType == stanceType)
                weight += PrimarySlot.DistanceToPose * (isBlending ? 1f - incomingBlend : 1f);

            if (incoming != null && incoming.Stance.StanceType == stanceType)
                weight += incoming.DistanceToPose * (isBlending ? incomingBlend : 1f);

            return Mathf.Clamp01(weight);
        }

        public void RunOnAwake()
        {
        }

        public void RunOnDestroy()
        {
        }

        public void RunOnUpdate(float deltaTime)
        {
            UpdateStanceState(deltaTime);
        }

        private void UpdateStanceState(float deltaTime)
        {
            //update primary
            PrimarySlot?.StanceSlotUpdate(deltaTime);

            //check blend threshold - unpause incoming if met
            if (_incoming != null && _incomingPaused && PrimarySlot != null)
            {
                if (PrimarySlot.IsHeadingToIdle
                && PrimarySlot.IdleProximity >= _incoming.Stance.BlendIntoThreshold(PrimarySlot.Stance.StanceType))
                {
                    _incomingPaused = false;
                }
            }

            //upate incoming if not paused
            if (_incoming != null && !_incomingPaused)
                _incoming.StanceSlotUpdate(deltaTime);

            //cleanup completed slots: discard slots that reached idle.
            //incoming first, so a faded-out incoming and an idle primary can both go in the same frame
            if (_incoming != null && !_incomingPaused && _incoming.IsAtIdle)
                _incoming = null;

            //an idle primary still holds (1 - w) of the cross-fade: keep it until the incoming reaches its pose or fades out,
            //otherwise the incoming is promoted mid-blend and drawn at full weight in one frame
            bool incomingIsBlending = _incoming != null && !_incomingPaused && !_incoming.IsAtPose;
            if (PrimarySlot?.IsAtIdle == true && !incomingIsBlending)
                PrimarySlot = null;

            //promote incoming if primary is gone
            if (PrimarySlot == null && _incoming != null)
            {
                PrimarySlot = _incoming;
                _incoming = null;
                _incomingPaused = false;
            }

            UpdateAimSpeed();
            //UpdateAimState();

            UpdateTransforms(deltaTime);
        }

        /// <summary>
        /// Aproximates how far aiming has progressed. 
        /// Used to fade stance axes in and out while aiming.
        /// </summary>
        /// <param name="deltaTime"></param>
        private void UpdateAimWeight(float deltaTime)
        {
            bool isAiming = AimStateInstance.IsAiming;
            float target = isAiming ? 1f : 0f;
            float baseSpeed = isAiming ? AimFadeInSpeed : AimFadeOutSpeed;
            float finalSpeed = StanceControllerInstance.StatsHandlerInstance.GetAltAimSpeedModifier(baseSpeed);
            float t = 1f - Mathf.Exp(-finalSpeed * deltaTime);
            _aimWeight = Mathf.Lerp(_aimWeight, target, t);

            if (Mathf.Abs(_aimWeight - target) < AimWeightSnapThreshold)
                _aimWeight = target;
        }

        // scaled per slot, before the cross-fade, so stances with different aim scales blend without popping
        private Vector3 EvaluatePosition(StanceSlot slot)
        {
            return ApplyAimScale(slot.EvaluatePosition(), slot.Stance.AimPositionScale);
        }

        private Vector3 EvaluateRotation(StanceSlot slot)
        {
            return ApplyAimScale(slot.EvaluateRotation(), slot.Stance.AimRotationScale);
        }

        // already aim-scaled, masked to the axes alt aim treats as part of the aimed pose
        private Vector3 EvaluateAltAimHeld(StanceSlot slot)
        {
            return Vector3.Scale(EvaluatePosition(slot), slot.Stance.AltAimCorrectedPositionAxes);
        }

        private Vector3 ApplyAimScale(Vector3 value, Vector3 aimScale)
        {
            if (_aimWeight <= 0f) return value;
            return Vector3.Scale(value, Vector3.Lerp(Vector3.one, aimScale, _aimWeight));
        }

        private void UpdateTransforms(float deltaTime)
        {
            UpdateAimWeight(deltaTime);

            //evaluate output from active slots
            var rawPos = Vector3.zero;
            var rawRot = Vector3.zero;
            var rawHeld = Vector3.zero;

            if (PrimarySlot != null && _incoming != null && !_incomingPaused)
            {
                // DistanceToPose, not Progress: a cancelled incoming can be on its exit curve, where Progress runs the other way
                float weight = _incoming.DistanceToPose;
                rawPos = Vector3.Lerp(EvaluatePosition(PrimarySlot), EvaluatePosition(_incoming), weight);
                rawRot = Vector3.Lerp(EvaluateRotation(PrimarySlot), EvaluateRotation(_incoming), weight);
                rawHeld = Vector3.Lerp(EvaluateAltAimHeld(PrimarySlot), EvaluateAltAimHeld(_incoming), weight);
            }
            else if (PrimarySlot != null)
            {
                rawPos = EvaluatePosition(PrimarySlot);
                rawRot = EvaluateRotation(PrimarySlot);
                rawHeld = EvaluateAltAimHeld(PrimarySlot);
            }
            else if (_incoming != null)
            {
                rawPos = EvaluatePosition(_incoming);
                rawRot = EvaluateRotation(_incoming);
                rawHeld = EvaluateAltAimHeld(_incoming);
            }

            //output smoothing
            float smoothFactor = Mathf.Clamp01(deltaTime * PluginConfig.StanceBlendSpeed.Value);
            _smoothedPosition = Vector3.Lerp(_smoothedPosition, rawPos, smoothFactor);
            _smoothedRotation = Vector3.Lerp(_smoothedRotation, rawRot, smoothFactor);
            _smoothedAltAimHeld = Vector3.Lerp(_smoothedAltAimHeld, rawHeld, smoothFactor);

            StancePosition = _smoothedPosition;
            StanceRotation = _smoothedRotation;
            AltAimHeldPosition = _smoothedAltAimHeld;
        }

        //Move to StanceAimHandler
        //eases EFT's aim speed (camera to sights) while aiming during a stance transition, so the camera doesn't follow the stance animation too closely.
        //only while aiming: out of ADS the camera doesn't track the gun. Alt aim (gun to camera) is eased separately by its own curve, and by a return curve out of ADS
        private void UpdateAimSpeed()
        {
            StanceSlot? slot = AimSpeedSlot;
            float aimSpeedModifier = AimStateInstance.IsAiming && slot != null ? slot.EvaluateAimSpeed() : 1f;

            StanceControllerInstance.PwaAimSpeed = StanceControllerInstance.PwaOriginalAimSpeed * aimSpeedModifier;
        }

        private StanceSlot? AimSpeedSlot => _incoming != null && _incomingPaused == false ? _incoming : PrimarySlot;

        // read when the offset is applied, not cached in Update: aiming can change between the two, and using the old value skips the curve for a frame
        public float AltAimSpeedModifier
        {
            get
            {
                StanceSlot? slot = AimSpeedSlot;
                if (slot == null) return 1f;

                return AimStateInstance.IsAiming ? slot.EvaluateAltAimSpeed() : slot.EvaluateAltAimReturnSpeed();
            }
        }

        public void RequestStance(IStance stance)
        {
            // no active stance: simple enter
            if (PrimarySlot == null && _incoming == null)
            {
                var transition = new StanceTransitionContext(EStanceType.None, stance.StanceType);

                PrimarySlot = new StanceSlot(stance, ECurveType.Enter, 0f, +1, transition);
                stance.OnEnter();
                return;
            }

            // same stance as primary: never toggles, only an exit needs turning around.
            // An incoming that is fading out after a cancel is left to finish fading, the primary takes over again
            if (PrimarySlot?.Stance == stance && (_incoming == null || _incoming.IsHeadingToIdle))
            {
                if (PrimarySlot.IsHeadingToIdle) // heading to idle -> reverse toward pose
                {
                    PrimarySlot.Transition = new StanceTransitionContext(EStanceType.None, stance.StanceType);

                    PrimarySlot.Direction *= -1;
                    stance.OnEnter();
                }
                return;
            }

            //same stance is incoming: already on its way in, or fading out after a cancel -> turn it back around
            if (_incoming?.Stance == stance)
            {
                if (_incoming.IsHeadingToIdle)
                {
                    _incoming.Transition = new StanceTransitionContext(PrimarySlot?.Stance.StanceType ?? EStanceType.None, stance.StanceType);
                    _incoming.Direction *= -1;
                    stance.OnEnter();
                }
                return;
            }

            //third stance during blend. A = Null, B = Primary, C = Incoming. B -> C.
            if (_incoming != null)
            {
                //collapse: drop primary, promote incoming, start its exit
                //current incoming becomes the active stance.  Old primary is abandoned.
                PrimarySlot = _incoming; //incoming becomes primary, and will be blended out to the new stance
                _incoming = null;

                //start exit of new primary
                var transition = new StanceTransitionContext(PrimarySlot.Stance.StanceType, stance.StanceType);
                BeginExit(PrimarySlot, transition);

                //start new incoming
                _incoming = new StanceSlot(stance, ECurveType.Enter, 0f, +1, transition);
                _incomingPaused = true;
                stance.OnEnter();

                return;
            }

            //normal transition A -> B
            if (PrimarySlot != null)
            {
                var transition = new StanceTransitionContext(PrimarySlot.Stance.StanceType, stance.StanceType);

                BeginExit(PrimarySlot, transition);

                _incoming = new StanceSlot(stance, ECurveType.Enter, 0f, +1, transition);
                _incomingPaused = true;
                stance.OnEnter();
            }
        }

        public void BeginExit(StanceSlot slot, StanceTransitionContext transition)
        {
            // already heading to idle: a stance queued behind it may slow it to the stance-to-stance speed,
            // but cancelling must not speed it back up to the to-idle speed mid-move
            if (slot.IsHeadingToIdle)
            {
                if (transition.To != EStanceType.None)
                    slot.Transition = transition;

                return;
            }

            slot.Transition = transition;

            if (slot.Direction == 0) //holding -> switch to exit curve
            {
                slot.ActiveCurveType = ECurveType.Exit;
                slot.Progress = 0f;
                slot.Direction = +1;
            }
            else // heading to pose -> change direction to idle, stay on the same curve, just reverse
            {
                slot.Direction *= -1;
            }

            slot.Stance.OnExit();
        }

        // public void CancelAll()
        // {
        //     if (PrimarySlot != null)
        //     {
        //         var transition = new StanceTransitionContext(PrimarySlot.Stance.StanceType, EStanceType.None);
        //         BeginExit(PrimarySlot, transition);
        //     }

        //     // Incoming hasn't started yet, discard it.
        //     if (_incomingPaused)
        //     {
        //         _incoming = null;
        //         _incomingPaused = false;
        //     }
        //     else if (_incoming != null) // incoming is already blending, exit it
        //     {
        //         var transition = new StanceTransitionContext(_incoming.Stance.StanceType, EStanceType.None);
        //         BeginExit(_incoming, transition);
        //     }
        // }

        public void CancelAll()
        {
            if (PrimarySlot != null)
            {
                var transition = new StanceTransitionContext(PrimarySlot.Stance.StanceType, EStanceType.None);
                BeginExit(PrimarySlot, transition);
            }

            if (_incoming == null)
                return;

            if (_incomingPaused) // never drawn yet, nothing to fade
            {
                _incoming.Stance.OnExit();   // OnEnter ran when it was requested, keep the pair balanced
                _incoming = null;
                _incomingPaused = false;
            }
            else // already blending: fade it out from where it is instead of dropping its weight in one frame.
                 // Cleanup keeps an idle primary until the incoming is gone, so it is never promoted at full weight.
            {
                BeginExit(_incoming, new StanceTransitionContext(_incoming.Stance.StanceType, EStanceType.None)); // calls OnExit
            }
        }
    }
}