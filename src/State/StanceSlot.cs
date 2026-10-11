using StanceOverhaul.Enums;
using StanceOverhaul.Stances;
using UnityEngine;
using StanceOverhaul.Events;
using static RealismCommonLib.Plugin;
using static StanceOverhaul.Plugin;
using RealismCommonLib.Utils;

namespace StanceOverhaul.State;

internal class StanceSlot
{
    public StanceTransitionContext Transition { get; set; }
    public IStance Stance { get; }
    public ECurveType ActiveCurveType { get; set; }
    public float Progress { get; set; } // 0..1
    public int Direction { get; set; } // +1 or -1
    public float PreviousProgress { get; private set; }

    private Vector3Curve _enterPosCurve;
    private Vector3Curve _enterRotCurve;
    private Vector3Curve _exitPosCurve;
    private Vector3Curve _exitRotCurve;

    internal StanceSlot(IStance stance, ECurveType activeCurve, float progress, int direction, StanceTransitionContext transition)
    {
        Stance = stance;
        Stance.CorrectPositionCurveX(); //to prevent timing issues, this needs to be called on slot creation.

        ActiveCurveType = activeCurve;
        Direction = direction;
        Progress = progress;
        Transition = transition;

        _enterPosCurve = (WeaponStateInstance.TreatAsPistol ? Stance.PistolEnterPositionCurve : null) ?? Stance.EnterPositionCurve;
        _enterRotCurve = (WeaponStateInstance.TreatAsPistol ? Stance.PistolEnterRotationCurve : null) ?? Stance.EnterRotationCurve;
        _exitPosCurve = (WeaponStateInstance.TreatAsPistol ? Stance.PistolExitPositionCurve : null) ?? Stance.ExitPositionCurve;
        _exitRotCurve = (WeaponStateInstance.TreatAsPistol ? Stance.PistolExitRotationCurve : null) ?? Stance.ExitRotationCurve;
    }

    public bool IsAtIdle =>
        (ActiveCurveType == ECurveType.Enter && Progress <= 0f) ||
        (ActiveCurveType == ECurveType.Exit && Progress >= 1f);

    /// <summary>
    /// Is at the terminal state of the stance, and hasn't started transitioning away from it yet.
    /// </summary>
    public bool IsAtPose =>
        (ActiveCurveType == ECurveType.Enter && Progress >= 1f) ||
        (ActiveCurveType == ECurveType.Exit && Progress <= 0f);

    public bool IsHeadingToIdle =>
        (ActiveCurveType == ECurveType.Enter && Direction == -1) ||
        (ActiveCurveType == ECurveType.Exit && Direction == +1);

    public bool IsHeadingToPose =>
        (ActiveCurveType == ECurveType.Enter && Direction == +1) ||
        (ActiveCurveType == ECurveType.Exit && Direction == -1);


    /// <summary>
    /// Is at the terminal state of the stance, or heading towards it, and not heading away from it.
    /// </summary>
    public bool IsAtOrHeadingToActivePose =>
        (ActiveCurveType == ECurveType.Enter && (Direction == +1 || Progress >= 1f)) ||
        (ActiveCurveType == ECurveType.Exit && Direction == -1);

    public float IdleProximity =>
        ActiveCurveType == ECurveType.Exit ? Progress : 1f - Progress;

    public float DistanceToPose =>
        1f - IdleProximity;

    public Vector3 EvaluatePosition()
    {
        return ActiveCurveType == ECurveType.Exit
            ? _exitPosCurve.Evaluate(Progress)
            : _enterPosCurve.Evaluate(Progress);
    }

    public Vector3 EvaluateRotation()
    {
        return ActiveCurveType == ECurveType.Exit
            ? _exitRotCurve.Evaluate(Progress)
            : _enterRotCurve.Evaluate(Progress);
    }

    public float EvaluateAimSpeed()
    {
        return ActiveCurveType == ECurveType.Exit
            ? Stance.ExitAimSpeedCurve.Evaluate(Progress)
            : ActiveCurveType == ECurveType.Enter ?
            Stance.EnterAimSpeedCurve.Evaluate(Progress)
            : 1f;
    }

    public void StanceSlotUpdate(float deltaTime)
    {
        if (Direction == 0) return; // holding

        var speed = Stance.TransitionFromModifier(Transition.From) *
                    Stance.TransitionToSpeedModifier(Transition.To) *
                    PluginConfig.GlobalStanceSpeed.Value;

        speed = Mathf.Clamp(StanceControllerInstance.StatsHandlerInstance.GetStanceSpeedModifier(speed), 1f, speed);

        PreviousProgress = Progress;

        Progress = Mathf.Clamp01(
            Progress +
            Direction *
            deltaTime *
            speed);

        if (PreviousProgress < Stance.StanceHitShoulderThreshold && Progress >= Stance.StanceHitShoulderThreshold)
        {
            StanceEvents.RaiseOnStanceHitShoulder();
        }

        // reached pose end -> enter holding
        if (IsAtPose)
            Direction = 0;

        // TODO: put in a better place
        StanceControllerInstance.StanceRotationSpring.ReturnSpeed = StanceControllerInstance.StatsHandlerInstance.GetSpringReturnSpeed(Stance.StanceReturnSpeedModifier);
        StanceControllerInstance.StancePositionSpring.ReturnSpeed = StanceControllerInstance.StatsHandlerInstance.GetSpringReturnSpeed(Stance.StanceReturnSpeedModifier);

        StanceControllerInstance.StanceRotationSpring.Damping = StanceControllerInstance.StatsHandlerInstance.GetSpringDamping(Stance.StanceDampingModifier);
        StanceControllerInstance.StancePositionSpring.Damping = StanceControllerInstance.StatsHandlerInstance.GetSpringDamping(Stance.StanceDampingModifier);
    }
}