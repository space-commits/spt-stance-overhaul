using RealismCommonLib.Utils;
using StanceOverhaul.Enums;
using UnityEngine;
using static RealismCommonLib.Plugin;
using static StanceOverhaul.Plugin;

namespace StanceOverhaul.Stances;

public class LeftShoulder : StanceBase
{
    public override EStanceType StanceType => EStanceType.LeftShoulder;

    public override Vector3Curve EnterPositionCurve => _enterPos;
    public override Vector3Curve EnterRotationCurve => _enterRot;

    public override Vector3Curve ExitPositionCurve => _exitPos;
    public override Vector3Curve ExitRotationCurve => _exitRot;

    public override float StaminaRate => PluginConfig.LeftShoulderStaminaRate.Value;
    public override float WalkSpeedBonus => PluginConfig.LeftShoulderWalkSpeedBonus.Value;
    public override float SprintAccelBonus => PluginConfig.LeftShoulderSprintAccelBonus.Value;
    public override float HipfireBonus => PluginConfig.LeftShoulderHipfireBonus.Value;

    public override bool BlocksFiring => false;

    public override AnimationCurve ExitAimSpeedCurve => CurveDrawer.GetCurve("left_exit_aim_speed", CurveAxis.X)!;

    public override AnimationCurve EnterAimSpeedCurve => CurveDrawer.GetCurve("left_enter_aim_speed", CurveAxis.X)!;

    public override EStanceReloadType[] ReloadTypesThatPauseStance => new EStanceReloadType[]
    {
        EStanceReloadType.Magazine,
        EStanceReloadType.QuickReload,
        EStanceReloadType.Tube,
        EStanceReloadType.Top,
        EStanceReloadType.Revolver,
        EStanceReloadType.CheckAmmo,
        EStanceReloadType.CheckChamber,
        EStanceReloadType.Rechamber,
        EStanceReloadType.ClearMalfunction,
        EStanceReloadType.InsertMagazine,
        EStanceReloadType.RemoveMagazine
    };

    public override float BlendIntoThreshold(EStanceType nextStance)
    {
        switch (nextStance)
        {
            case EStanceType.ActiveAiming: return PluginConfig.LeftShoulderBlendThresholdActiveAim.Value;
            case EStanceType.HighReady: return PluginConfig.LeftShoulderBlendThresholdHighReady.Value;
            case EStanceType.LowReady: return PluginConfig.LeftShoulderBlendThresholdLowReady.Value;
            case EStanceType.PatrolStance: return PluginConfig.LeftShoulderBlendThresholdPatrol.Value;
            case EStanceType.ShortStock: return PluginConfig.LeftShoulderBlendThresholdShortStock.Value;
            default: return 0.2f;
        }
    }

    public override float TransitionFromModifier(EStanceType? previousStance)
    {
        float speed = PluginConfig.LeftShoulderSpeedModifier.Value;
        switch (previousStance)
        {
            case EStanceType.ActiveAiming: speed *= PluginConfig.LeftShoulderTransitionFromActiveAim.Value; return speed;
            case EStanceType.HighReady: speed *= PluginConfig.LeftShoulderTransitionFromHighReady.Value; return speed;
            case EStanceType.LowReady: speed *= PluginConfig.LeftShoulderTransitionFromLowReady.Value; return speed;
            case EStanceType.PatrolStance: speed *= PluginConfig.LeftShoulderTransitionFromPatrol.Value; return speed;
            case EStanceType.ShortStock: speed *= PluginConfig.LeftShoulderTransitionFromShortStock.Value; return speed;
            case EStanceType.None: speed *= PluginConfig.LeftShoulderTransitionFromIdle.Value; return speed;
            default: return speed;
        }
    }

    public override float TransitionToSpeedModifier(EStanceType? nextStance)
    {
        float speed = PluginConfig.LeftShoulderSpeedModifier.Value;
        switch (nextStance)
        {
            case EStanceType.ActiveAiming: speed *= PluginConfig.LeftShoulderTransitionToActiveAim.Value; return speed;
            case EStanceType.HighReady: speed *= PluginConfig.LeftShoulderTransitionToHighReady.Value; return speed;
            case EStanceType.LowReady: speed *= PluginConfig.LeftShoulderTransitionToLowReady.Value; return speed;
            case EStanceType.PatrolStance: speed *= PluginConfig.LeftShoulderTransitionToPatrol.Value; return speed;
            case EStanceType.ShortStock: speed *= PluginConfig.LeftShoulderTransitionToShortStock.Value; return speed;
            case EStanceType.None: speed *= PluginConfig.LeftShoulderTransitionToIdle.Value; return speed;
            default: return speed;
        }
    }

    private Vector3Curve _enterPos;
    private readonly Vector3Curve _enterRot;

    private Vector3Curve _exitPos;
    private readonly Vector3Curve _exitRot;

    public LeftShoulder()
    {
        _enterPos = CurveDrawer.GetCurve("left_enter_position")?.Clone()!;
        _enterRot = CurveDrawer.GetCurve("left_enter_rotation")?.Clone()!;

        _exitPos = CurveDrawer.GetCurve("left_exit_position")?.Clone()!;
        _exitRot = CurveDrawer.GetCurve("left_exit_rotation")?.Clone()!;
    }

    public override void CorrectPositionCurveX()
    {
        _enterPos = CurveDrawer.GetCurve("left_enter_position")?.Clone()!;
        _exitPos = CurveDrawer.GetCurve("left_exit_position")?.Clone()!;

        _enterPos = StanceUtils.NormalizeXPositionCurve(_enterPos);
        _exitPos = StanceUtils.NormalizeXPositionCurve(_exitPos);
    }
}
