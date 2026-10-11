using RealismCommonLib.Utils;
using StanceOverhaul.Enums;
using UnityEngine;
using static RealismCommonLib.Plugin;
using static StanceOverhaul.Plugin;

namespace StanceOverhaul.Stances;

public class ActiveAim : StanceBase
{
    public override EStanceType StanceType => EStanceType.ActiveAiming;
    public override EStanceStaminaType StaminaMode => EStanceStaminaType.Drain;

    public override Vector3Curve EnterPositionCurve => _enterPos;
    public override Vector3Curve EnterRotationCurve => _enterRot;

    public override Vector3Curve ExitPositionCurve => _exitPos;
    public override Vector3Curve ExitRotationCurve => _exitRot;

    public override bool RememberStance => true;

    public override float StaminaRate => PluginConfig.ActiveAimStaminaRate.Value;
    public override float WalkSpeedBonus => PluginConfig.ActiveAimWalkSpeedBonus.Value;
    public override float SprintAccelBonus => PluginConfig.ActiveAimSprintAccelBonus.Value;
    public override float HipfireBonus => PluginConfig.ActiveAimHipfireBonus.Value;
    public override float MagazineReloadSpeedModifier => PluginConfig.ActiveAimMagazineReloadSpeedModifier.Value;
    public override float PumpBoltSpeedModifier => PluginConfig.ActiveAimPumpBoltSpeedModifier.Value;
    public override float WeaponManipSpeedModifier => PluginConfig.ActiveAimWeaponManipSpeedModifier.Value;

    public override bool BlocksFiring => false;

    public override AnimationCurve ExitAimSpeedCurve => CurveDrawer.GetCurve("active_exit_aim_speed", CurveAxis.X)!;

    public override AnimationCurve EnterAimSpeedCurve => CurveDrawer.GetCurve("active_enter_aim_speed", CurveAxis.X)!;

    public override EStanceReloadType[] ReloadTypesThatPauseStance => new EStanceReloadType[]
    {
        EStanceReloadType.Top,
        EStanceReloadType.Revolver,
    };

    public override float BlendIntoThreshold(EStanceType nextStance)
    {
        switch (nextStance)
        {
            case EStanceType.LowReady: return PluginConfig.ActiveAimBlendThresholdLowReady.Value;
            case EStanceType.HighReady: return PluginConfig.ActiveAimBlendThresholdHighReady.Value;
            case EStanceType.LeftShoulder: return PluginConfig.ActiveAimBlendThresholdLeftShoulder.Value;
            case EStanceType.PatrolStance: return PluginConfig.ActiveAimBlendThresholdPatrol.Value;
            case EStanceType.ShortStock: return PluginConfig.ActiveAimBlendThresholdShortStock.Value;
            default: return 0.2f;
        }
    }

    public override float TransitionFromModifier(EStanceType? previousStance)
    {
        float speed = PluginConfig.ActiveAimSpeedModifier.Value;
        switch (previousStance)
        {
            case EStanceType.LowReady: speed *= PluginConfig.ActiveAimTransitionFromLowReady.Value; return speed;
            case EStanceType.HighReady: speed *= PluginConfig.ActiveAimTransitionFromHighReady.Value; return speed;
            case EStanceType.LeftShoulder: speed *= PluginConfig.ActiveAimTransitionFromLeftShoulder.Value; return speed;
            case EStanceType.PatrolStance: speed *= PluginConfig.ActiveAimTransitionFromPatrol.Value; return speed;
            case EStanceType.ShortStock: speed *= PluginConfig.ActiveAimTransitionFromShortStock.Value; return speed;
            case EStanceType.None: speed *= PluginConfig.ActiveAimTransitionFromIdle.Value; return speed;
            default: return speed;
        }
    }

    public override float TransitionToSpeedModifier(EStanceType? nextStance)
    {
        float speed = PluginConfig.ActiveAimSpeedModifier.Value;
        switch (nextStance)
        {
            case EStanceType.LowReady: speed *= PluginConfig.ActiveAimTransitionToLowReady.Value; return speed;
            case EStanceType.HighReady: speed *= PluginConfig.ActiveAimTransitionToHighReady.Value; return speed;
            case EStanceType.LeftShoulder: speed *= PluginConfig.ActiveAimTransitionToLeftShoulder.Value; return speed;
            case EStanceType.PatrolStance: speed *= PluginConfig.ActiveAimTransitionToPatrol.Value; return speed;
            case EStanceType.ShortStock: speed *= PluginConfig.ActiveAimTransitionToShortStock.Value; return speed;
            case EStanceType.None: speed *= PluginConfig.ActiveAimTransitionToIdle.Value; return speed;
            default: return speed;
        }
    }

    private Vector3Curve _enterPos;
    private readonly Vector3Curve _enterRot;

    private Vector3Curve _exitPos;
    private readonly Vector3Curve _exitRot;

    public ActiveAim()
    {
        _enterPos = CurveDrawer.GetCurve("active_enter_position")?.Clone()!;
        _enterRot = CurveDrawer.GetCurve("active_enter_rotation")?.Clone()!;

        _exitPos = CurveDrawer.GetCurve("active_exit_position")?.Clone()!;
        _exitRot = CurveDrawer.GetCurve("active_exit_rotation")?.Clone()!;
    }

    public override void CorrectPositionCurveX()
    {
        _enterPos = CurveDrawer.GetCurve("active_enter_position")?.Clone()!;
        _exitPos = CurveDrawer.GetCurve("active_exit_position")?.Clone()!;

        _enterPos = StanceUtils.NormalizeXPositionCurve(_enterPos);
        _exitPos = StanceUtils.NormalizeXPositionCurve(_exitPos);
    }
}
