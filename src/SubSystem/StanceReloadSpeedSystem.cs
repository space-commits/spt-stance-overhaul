using StanceOverhaul.Enums;
using StanceOverhaul.Events;
using EFT.InventoryLogic;
using RealismCommonLib.Events;
using RealismCommonLib.ModifierHandlers;
using static RealismCommonLib.Plugin;
using static StanceOverhaul.Plugin;

namespace StanceOverhaul.SubSystem;

public class StanceReloadSpeedSystem : ISubSystem
{
    private FloatMultiplierHandle _magReload;
    private FloatMultiplierHandle _checkAmmo;
    private FloatMultiplierHandle _checkChamber;
    private FloatMultiplierHandle _pumpBolt;
    private FloatMultiplierHandle _malfFix;
    private FloatMultiplierHandle _rechamber;
    private FloatMultiplierHandle _internalMagReload;
    private FloatMultiplierHandle _revolverReload;
    
    public EStanceReloadType CurrentReloadType { get; private set; }

    public bool IsInReloadOperation
    {
        get
        {
            return CurrentReloadType != EStanceReloadType.None;
        }
    }

    public void RunOnAwake()
    {
        ReloadEvents.WeaponStateReset += OnWeaponStateReset;
        ReloadEvents.CheckAmmo += OnCheckAmmo;
        ReloadEvents.ChamberCheck += OnChamberCheck;
        ReloadEvents.MalfFix += OnMalfFix;
        ReloadEvents.Rechamber += OnRechamber;
        ReloadEvents.MagReload += OnMagReload;
        ReloadEvents.QuickMagReload += OnQuickMagReload;
        ReloadEvents.InternalMagReload += OnInternalMagReload;
        ReloadEvents.BoltActionPump += PumpBolt;

        AssignReloadHandlers();
    }

    public void RunOnDestroy()
    {
        ReloadEvents.WeaponStateReset -= OnWeaponStateReset;
        ReloadEvents.CheckAmmo -= OnCheckAmmo;
        ReloadEvents.ChamberCheck -= OnChamberCheck;
        ReloadEvents.MalfFix -= OnMalfFix;
        ReloadEvents.Rechamber -= OnRechamber;
        ReloadEvents.MagReload -= OnMagReload;
        ReloadEvents.QuickMagReload -= OnQuickMagReload;
        ReloadEvents.InternalMagReload -= OnInternalMagReload;
        ReloadEvents.BoltActionPump -= PumpBolt;

        UnassignReloadHandlers();
    }

    private void AssignReloadHandlers()
    {
        _magReload = StatModifiers.MagReloadSpeed.Add(1f);
        _checkAmmo = StatModifiers.CheckAmmoSpeed.Add(1f);
        _checkChamber = StatModifiers.CheckChamberSpeed.Add(1f);
        _pumpBolt = StatModifiers.PumpBoltSpeed.Add(1f);
        _malfFix = StatModifiers.MalfFixSpeed.Add(1f);
        _rechamber = StatModifiers.RechamberSpeed.Add(1f);
        _revolverReload = StatModifiers.RevolverReloadModifier.Add(1f);
        _internalMagReload = StatModifiers.InternalReloadModifier.Add(1f);
    }

    private void UnassignReloadHandlers()
    {
        StatModifiers.MagReloadSpeed.Remove(_magReload);
        StatModifiers.CheckAmmoSpeed.Remove(_checkAmmo);
        StatModifiers.CheckChamberSpeed.Remove(_checkChamber);
        StatModifiers.PumpBoltSpeed.Remove(_pumpBolt);
        StatModifiers.MalfFixSpeed.Remove(_malfFix);
        StatModifiers.RechamberSpeed.Remove(_rechamber);
        StatModifiers.InternalReloadModifier.Remove(_internalMagReload);
        StatModifiers.RevolverReloadModifier.Remove(_revolverReload);
    }

