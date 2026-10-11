using StanceOverhaul.Enums;
using StanceOverhaul.Events;
using StanceOverhaul.Stances;
using UnityEngine;
using EFT;
using System;
using static RealismCommonLib.Plugin;
using static StanceOverhaul.Plugin;

namespace StanceOverhaul.SubSystem;


public class StanceStaminaSystem : ISubSystem
{
    private const float BaseMinimumArmStamina = 10f;
    private const float BaseIdleDrainRate = 0.11f;

    // Positive = regen pts/sec, negative = drain pts/sec, 0 = do nothing.
    private float _stanceRate = 0f;

    private EStanceStaminaType _stanceStaminaMode = EStanceStaminaType.Neutral;

    public float CurrentStanceStaminaDrainRate
    {
        get
        {
            return _stanceRate;
        }
    }

    public bool RestoreStamina
    {
        get
        {
            return (_stanceStaminaMode == EStanceStaminaType.Regen || ExternalStateThatAllowsRegen) && !IsInExternalStateThatPausesRestoration && !ExternalStateThatDrainsStam;
        }
    }

    public bool DrainStamina
    {
        get
        {
            bool stanceCanDrain = _stanceStaminaMode == EStanceStaminaType.Drain && !IsInExternalStateThatPausesRestoration && !RestoreStamina;
            return stanceCanDrain || ExternalStateThatDrainsStam;
        }
    }

    public bool DisableStaminaRestoration
    {
        get
        {
            return (_stanceStaminaMode == EStanceStaminaType.Freeze || DrainStamina || IsInExternalStateThatPausesRestoration) && !ExternalStateThatAllowsRegen;
        }
    }


    //TODO: factor by strength and endurance skills
    public float StaminaRegenRate
    {
        get
        {
            return ExternalStateThatAllowsRegen ? BaseGameStaminaRegenRate() : _stanceRate;
        }
    }

    //TODO: factor by strength and endurance skills
    public float StaminaDrainRate
    {
        get
        {
            return ExternalStateThatDrainsStam ? GetExternalStaminaDrainRate() : _stanceRate;
        }
    }

    //TODO: factor by strength and endurance skills
    public float StanceMinArmStamina
    {
        get
        {
            bool isDoingStanceDrain = _stanceStaminaMode == EStanceStaminaType.Drain && _stanceRate != 0f && !ExternalStateThatDrainsStam;
            return isDoingStanceDrain ? BaseMinimumArmStamina : 0f;
        }
    }

    public bool ExternalStateThatAllowsRegen
    {
        get
        {
            return !PlayerStateInstance.WeaponIsReady || PlayerStateInstance.IsInInventory || PlayerStateInstance.IsUsingStationaryWeapon || PlayerStateInstance.IsMounting || PlayerStateInstance.Player.IsInPronePose;
        }
    }

    public bool ExternalStateThatDrainsStam
    {
        get
        {
            return StanceControllerInstance.IsDoingTacSprint || PlayerStateInstance.Player.Physical.HoldingBreath || PlayerStateInstance.Player.ProceduralWeaponAnimation.IsAiming;
        }
    }

    public bool IsInExternalStateThatPausesRestoration
    {
        get
        {
            var doingRegularSprint = PlayerStateInstance.IsSprinting && !StanceControllerInstance.IsDoingTacSprint;
            return doingRegularSprint || FiringStateInstance.IsFiring || ExternalStatePausesStanceDrain;
        }
    }

    public bool ExternalStatePausesStanceDrain
    {
        get
        {
            return StanceControllerInstance.CurrentStanceType == EStanceType.ActiveAiming && PlayerStateInstance.Player.Pose == EPlayerPose.Duck;
        }
    }

    public float GetExternalStaminaDrainRate()
    {
        if (StanceControllerInstance.IsDoingTacSprint)
            return -ComputeDrainRate(PluginConfig.TacSprintDrainRate.Value * PluginConfig.IdleStamDrainModi.Value);
        if (PlayerStateInstance.Player.Physical.HoldingBreath || PlayerStateInstance.Player.ProceduralWeaponAnimation.IsAiming)
            return -ComputeDrainRate(BaseIdleDrainRate * PluginConfig.IdleStamDrainModi.Value);
        return 0f;
    }

