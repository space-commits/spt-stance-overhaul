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

        public Vector3 StancePosition { get; private set; }
        public Vector3 StanceRotation { get; private set; }

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

        public bool IsIdle => ActiveStance == null;

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

            //cleanup completed slots: discard slots that reached idle
            if (PrimarySlot?.IsAtIdle == true) //&& _incoming == null
                PrimarySlot = null;

            if (_incoming != null && !_incomingPaused && _incoming.IsAtIdle)
                _incoming = null;

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

        private void UpdateTransforms(float deltaTime)
        {
            //evaluate output from active slots
            var rawPos = Vector3.zero;
            var rawRot = Vector3.zero;

            if (PrimarySlot != null && _incoming != null && !_incomingPaused)
            {
                float weight = _incoming.Progress;
                rawPos = Vector3.Lerp(PrimarySlot.EvaluatePosition(), _incoming.EvaluatePosition(), weight);
                rawRot = Vector3.Lerp(PrimarySlot.EvaluateRotation(), _incoming.EvaluateRotation(), weight);
            }
            else if (PrimarySlot != null)
            {
                rawPos = PrimarySlot.EvaluatePosition();
                rawRot = PrimarySlot.EvaluateRotation();
            }
            else if (_incoming != null)
            {
                rawPos = _incoming.EvaluatePosition();
                rawRot = _incoming.EvaluateRotation();
            }

            //output smoothing
            float smoothFactor = Mathf.Clamp01(deltaTime * PluginConfig.StanceBlendSpeed.Value);
            _smoothedPosition = Vector3.Lerp(_smoothedPosition, rawPos, smoothFactor);
            _smoothedRotation = Vector3.Lerp(_smoothedRotation, rawRot, smoothFactor);

            StancePosition = _smoothedPosition;
            StanceRotation = _smoothedRotation;
        }

        //Move to StanceAimHandler
        private void UpdateAimSpeed()
        {
            float aimSpeedModifier = 1f;

            if (AimStateInstance.IsAiming)
            {
                if (_incoming != null && _incomingPaused == false)
                    aimSpeedModifier = _incoming.EvaluateAimSpeed();
                else if (PrimarySlot != null)
                    aimSpeedModifier = PrimarySlot.EvaluateAimSpeed();
            }

            StanceControllerInstance.PwaAimSpeed = StanceControllerInstance.PwaOriginalAimSpeed * aimSpeedModifier;
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

            // same stance as primary: toggle exit or reverse
            if (PrimarySlot?.Stance == stance && _incoming == null)
            {
                //holding -> switch to exit curve
                if (PrimarySlot.Direction == 0)
                {
                    PrimarySlot.Transition = new StanceTransitionContext(stance.StanceType, EStanceType.None); ;

                    PrimarySlot.ActiveCurveType = ECurveType.Exit;
                    PrimarySlot.Progress = 0f;
                    PrimarySlot.Direction = +1;
                    stance.OnExit();
                }
                else if (PrimarySlot.IsHeadingToIdle) // heading to idle -> reverse toward pose
                {
                    PrimarySlot.Transition = new StanceTransitionContext(EStanceType.None, stance.StanceType);

                    PrimarySlot.Direction *= -1;
                    stance.OnEnter();
                }
                else // heading to pose -> reverse toward idle
                {
                    PrimarySlot.Transition = new StanceTransitionContext(stance.StanceType, EStanceType.None);

                    PrimarySlot.Direction *= -1;
                    stance.OnExit();
                }
                return;
            }

            //same stance is incoming, discard current stance and promote incoming to primary, start its exit
            if (_incoming?.Stance == stance)
            {
                PrimarySlot = _incoming;
                _incoming = null;
                _incomingPaused = false;

                var transition = new StanceTransitionContext(PrimarySlot.Stance.StanceType, EStanceType.None);
                BeginExit(PrimarySlot, transition);

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
            slot.Transition = transition;

            if (slot.Direction == 0) //holding -> switch to exit curve
            {
                slot.ActiveCurveType = ECurveType.Exit;
                slot.Progress = 0f;
                slot.Direction = +1;
            }
            else if (slot.IsHeadingToIdle) // already heading to idle: no change needed
            {
                return;
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

            // Never reverse an incoming slot: mid cross-fade it drives the blend weight backwards and
            // pops to full weight on promotion. The primary is already exiting, so let it finish alone.
            if (_incoming != null)
            {
                _incoming.Stance.OnExit();   // OnEnter ran when it was requested, keep the pair balanced
                _incoming = null;
                _incomingPaused = false;
            }
        }
    }
}