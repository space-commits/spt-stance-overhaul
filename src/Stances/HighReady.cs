using RealismCommonLib.Utils;
using StanceOverhaul.Enums;
using UnityEngine;
using static RealismCommonLib.Plugin;
using static StanceOverhaul.Plugin;

namespace StanceOverhaul.Stances;

public class HighReady : StanceBase
{
    public override EStanceType StanceType => EStanceType.HighReady;
    public override EStanceStaminaType StaminaMode => EStanceStaminaType.Regen;

    public override Vector3Curve EnterPositionCurve => _enterPos;
    public override Vector3Curve EnterRotationCurve => _enterRot;

    public override Vector3Curve ExitPositionCurve => _exitPos;
    public override Vector3Curve ExitRotationCurve => _exitRot;

    public override float StaminaRate => PluginConfig.HighReadyStaminaRate.Value;
    public override float WalkSpeedBonus => PluginConfig.HighReadyWalkSpeedBonus.Value;
    public override float SprintAccelBonus => PluginConfig.HighReadySprintAccelBonus.Value;
    public override bool CanDoTacSprint => true;
    public override bool RememberStance => true;
    public override bool BlocksFiring => true;

    public override float MagazineReloadSpeedModifier => PluginConfig.HighReadyMagazineReloadSpeedModifier.Value;
    public override float TubeReloadSpeedModifier => PluginConfig.HighReadyTubeReloadSpeedModifier.Value;
    public override float TopReloadSpeedModifier => PluginConfig.HighReadyTopReloadSpeedModifier.Value;
    public override float WeaponManipSpeedModifier => PluginConfig.HighReadyWeaponManipSpeedModifier.Value;

    public override EStanceReloadType[] ReloadTypesThatPauseStance => new EStanceReloadType[]
    {
        EStanceReloadType.PumpBolt,
    };

    public override AnimationCurve ExitAimSpeedCurve => CurveDrawer.GetCurve("high_ready_exit_aim_speed", CurveAxis.X)!;

    public override AnimationCurve EnterAimSpeedCurve => CurveDrawer.GetCurve("high_ready_enter_aim_speed", CurveAxis.X)!;

    public override float BlendIntoThreshold(EStanceType nextStance)
    {
        switch (nextStance)
        {
            case EStanceType.ActiveAiming: return PluginConfig.HighReadyBlendThresholdActiveAim.Value;
            case EStanceType.LowReady: return PluginConfig.HighReadyBlendThresholdLowReady.Value;
            case EStanceType.LeftShoulder: return PluginConfig.HighReadyBlendThresholdLeftShoulder.Value;
            case EStanceType.PatrolStance: return PluginConfig.HighReadyBlendThresholdPatrol.Value;
            case EStanceType.ShortStock: return PluginConfig.HighReadyBlendThresholdShortStock.Value;
            default: return 0.2f;
        }
    }

    public override float TransitionFromModifier(EStanceType? previousStance)
    {
        float speed = PluginConfig.HighReadySpeedModifier.Value;
        switch (previousStance)
        {
            case EStanceType.ActiveAiming: speed *= PluginConfig.HighReadyTransitionFromActiveAim.Value; return speed;
            case EStanceType.LowReady: speed *= PluginConfig.HighReadyTransitionFromLowReady.Value; return speed;
            case EStanceType.LeftShoulder: speed *= PluginConfig.HighReadyTransitionFromLeftShoulder.Value; return speed;
            case EStanceType.PatrolStance: speed *= PluginConfig.HighReadyTransitionFromPatrol.Value; return speed;
            case EStanceType.ShortStock: speed *= PluginConfig.HighReadyTransitionFromShortStock.Value; return speed;
            case EStanceType.None: speed *= PluginConfig.HighReadyTransitionFromIdle.Value; return speed;
            default: return speed;
        }
    }

    public override float TransitionToSpeedModifier(EStanceType? nextStance)
    {
        float speed = PluginConfig.HighReadySpeedModifier.Value;
        switch (nextStance)
        {
            case EStanceType.ActiveAiming: speed *= PluginConfig.HighReadyTransitionToActiveAim.Value; return speed;
            case EStanceType.LowReady: speed *= PluginConfig.HighReadyTransitionToLowReady.Value; return speed;
            case EStanceType.LeftShoulder: speed *= PluginConfig.HighReadyTransitionToLeftShoulder.Value; return speed;
            case EStanceType.PatrolStance: speed *= PluginConfig.HighReadyTransitionToPatrol.Value; return speed;
            case EStanceType.ShortStock: speed *= PluginConfig.HighReadyTransitionToShortStock.Value; return speed;
            case EStanceType.None: speed *= PluginConfig.HighReadyTransitionToIdle.Value; return speed;
            default: return speed;
        }
    }

    private readonly Vector3Curve _enterPos;
    private readonly Vector3Curve _enterRot;

    private readonly Vector3Curve _exitPos;
    private readonly Vector3Curve _exitRot;

    public HighReady()
    {
        _enterPos = CurveDrawer.GetCurve("high_ready_enter_position")!;
        _enterRot = CurveDrawer.GetCurve("high_ready_enter_rotation")!;

        _exitPos = CurveDrawer.GetCurve("high_ready_exit_position")!;
        _exitRot = CurveDrawer.GetCurve("high_ready_exit_rotation")!;
    }
}
