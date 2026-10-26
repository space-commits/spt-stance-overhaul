using UnityEngine;
using EFT;
using static RealismCommonLib.Plugin;

namespace StanceOverhaul.SubSystem;

public struct SpringHandlingProfile
{
    public float BaseDamping;
    /// <summary>
    ///  Max amount damping is reduced by at the lightest / best-ergo end.
    /// </summary>
    public float DampingReductionRange;

    public float BaseReturnSpeed;
    /// <summary>
    /// Max amount return speed is increased by at the lightest / best-ergo end.
    /// </summary>
    public float ReturnSpeedIncreaseRange;
}


//TODO: factor in balance
public class StatsSystem : ISubSystem
{
    private const float MinimumWeaponWeghtPerc = 0.35f;

    // Maximum ergo redfuction
    private const float MaximumErgoReduction = 0.50f;

    // Controls diminishing returns 1 = linear, > 1 = stronger diminishing returns
    private const float ErgoExponent = 2.15f;
    private const float StaminaWeightExponentRifle = 1.35f;
    private const float StaminaWeightExponentPistol = 2f;
    private const float SpeedWeightExponentRifle = 1.25f;
    private const float SpeedWeightExponentPistol = 3f;

    private const float OffsetWeightExponentRifle = 1.25f;
    private const float OffsetWeightExponentPistol = 3f;

    private const float DampingWeightExponentRifle = 6f;
    private const float DampingWeightExponentPistol = 6f;
    private const float ReturnSpeedWeightExponentRifle = 20f;
    private const float ReturnSpeedWeightExponentPistol = 20f;

    private const float RifleEffWeightMin = 1f;
    private const float RifleEffWeightMax = 24f;
    private const float PistolEffWeightMin = 0.2f;
    private const float PistolEffWeightMax = 3f;

    private const float AbsoluteMinDamping = 0.72f;
    private const float AbsoluteMaxDamping = 0.84f;

    private const float AbsoluteMinReturnSpeed = 0.03f;
    private const float AbsoluteMaxReturnSpeed = 0.1f;

    private static readonly SpringHandlingProfile _defaultRifleSpringProfile = new SpringHandlingProfile
    {
        BaseDamping = 0.85f,
        DampingReductionRange = 0.09f,
        BaseReturnSpeed = 0.06f,
        ReturnSpeedIncreaseRange = 0.02f
    };

    private static readonly SpringHandlingProfile _defaultPistolSpringProfile = new SpringHandlingProfile
    {
        BaseDamping = 0.85f,
        DampingReductionRange = 0.09f,
        BaseReturnSpeed = 0.06f,
        ReturnSpeedIncreaseRange = 0.02f
    };

    public SpringHandlingProfile CurrentRifleSpringProfile => _defaultRifleSpringProfile;
    public SpringHandlingProfile CurrentPistolSpringProfile => _defaultPistolSpringProfile;

    /// <summary>
    /// Weight of the weapon after ergonomics is applied as a reducing factor.
    /// </summary>
    public float EffectiveWeaponWeight { get; private set; }

    public float StrengthWeightModifier
    {
        get
        {
            return SkillStateInstance.StrengthLevel * 0.0075f;
        }
    }

    public void RunOnAwake()
    {
        WeaponStateInstance.OnWeaponStateChangedFc += UpdateStats;
    }

    public void RunOnDestroy()
    {
        WeaponStateInstance.OnWeaponStateChangedFc -= UpdateStats;
    }

    public void RunOnUpdate(float deltaTime)
    {
    }

    private void UpdateStats(Player.FirearmController fc)
    {
        CalculateEffectiveWeight();
    }

    public void CalculateEffectiveWeight()
    {
        var ergonomics = WeaponStateInstance.TotalErgo;
        var weight = WeaponStateInstance.TotalWeaponWeight;
        var minWeight = weight * MinimumWeaponWeghtPerc;

        float ergoNormalized = Mathf.Clamp(ergonomics / 100f, 0f, 1f);

        //east-out power curve, tweaks the diminishing returns of ergonomics on weight reduction. 
        //change exponent to tweak the curve, 1 = linear, higher the value the stronger early ergo is and less strong later ergo is
        float ergoReduction = MaximumErgoReduction * (1f - Mathf.Pow(1f - ergoNormalized, ErgoExponent));

        //reduce weight above a floor
        float excessWeight = weight - minWeight;
        float effectiveWeight = minWeight + excessWeight * (1f - ergoReduction);

        EffectiveWeaponWeight = effectiveWeight;
    }