    private void OnWeaponStateReset()
    {
        CurrentReloadType = EStanceReloadType.None;
        _checkAmmo.Multiplier = 1f;
        _checkChamber.Multiplier = 1f;
        _rechamber.Multiplier = 1f;
        _malfFix.Multiplier = 1f;
        _magReload.Multiplier = 1f;
        _internalMagReload.Multiplier = 1f;
        _pumpBolt.Multiplier = 1f;
        _revolverReload.Multiplier = 1f;

        StanceEvents.RaiseStanceReloadReset();
    }

    public void RunOnUpdate(float deltaTime)
    {
    }

    private void OnInternalMagReload()
    {
        ApplyInternalReloadSpeedBonus();
        StanceEvents.RaiseStanceReload();
    }

    private void OnMagReload()
    {
        CurrentReloadType = EStanceReloadType.Magazine;

        if (PluginConfig.EnableReloadSpeedChanges.Value)
            ApplyMagReloadSpeedBonuses();

        StanceEvents.RaiseStanceReload();
        StanceEvents.RaiseStanceMagReload();
    }

    private void OnQuickMagReload()
    {
        OnMagReload();
    }

    private void ApplyMagReloadSpeedBonuses()
    {
        _magReload.Multiplier = StanceControllerInstance.CurrentStance?.MagazineReloadSpeedModifier ?? 1f;
    }

    private void OnCheckAmmo()
    {
        CurrentReloadType = EStanceReloadType.CheckAmmo;
        OnWeaponManip();
        StanceEvents.RaiseStanceCheckAmmo();
    }

    private void OnChamberCheck()
    {
        CurrentReloadType = EStanceReloadType.CheckChamber;
        OnWeaponManip();
        StanceEvents.RaiseStanceChamberCheck();
    }

    private void OnRechamber()
    {
        CurrentReloadType = EStanceReloadType.Rechamber;
        OnWeaponManip();
        StanceEvents.RaiseStanceChamber();
    }

    private void OnMalfFix()
    {
        CurrentReloadType = EStanceReloadType.ClearMalfunction;
        OnWeaponManip();
        StanceEvents.RaiseStanceChamber();
    }

    private void OnWeaponManip()
    {
        if (PluginConfig.EnableReloadSpeedChanges.Value)
        {
            var bonus = StanceControllerInstance.CurrentStance?.WeaponManipSpeedModifier ?? 1f;
            _checkAmmo.Multiplier = bonus;
            _checkChamber.Multiplier = bonus;
            _rechamber.Multiplier = bonus;
            _malfFix.Multiplier = bonus;
        }

        StanceEvents.RaiseStanceReload();
    }


    private void ApplyInternalReloadSpeedBonus()
    {
        float bonus = 1f;

        if (ReloadStateInstance.IsAttemptingRevolverReload)
        {
            CurrentReloadType = EStanceReloadType.Revolver;
            _revolverReload.Multiplier = StanceControllerInstance.CurrentStance?.RevolverReloadSpeedModifier ?? 1f;
            return;
        }
        else if (WeaponStateInstance.IsShotgun && WeaponStateInstance.WeaponReloadType == Weapon.EReloadMode.InternalMagazine)
        {
            CurrentReloadType = EStanceReloadType.Tube;
            bonus = StanceControllerInstance.CurrentStance?.TubeReloadSpeedModifier ?? 1f;
            StanceEvents.RaiseStanceTubeReload();
        }
        else
        {
            CurrentReloadType = EStanceReloadType.Top;
            bonus = StanceControllerInstance.CurrentStance?.TopReloadSpeedModifier ?? 1f;
            StanceEvents.RaiseStanceTopReload();
        }

        if (PluginConfig.EnableReloadSpeedChanges.Value)
            _internalMagReload.Multiplier = bonus;
    }

    private void PumpBolt()
    {
        CurrentReloadType = EStanceReloadType.PumpBolt;

        if (PluginConfig.EnableReloadSpeedChanges.Value)
            _pumpBolt.Multiplier = StanceControllerInstance.CurrentStance?.PumpBoltSpeedModifier ?? 1f;
    }
}