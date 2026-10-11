using RealismCommonLib.Utils;
using StanceOverhaul.Enums;
using UnityEngine;
using static RealismCommonLib.Plugin;
using static StanceOverhaul.Plugin;

namespace StanceOverhaul.Stances;

public class ShortStock : StanceBase
{
    public override EStanceType StanceType => EStanceType.ShortStock;
    public override EStanceStaminaType StaminaMode => EStanceStaminaType.Regen;

    public override Vector3Curve EnterPositionCurve => _enterPos;
    public override Vector3Curve EnterRotationCurve => _enterRot;

    public override Vector3Curve ExitPositionCurve => _exitPos;
    public override Vector3Curve ExitRotationCurve => _exitRot;

    public override float StaminaRate => PluginConfig.ShortStockStaminaRate.Value;
    public override float WalkSpeedBonus => PluginConfig.ShortStockWalkSpeedBonus.Value;
    public override float SprintAccelBonus => PluginConfig.ShortStockSprintAccelBonus.Value;
    public override float HipfireBonus => PluginConfig.ShortStockHipfireBonus.Value;
    public override bool RememberStance => true;
    public override float PumpBoltSpeedModifier => PluginConfig.ShortStockPumpBoltSpeedModifier.Value;

    public override bool BlocksFiring => false;

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

    public override AnimationCurve ExitAimSpeedCurve => CurveDrawer.GetCurve("short_exit_aim_speed", CurveAxis.X)!;

    public override AnimationCurve EnterAimSpeedCurve => CurveDrawer.GetCurve("short_enter_aim_speed", CurveAxis.X)!;

    public override float BlendIntoThreshold(EStanceType nextStance)
    {
        switch (nextStance)
        {
            case EStanceType.ActiveAiming: return PluginConfig.ShortStockBlendThresholdActiveAim.Value;
            case EStanceType.HighReady: return PluginConfig.ShortStockBlendThresholdHighReady.Value;
            case EStanceType.LowReady: return PluginConfig.ShortStockBlendThresholdLowReady.Value;
            case EStanceType.PatrolStance: return PluginConfig.ShortStockBlendThresholdPatrol.Value;
            case EStanceType.LeftShoulder: return PluginConfig.ShortStockBlendThresholdLeftShoulder.Value;
            default: return 0.4f;
        }
    }

    public override float TransitionFromModifier(EStanceType? previousStance)
    {
        float speed = PluginConfig.ShortStockSpeedModifier.Value;
        switch (previousStance)
        {
            case EStanceType.ActiveAiming: speed *= PluginConfig.ShortStockTransitionFromActiveAim.Value; return speed;
            case EStanceType.HighReady: speed *= PluginConfig.ShortStockTransitionFromHighReady.Value; return speed;
            case EStanceType.LowReady: speed *= PluginConfig.ShortStockTransitionFromLowReady.Value; return speed;
            case EStanceType.PatrolStance: speed *= PluginConfig.ShortStockTransitionFromPatrol.Value; return speed;
            case EStanceType.LeftShoulder: speed *= PluginConfig.ShortStockTransitionFromLeftShoulder.Value; return speed;
            case EStanceType.None: speed *= PluginConfig.ShortStockTransitionFromIdle.Value; return speed;
            default: return speed;
        }
    }

    public override float TransitionToSpeedModifier(EStanceType? nextStance)
    {
        float speed = PluginConfig.ShortStockSpeedModifier.Value;
        switch (nextStance)
        {
            case EStanceType.ActiveAiming: speed *= PluginConfig.ShortStockTransitionToActiveAim.Value; return speed;
            case EStanceType.HighReady: speed *= PluginConfig.ShortStockTransitionToHighReady.Value; return speed;
            case EStanceType.LowReady: speed *= PluginConfig.ShortStockTransitionToLowReady.Value; return speed;
            case EStanceType.PatrolStance: speed *= PluginConfig.ShortStockTransitionToPatrol.Value; return speed;
            case EStanceType.LeftShoulder: speed *= PluginConfig.ShortStockTransitionToLeftShoulder.Value; return speed;
            case EStanceType.None: speed *= PluginConfig.ShortStockTransitionToIdle.Value; return speed;
            default: return speed;
        }
    }

    private readonly Vector3Curve _enterPos;
    private readonly Vector3Curve _enterRot;

    private readonly Vector3Curve _exitPos;
    private readonly Vector3Curve _exitRot;

    public ShortStock()
    {
        _enterPos = CurveDrawer.GetCurve("short_enter_position")!;
        _enterRot = CurveDrawer.GetCurve("short_enter_rotation")!;

        _exitPos = CurveDrawer.GetCurve("short_exit_position")!;
        _exitRot = CurveDrawer.GetCurve("short_exit_rotation")!;
    }
}