using RealismCommonLib.Utils;
using StanceOverhaul.Enums;
using UnityEngine;
using static RealismCommonLib.Plugin;
using static StanceOverhaul.Plugin;

namespace StanceOverhaul.Stances;

public class LowReady : StanceBase
{
    public override EStanceType StanceType => EStanceType.LowReady;
    public override EStanceStaminaType StaminaMode => EStanceStaminaType.Regen;

    public override Vector3Curve EnterPositionCurve => _enterPos;
    public override Vector3Curve EnterRotationCurve => _enterRot;

    public override Vector3Curve ExitPositionCurve => _exitPos;
    public override Vector3Curve ExitRotationCurve => _exitRot;

    public override float StaminaRate => PluginConfig.LowReadyStaminaRate.Value;
    public override float WalkSpeedBonus => PluginConfig.LowReadyWalkSpeedBonus.Value;
    public override float SprintAccelBonus => PluginConfig.LowReadySprintAccelBonus.Value;
    public override bool RememberStance => true;
    public override float MagazineReloadSpeedModifier => PluginConfig.LowReadyMagazineReloadSpeedModifier.Value;
    public override float TubeReloadSpeedModifier => PluginConfig.LowReadyTubeReloadSpeedModifier.Value;
    public override float TopReloadSpeedModifier => PluginConfig.LowReadyTopReloadSpeedModifier.Value;
    public override float WeaponManipSpeedModifier => PluginConfig.LowReadyWeaponManipSpeedModifier.Value;

    public override AnimationCurve ExitAimSpeedCurve => CurveDrawer.GetCurve("low_ready_exit_aim_speed", CurveAxis.X)!;

    public override AnimationCurve EnterAimSpeedCurve => CurveDrawer.GetCurve("low_ready_enter_aim_speed", CurveAxis.X)!;

    public override EStanceReloadType[] ReloadTypesThatPauseStance => new EStanceReloadType[]
    {
        EStanceReloadType.PumpBolt
    };

    public override float BlendIntoThreshold(EStanceType nextStance)
    {
        switch (nextStance)
        {
            case EStanceType.ActiveAiming: return PluginConfig.LowReadyBlendThresholdActiveAim.Value;
            case EStanceType.HighReady: return PluginConfig.LowReadyBlendThresholdHighReady.Value;
            case EStanceType.LeftShoulder: return PluginConfig.LowReadyBlendThresholdLeftShoulder.Value;
            case EStanceType.PatrolStance: return PluginConfig.LowReadyBlendThresholdPatrol.Value;
            case EStanceType.ShortStock: return PluginConfig.LowReadyBlendThresholdShortStock.Value;
            default: return 0.2f;
        }
    }

    public override float TransitionFromModifier(EStanceType? previousStance)
    {
        float speed = PluginConfig.LowReadySpeedModifier.Value;
        switch (previousStance)
        {
            case EStanceType.ActiveAiming: speed *= PluginConfig.LowReadyTransitionFromActiveAim.Value; return speed;
            case EStanceType.HighReady: speed *= PluginConfig.LowReadyTransitionFromHighReady.Value; return speed;
            case EStanceType.LeftShoulder: speed *= PluginConfig.LowReadyTransitionFromLeftShoulder.Value; return speed;
            case EStanceType.PatrolStance: speed *= PluginConfig.LowReadyTransitionFromPatrol.Value; return speed;
            case EStanceType.ShortStock: speed *= PluginConfig.LowReadyTransitionFromShortStock.Value; return speed;
            case EStanceType.None: speed *= PluginConfig.LowReadyTransitionFromIdle.Value; return speed;
            default: return speed;
        }
    }

    public override float TransitionToSpeedModifier(EStanceType? nextStance)
    {
        float speed = PluginConfig.LowReadySpeedModifier.Value;
        switch (nextStance)
        {
            case EStanceType.ActiveAiming: speed *= PluginConfig.LowReadyTransitionToActiveAim.Value; return speed;
            case EStanceType.HighReady: speed *= PluginConfig.LowReadyTransitionToHighReady.Value; return speed;
            case EStanceType.LeftShoulder: speed *= PluginConfig.LowReadyTransitionToLeftShoulder.Value; return speed;
            case EStanceType.PatrolStance: speed *= PluginConfig.LowReadyTransitionToPatrol.Value; return speed;
            case EStanceType.ShortStock: speed *= PluginConfig.LowReadyTransitionToShortStock.Value; return speed;
            case EStanceType.None: speed *= PluginConfig.LowReadyTransitionToIdle.Value; return speed;
            default: return speed;
        }
    }

    private readonly Vector3Curve _enterPos;
    private readonly Vector3Curve _enterRot;

    private readonly Vector3Curve _exitPos;
    private readonly Vector3Curve _exitRot;

    public LowReady()
    {
        _enterPos = CurveDrawer.GetCurve("low_ready_enter_position")!;
        _enterRot = CurveDrawer.GetCurve("low_ready_enter_rotation")!;

        _exitPos = CurveDrawer.GetCurve("low_ready_exit_position")!;
        _exitRot = CurveDrawer.GetCurve("low_ready_exit_rotation")!;
    }
}