    public float BaseGameStaminaRegenRate()
    {
        //patrol should be the best regen stance, use it as a baseline
        var standardRate = StanceControllerInstance.StatsHandlerInstance.GetRegenerationRate(PluginConfig.PatrolStaminaRate.Value * 0.85f);
        var noWeapon = PluginConfig.PatrolStaminaRate.Value * 1.15f;

        var weaponCounts = PlayerStateInstance.IsInInventory;
        var weaponDoesNotCount = PlayerStateInstance.IsUsingStationaryWeapon || !PlayerStateInstance.WeaponIsReady || PlayerStateInstance.IsMounting;

        if (weaponCounts)
            return standardRate;
        else if (weaponDoesNotCount)
            return noWeapon;

        return 0f;
    }


    public void RunOnAwake()
    {
        StanceEvents.OnStanceEntered += CheckState;
        StanceEvents.OnStanceExited += OnStateChanged;
        WeaponStateInstance.OnWeaponStateChanged += OnStateChanged;
        RealismCommonLib.Events.PlayerEvents.OnAnyItemEquipped += OnStateChanged;
    }

    public void RunOnDestroy()
    {
        StanceEvents.OnStanceEntered -= CheckState;
        StanceEvents.OnStanceExited -= OnStateChanged;
        WeaponStateInstance.OnWeaponStateChanged -= OnStateChanged;
        RealismCommonLib.Events.PlayerEvents.OnAnyItemEquipped -= OnStateChanged;
    }

    public void RunOnUpdate(float deltaTime)
    {
        if (!PluginConfig.EnableStanceStamChanges.Value) return;

        var physical = PlayerStateInstance.Player?.Physical;
        if (physical == null || _stanceStaminaMode == EStanceStaminaType.Neutral) return;

        if (RestoreStamina)
        {
            //ModLogger.LogWarning($"RestoreStamina: stance={StanceControllerInstance?.CurrentStance?.StanceType}, staminaMode={_stanceStaminaMode}, staminaRate={_stanceRate}, regenRate={StaminaRegenRate}");
            physical.HandsStamina.Current = Mathf.Min(physical.HandsStamina.Current + StaminaRegenRate * deltaTime, physical.HandsStamina.TotalCapacity);
        }
        else if (DrainStamina)
        {
            //ModLogger.LogWarning($"DrainStamina: stance={StanceControllerInstance?.CurrentStance?.StanceType}, staminaMode={_stanceStaminaMode}, staminaRate={_stanceRate}");
            //ModLogger.LogWarning($"DrainStamina: current={physical.HandsStamina.Current}, min={StanceMinArmStamina}, drainRate={StaminaDrainRate}");

            if (physical.HandsStamina.Current > StanceMinArmStamina)
                physical.HandsStamina.Current = physical.HandsStamina.Current + StaminaDrainRate * deltaTime;
        }

        if (DisableStaminaRestoration)
        {
            //ModLogger.LogWarning($"DisableStaminaRestoration: stance={StanceControllerInstance?.CurrentStance?.StanceType}, staminaMode={_stanceStaminaMode}, staminaRate={_stanceRate}");
            physical.HandsStamina.DisableRestoration = Time.time + 1f;
        }
    }

    private void OnStateChanged()
    {
        CheckState(StanceControllerInstance?.CurrentStance);
    }

    private void CheckState(IStance? stance)
    {
        switch (stance?.StaminaMode)
        {
            case EStanceStaminaType.Regen:
                _stanceRate = ComputeRegenRate(stance.StaminaRate);
                _stanceStaminaMode = EStanceStaminaType.Regen;
                break;

            case EStanceStaminaType.Drain:
                _stanceRate = -ComputeDrainRate(stance.StaminaRate);
                _stanceStaminaMode = EStanceStaminaType.Drain;
                break;

            case EStanceStaminaType.Freeze:
                _stanceRate = 0f;
                _stanceStaminaMode = EStanceStaminaType.Freeze;
                break;
            default: // Neutral
                _stanceRate = PluginConfig.EnableIdleStamDrain.Value ? -ComputeDrainRateIdle() : 0f;
                _stanceStaminaMode = PluginConfig.EnableIdleStamDrain.Value ? EStanceStaminaType.Drain : EStanceStaminaType.Neutral;
                break;
        }
    }

    private float ComputeRegenRate(float baseRate)
    {
        return StanceControllerInstance.StatsHandlerInstance.GetRegenerationRate(baseRate);
    }

    private float ComputeDrainRate(float baseRate)
    {
        return StanceControllerInstance.StatsHandlerInstance.GetDrainRate(baseRate) * PluginConfig.IdleStamDrainModi.Value;
    }

    /// <summary>
    /// For idle stam drain, no stance factor
    /// </summary>
    private float ComputeDrainRateIdle()
    {
        return StanceControllerInstance.StatsHandlerInstance.GetDrainRate(BaseIdleDrainRate) * PluginConfig.IdleStamDrainModi.Value;
    }
}