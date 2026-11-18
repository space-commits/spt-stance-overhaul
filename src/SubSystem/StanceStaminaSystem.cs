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

    private const float AdsDrainCrouchedModifier = 0.8f;
    private const float AdsDrainProneModifier = 0.4f;

    // Base rate of the current stamina mode, before weapon weight, skills and config are applied.
    private float _stanceBaseRate = 0f;

    private EStanceStaminaType _stanceStaminaMode = EStanceStaminaType.Neutral;

    // Positive = regen pts/sec, negative = drain pts/sec, 0 = do nothing.
    // Computed when read, so a weapon swap, skill level-up or config change can't leave it stale.
    private float StanceRate
    {
        get
        {
            switch (_stanceStaminaMode)
            {
                case EStanceStaminaType.Regen: return ComputeRegenRate(_stanceBaseRate);
                case EStanceStaminaType.Drain: return -ComputeDrainRate(_stanceBaseRate, true);
                default: return 0f;
            }
        }
    }

    public float CurrentStanceStaminaDrainRate
    {
        get
        {
            return StanceRate;
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
            bool stanceCanDrain = _stanceStaminaMode == EStanceStaminaType.Drain && !IsInExternalStateThatPausesRestoration && !RestoreStamina && !ExternalStateAllowsVanillaRegen;
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
            return ExternalStateThatAllowsRegen ? BaseGameStaminaRegenRate() : StanceRate;
        }
    }

    //TODO: factor by strength and endurance skills
    public float StaminaDrainRate
    {
        get
        {
            return ExternalStateThatDrainsStam ? GetExternalStaminaDrainRate() : StanceRate;
        }
    }

    // Floor for idle and stance drain, raised by Endurance (StatsSystem.GetMinimumArmStamina). ADS, breath-hold and tac sprint drain have no floor.
    public float StanceMinArmStamina
    {
        get
        {
            bool isDoingStanceDrain = _stanceStaminaMode == EStanceStaminaType.Drain && StanceRate != 0f && !ExternalStateThatDrainsStam;
            return isDoingStanceDrain ? StanceControllerInstance.StatsHandlerInstance.GetMinimumArmStamina(BaseMinimumArmStamina) : 0f;
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

    // Crouching with no stance: idle drain stops, regen stays blocked (freeze)
    public bool ExternalStatePausesStanceDrain
    {
        get
        {
            return StanceControllerInstance.CurrentStanceType == EStanceType.None && PlayerStateInstance.Player.Pose == EPlayerPose.Duck && !PlayerStateInstance.IsMoving;
        }
    }

    // Crouching in active aim: stance drain stops and vanilla regen is alloweds
    public bool ExternalStateAllowsVanillaRegen
    {
        get
        {
            return StanceControllerInstance.CurrentStanceType == EStanceType.ActiveAiming && PlayerStateInstance.Player.Pose == EPlayerPose.Duck && !PlayerStateInstance.IsMoving;
        }
    }

    public float GetExternalStaminaDrainRate()
    {
        if (StanceControllerInstance.IsDoingTacSprint)
            return -ComputeDrainRate(PluginConfig.TacSprintDrainRate.Value, true);
        if (PlayerStateInstance.Player.Physical.HoldingBreath || PlayerStateInstance.Player.ProceduralWeaponAnimation.IsAiming)
            return -ComputeDrainRate(BaseIdleDrainRate, true) * AdsDrainPoseModifier;
        return 0f;
    }

    private float AdsDrainPoseModifier
    {
        get
        {
            Player player = PlayerStateInstance.Player;
            return player.IsInPronePose ? AdsDrainProneModifier :
                   player.Pose == EPlayerPose.Duck ? AdsDrainCrouchedModifier :
                   1f;
        }
    }

    public float GetLegStaminaDrainRate()
    {
        if (StanceControllerInstance.IsDoingTacSprint)
            return -ComputeDrainRate(PluginConfig.TacSprintDrainRate.Value * 0.85f, false);
        return 0f;
    }

    public float BaseGameStaminaRegenRate()
    {
        //patrol should be the best regen stance, use it as a baseline
        var standardRate = StanceControllerInstance.StatsHandlerInstance.GetRegenerationRate(PluginConfig.StamRegenRate.Value);
        var noWeapon = PluginConfig.StamRegenRate.Value * 1.25f;

        var weaponCounts = PlayerStateInstance.IsInInventory;
        var weaponDoesNotCount = PlayerStateInstance.IsUsingStationaryWeapon || !PlayerStateInstance.WeaponIsReady || PlayerStateInstance.IsMounting;

        if (weaponCounts)
            return standardRate;
        else if (weaponDoesNotCount)
            return noWeapon;
        else if (PlayerStateInstance.Player.IsInPronePose) // prone with the weapon in hands: same rate as the inventory, on top of the base game's regen
            return standardRate;

        return 0f;
    }


    public void RunOnAwake()
    {
        StanceEvents.OnStanceEntered += CheckState;
        StanceEvents.OnStanceExited += OnStateChanged;
        WeaponStateInstance.OnWeaponStateChanged += OnStateChanged;
        RealismCommonLib.Events.PlayerEvents.OnAnyItemEquipped += OnStateChanged;
        RealismCommonLib.Events.PlayerEvents.AimStateChanged += OnStateChanged;
    }

    public void RunOnDestroy()
    {
        StanceEvents.OnStanceEntered -= CheckState;
        StanceEvents.OnStanceExited -= OnStateChanged;
        WeaponStateInstance.OnWeaponStateChanged -= OnStateChanged;
        RealismCommonLib.Events.PlayerEvents.OnAnyItemEquipped -= OnStateChanged;
        RealismCommonLib.Events.PlayerEvents.AimStateChanged -= OnStateChanged;
    }

    public void RunOnUpdate(float deltaTime)
    {
        if (!PluginConfig.EnableStanceStamChanges.Value) return;

        var physical = PlayerStateInstance.Player?.Physical;
        if (physical == null) return;

        // Tac sprint drains leg stamina on top of vanilla sprint drain, at the same rate as arm stamina
        if (StanceControllerInstance.IsDoingTacSprint && PlayerStateInstance.IsSprinting)
            physical.Stamina.Current = Mathf.Max(physical.Stamina.Current + GetLegStaminaDrainRate() * deltaTime, 0f);

        if (_stanceStaminaMode == EStanceStaminaType.Neutral) return;

        if (RestoreStamina)
        {
            //ModLogger.LogWarning($"RestoreStamina: stance={StanceControllerInstance?.CurrentStance?.StanceType}, staminaMode={_stanceStaminaMode}, staminaRate={StanceRate}, regenRate={StaminaRegenRate}");
            physical.HandsStamina.Current = Mathf.Min(physical.HandsStamina.Current + StaminaRegenRate * deltaTime, physical.HandsStamina.TotalCapacity);
        }
        else if (DrainStamina)
        {
            if (physical.HandsStamina.Current > StanceMinArmStamina)
                physical.HandsStamina.Current = physical.HandsStamina.Current + StaminaDrainRate * deltaTime;
        }

        if (DisableStaminaRestoration)
        {
            //ModLogger.LogWarning($"DisableStaminaRestoration: stance={StanceControllerInstance?.CurrentStance?.StanceType}, staminaMode={_stanceStaminaMode}, staminaRate={StanceRate}");
            physical.HandsStamina.DisableRestoration = Time.time + 1f;
        }
    }

    private void OnStateChanged()
    {
        CheckState(StanceControllerInstance?.CurrentStance);
    }

    private void CheckState(IStance? stance)
    {
        stance = StanceUtils.GetBonusStance(stance);

        switch (stance?.StaminaMode)
        {
            case EStanceStaminaType.Regen:
                _stanceBaseRate = stance.StaminaRate;
                _stanceStaminaMode = EStanceStaminaType.Regen;
                break;

            case EStanceStaminaType.Drain:
                _stanceBaseRate = stance.StaminaRate;
                _stanceStaminaMode = EStanceStaminaType.Drain;
                break;

            case EStanceStaminaType.Freeze:
                _stanceBaseRate = 0f;
                _stanceStaminaMode = EStanceStaminaType.Freeze;
                break;
            default: // Neutral: idle drain, no stance factor
                _stanceBaseRate = BaseIdleDrainRate;
                _stanceStaminaMode = PluginConfig.EnableIdleStamDrain.Value ? EStanceStaminaType.Drain : EStanceStaminaType.Neutral;
                break;
        }
    }

    private float ComputeRegenRate(float baseRate)
    {
        return StanceControllerInstance.StatsHandlerInstance.GetRegenerationRate(baseRate);
    }

    private float ComputeDrainRate(float baseRate, bool addIdleModi)
    {
        float drainRate = StanceControllerInstance.StatsHandlerInstance.GetDrainRate(baseRate);
        return addIdleModi ? drainRate * PluginConfig.StamDrainModi.Value : drainRate;
    }
}