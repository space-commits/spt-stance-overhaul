using RealismCommonLib.Utils;
using StanceOverhaul.Enums;
using UnityEngine;
using static RealismCommonLib.Plugin;
using static StanceOverhaul.Plugin;

namespace StanceOverhaul.Stances;

public class PistolCompress : StanceBase
{
    public override EStanceType StanceType => EStanceType.PistolCompress;
    public override EStanceStaminaType StaminaMode => EStanceStaminaType.Regen;

    public override Vector3Curve EnterPositionCurve => _enterPos;
    public override Vector3Curve EnterRotationCurve => _enterRot;

    public override Vector3Curve ExitPositionCurve => _exitPos;
    public override Vector3Curve ExitRotationCurve => _exitRot;

    public override float StaminaRate => PluginConfig.PistolStaminaRate.Value;
    public override float WalkSpeedBonus => PluginConfig.PistolWalkSpeedBonus.Value;
    public override float SprintAccelBonus => PluginConfig.PistolSprintAccelBonus.Value;
    public override float HipfireBonus => PluginConfig.PistolHipfireBonus.Value;
    public override bool RememberStance => true;
    public override float MagazineReloadSpeedModifier => PluginConfig.PistolMagazineReloadSpeedModifier.Value;
    public override float WeaponManipSpeedModifier => PluginConfig.PistolWeaponManipSpeedModifier.Value;
    public override float RevolverReloadSpeedModifier => PluginConfig.PistolRevolverReloadSpeedModifier.Value;

    public override bool BlocksFiring => false;

    public override AnimationCurve ExitAimSpeedCurve => CurveDrawer.GetCurve("pistol_compress_exit_aim_speed", CurveAxis.X)!;

    public override AnimationCurve EnterAimSpeedCurve => CurveDrawer.GetCurve("pistol_compress_enter_aim_speed", CurveAxis.X)!;

    public override float BlendIntoThreshold(EStanceType nextStance)
    {
        switch (nextStance)
        {
            case EStanceType.ActiveAiming: return 0.25f;
            case EStanceType.HighReady: return 0.25f;
            case EStanceType.LeftShoulder: return 0.25f;
            case EStanceType.PatrolStance: return PluginConfig.PistolPatrolBlendModifier.Value;
            case EStanceType.ShortStock: return 0.25f;
            case EStanceType.LowReady: return 0.25f;
            default: return 0f;
        }
    }

    public override float TransitionFromModifier(EStanceType? previousStance)
    {
        float speed = PluginConfig.PistolSpeedModifier.Value;
        switch (previousStance)
        {
            case EStanceType.None: speed *= PluginConfig.PistolTransitionFromIdle.Value; return speed;
            case EStanceType.ActiveAiming: speed *= 5f; return speed;
            case EStanceType.HighReady: speed *= 5f; return speed;
            case EStanceType.LowReady: speed *= 5f; return speed;
            case EStanceType.ShortStock: speed *= 5f; return speed;
            case EStanceType.PatrolStance: speed *= PluginConfig.PistolTransitionFromPatrol.Value; return speed;
            case EStanceType.LeftShoulder: speed *= 5f; return speed;
            default: return speed;
        }
    }

    public override float TransitionToSpeedModifier(EStanceType? nextStance)
    {
        float speed = PluginConfig.PistolSpeedModifier.Value;
        switch (nextStance)
        {
            case EStanceType.None: speed *= PluginConfig.PistolTransitionToIdle.Value; return speed;
            case EStanceType.ActiveAiming: speed *= 5f; return speed;
            case EStanceType.HighReady: speed *= 5f; return speed;
            case EStanceType.LowReady: speed *= 5f; return speed;
            case EStanceType.ShortStock: speed *= 5f; return speed;
            case EStanceType.PatrolStance: speed *= PluginConfig.PistolTransitionToPatrol.Value; return speed;
            case EStanceType.LeftShoulder: speed *= 5f; return speed;
            default: return speed;
        }
    }

    private readonly Vector3Curve _enterPos;
    private readonly Vector3Curve _enterRot;

    private readonly Vector3Curve _exitPos;
    private readonly Vector3Curve _exitRot;

    public PistolCompress()
    {
        _enterPos = CurveDrawer.GetCurve("pistol_compress_enter_position")!;
        _enterRot = CurveDrawer.GetCurve("pistol_compress_enter_rotation")!;

        _exitPos = CurveDrawer.GetCurve("pistol_compress_exit_position")!;
        _exitRot = CurveDrawer.GetCurve("pistol_compress_exit_rotation")!;
    }
}