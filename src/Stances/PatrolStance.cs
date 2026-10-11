using RealismCommonLib.Utils;
using StanceOverhaul.Enums;
using UnityEngine;
using static RealismCommonLib.Plugin;
using static StanceOverhaul.Plugin;

namespace StanceOverhaul.Stances;

public class PatrolStance : StanceBase
{
    public override EStanceType StanceType => EStanceType.PatrolStance;
    public override EStanceStaminaType StaminaMode => EStanceStaminaType.Regen;

    public override Vector3Curve EnterPositionCurve => _enterPos;
    public override Vector3Curve EnterRotationCurve => _enterRot;
    public override Vector3Curve ExitPositionCurve => _exitPos;
    public override Vector3Curve ExitRotationCurve => _exitRot;

    public override Vector3Curve PistolEnterPositionCurve => _enterPistolPos;
    public override Vector3Curve PistolEnterRotationCurve => _enterPistolRot;

    public override Vector3Curve PistolExitPositionCurve => _exitPistolPos;
    public override Vector3Curve PistolExitRotationCurve => _exitPistolRot;

    public override float StaminaRate => PluginConfig.PatrolStaminaRate.Value;
    public override float WalkSpeedBonus => PluginConfig.PatrolWalkSpeedBonus.Value;
    public override float SprintAccelBonus => PluginConfig.PatrolSprintAccelBonus.Value;

    public override bool BlocksFiring => true;

    public override AnimationCurve ExitAimSpeedCurve => CurveDrawer.GetCurve("patrol_exit_aim_speed", CurveAxis.X)!;

    public override AnimationCurve EnterAimSpeedCurve => CurveDrawer.GetCurve("patrol_enter_aim_speed", CurveAxis.X)!;

    public override EStanceReloadType[] ReloadTypesThatPauseStance => new EStanceReloadType[]
    {
        EStanceReloadType.Magazine,
        EStanceReloadType.QuickReload,
        EStanceReloadType.Tube,
        EStanceReloadType.Top,
        EStanceReloadType.Revolver,
        EStanceReloadType.PumpBolt,
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
            case EStanceType.ActiveAiming: return PluginConfig.PatrolBlendThresholdActiveAim.Value;
            case EStanceType.HighReady: return PluginConfig.PatrolBlendThresholdHighReady.Value;
            case EStanceType.LowReady: return PluginConfig.PatrolBlendThresholdLowReady.Value;
            case EStanceType.LeftShoulder: return PluginConfig.PatrolBlendThresholdLeftShoulder.Value;
            case EStanceType.ShortStock: return PluginConfig.PatrolBlendThresholdShortStock.Value;
            case EStanceType.PistolCompress: return PluginConfig.PatrolBlendThresholdPistol.Value;
            default: return 0.2f;
        }
    }

    public override float TransitionFromModifier(EStanceType? previousStance)
    {
        float speed = PluginConfig.PatrolSpeedModifier.Value;
        switch (previousStance)
        {
            case EStanceType.ActiveAiming: speed *= PluginConfig.PatrolTransitionFromActiveAim.Value; return speed;
            case EStanceType.HighReady: speed *= PluginConfig.PatrolTransitionFromHighReady.Value; return speed;
            case EStanceType.LowReady: speed *= PluginConfig.PatrolTransitionFromLowReady.Value; return speed;
            case EStanceType.LeftShoulder: speed *= PluginConfig.PatrolTransitionFromLeftShoulder.Value; return speed;
            case EStanceType.ShortStock: speed *= PluginConfig.PatrolTransitionFromShortStock.Value; return speed;
            case EStanceType.PistolCompress: speed *= PluginConfig.PatrolTransitionFromPistol.Value; return speed;
            case EStanceType.None: speed *= PluginConfig.PatrolTransitionFromIdle.Value; return speed;
            default: return speed;
        }
    }

    public override float TransitionToSpeedModifier(EStanceType? nextStance)
    {
        float speed = PluginConfig.PatrolSpeedModifier.Value;
        switch (nextStance)
        {
            case EStanceType.ActiveAiming: speed *= PluginConfig.PatrolTransitionToActiveAim.Value; return speed;
            case EStanceType.HighReady: speed *= PluginConfig.PatrolTransitionToHighReady.Value; return speed;
            case EStanceType.LowReady: speed *= PluginConfig.PatrolTransitionToLowReady.Value; return speed;
            case EStanceType.LeftShoulder: speed *= PluginConfig.PatrolTransitionToLeftShoulder.Value; return speed;
            case EStanceType.ShortStock: speed *= PluginConfig.PatrolTransitionToShortStock.Value; return speed;
            case EStanceType.PistolCompress: speed *= PluginConfig.PatrolTransitionToPistol.Value; return speed;
            case EStanceType.None: speed *= PluginConfig.PatrolTransitionToIdle.Value; return speed;
            default: return speed;
        }
    }

    private readonly Vector3Curve _enterPos;
    private readonly Vector3Curve _enterRot;

    private readonly Vector3Curve _exitPos;
    private readonly Vector3Curve _exitRot;

    private readonly Vector3Curve _enterPistolPos;
    private readonly Vector3Curve _enterPistolRot;

    private readonly Vector3Curve _exitPistolPos;
    private readonly Vector3Curve _exitPistolRot;


    public PatrolStance()
    {
        _enterPos = CurveDrawer.GetCurve("patrol_enter_position")!;
        _enterRot = CurveDrawer.GetCurve("patrol_enter_rotation")!;

        _exitPos = CurveDrawer.GetCurve("patrol_exit_position")!;
        _exitRot = CurveDrawer.GetCurve("patrol_exit_rotation")!;

        _enterPistolPos = CurveDrawer.GetCurve("pistol_patrol_enter_position")!;
        _enterPistolRot = CurveDrawer.GetCurve("pistol_patrol_enter_rotation")!;

        _exitPistolPos = CurveDrawer.GetCurve("pistol_patrol_exit_position")!;
        _exitPistolRot = CurveDrawer.GetCurve("pistol_patrol_exit_rotation")!;
    }


}