    private float GetLightnessFactor(float exponent)
    {
        bool isPistol = WeaponStateInstance.TreatAsPistol;
        float min = isPistol ? PistolEffWeightMin : RifleEffWeightMin;
        float max = isPistol ? PistolEffWeightMax : RifleEffWeightMax;

        float normalizedWeight = Mathf.Clamp01((EffectiveWeaponWeight - min) / (max - min)); // 0=lightest,1=heaviest
        return Mathf.Pow(1f - normalizedWeight, exponent); // 0=heaviest, 1=lightest
    }

    public float GetSpringDamping(float stanceDampingModifier = 1f)
    {
        var springProfile = WeaponStateInstance.TreatAsPistol ? CurrentPistolSpringProfile : CurrentRifleSpringProfile;
        var exponent = WeaponStateInstance.TreatAsPistol ? DampingWeightExponentPistol : DampingWeightExponentRifle;
        var lightnessFactor = GetLightnessFactor(exponent);

        var configModifier = WeaponStateInstance.TreatAsPistol ? PluginConfig.StancePistolDampingModifier.Value : PluginConfig.StanceRifleDampingModifier.Value;
        var damping = (springProfile.BaseDamping - springProfile.DampingReductionRange * lightnessFactor * stanceDampingModifier) * configModifier;

        return Mathf.Clamp(damping, AbsoluteMinDamping, AbsoluteMaxDamping);
    }

    public float GetSpringReturnSpeed(float stanceReturnSpeedModifier = 1f)
    {
        var springProfile = WeaponStateInstance.TreatAsPistol ? CurrentPistolSpringProfile : CurrentRifleSpringProfile;
        var exponent = WeaponStateInstance.TreatAsPistol ? ReturnSpeedWeightExponentPistol : ReturnSpeedWeightExponentRifle;
        var lightnessFactor = GetLightnessFactor(exponent);

        var configModifier = WeaponStateInstance.TreatAsPistol ? PluginConfig.StancePistolReturnSpeedModifier.Value : PluginConfig.StanceRifleReturnSpeedModifier.Value;
        var returnSpeed = (springProfile.BaseReturnSpeed + springProfile.ReturnSpeedIncreaseRange * lightnessFactor * stanceReturnSpeedModifier) * configModifier;

        return Mathf.Clamp(returnSpeed, AbsoluteMinReturnSpeed, AbsoluteMaxReturnSpeed);
    }

    public float GetDrainRate(float baseDrainRate)
    {
        var weightExp = WeaponStateInstance.TreatAsPistol ? StaminaWeightExponentPistol : StaminaWeightExponentRifle;
        var weightFactor = Mathf.Pow(EffectiveWeaponWeight, weightExp);
        var modifier = WeaponStateInstance.TreatAsPistol ? 0.3f : 0.15f;
        return baseDrainRate + weightFactor * modifier;
    }

    public float GetRegenerationRate(float baseRegenRate)
    {
        var weightExp = WeaponStateInstance.TreatAsPistol ? StaminaWeightExponentPistol : StaminaWeightExponentRifle;
        var weightFactor = Mathf.Pow(EffectiveWeaponWeight, weightExp);
        var modifier = WeaponStateInstance.TreatAsPistol ? 0.3f : 0.15f;
        return baseRegenRate / (1 + weightFactor * modifier);
    }

    public float GetStanceSpeedModifier(float baseStanceSpeed)
    {
        var weightExp = WeaponStateInstance.TreatAsPistol ? SpeedWeightExponentPistol : SpeedWeightExponentRifle;
        var weightFactor = Mathf.Pow(EffectiveWeaponWeight, weightExp);
        var modifier = WeaponStateInstance.TreatAsPistol ? 0.28f : 0.15f;
        return baseStanceSpeed / (1 + weightFactor * modifier);
    }

    public float GetWeaponOffset()
    {
        var weightExp = WeaponStateInstance.TreatAsPistol ? 5f : 1.5f;
        var weightFactor = Mathf.Pow(EffectiveWeaponWeight, weightExp);
        var modifier = WeaponStateInstance.TreatAsPistol ? -0.4f : -0.45f;
        return Mathf.Clamp(weightFactor * modifier, -5f, -0.5f);
    }

    public float GetStrengthWeightValue(float baseValue)
    {
        return Mathf.Min(baseValue * (1f + StrengthWeightModifier), 6.9f);
    }
    public float GetStrengthErgoValue(float baseValue)
    {
        return Mathf.Max(baseValue * StrengthWeightModifier, 12f);
    }
    
}