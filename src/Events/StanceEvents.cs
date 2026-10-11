using RealismCommonLib.Events;
using StanceOverhaul.Stances;
using EFT;
using System;

namespace StanceOverhaul.Events;

public static class StanceEvents
{
    /// <summary>Raised when a stance's OnEnter() fires. Carries the entering stance.</summary>
    public static event Action<IStance>? OnStanceEntered;

    /// <summary>Raised when a stance's OnExit() fires. Handler should revert to idle behaviour.</summary>
    public static event Action<IStance>? OnStanceExitedRef;
    public static event Action? OnStanceExited;

    public static event Action? OnTacSprintStarted;
    public static event Action? OnTacSprintEnded;

    public static event Action? OnStanceHitShoulder;

    public static event Action? OnStanceReloadReset;
    public static event Action? OnStanceCheckAmmo;
    public static event Action? OnStanceChamberCheck;
    public static event Action? OnStanceChamber;
    public static event Action? OnStanceMagReload;
    public static event Action? OnStanceTubeReload;
    public static event Action? OnStanceTopReload;
    public static event Action? OnStanceReload;
    public static event Action? OnTransformsInit;
    public static event Action<Player>? OnPlayerLoadRef;
    public static event Action<Player.FirearmController>? OnTransformsInitFC;
    public static event Action? OnPrimaryStanceChanged;
    public static event Action? OnStationaryWeaponOperated;

    internal static void RaiseOnStationaryWeaponOperated()
    {
        BaseEventHandler.RaiseEvent(OnStationaryWeaponOperated);
    }

    internal static void RaiseOnPrimaryStanceChanged()
    {
        BaseEventHandler.RaiseEvent(OnPrimaryStanceChanged);
    }

    internal static void RaiseOnPlayerLoad(Player player)
    {
        BaseEventHandler.RaiseEvent(OnPlayerLoadRef, player);
    }

    internal static void RaiseOnTransformsInit(Player.FirearmController fc)
    {
        BaseEventHandler.RaiseEvent(OnTransformsInit);
        BaseEventHandler.RaiseEvent(OnTransformsInitFC, fc);
    }

    internal static void RaiseOnStanceEntered(IStance stance)
    {
        BaseEventHandler.RaiseEvent(OnStanceEntered, stance);
    }

    internal static void RaiseOnStanceExited(IStance stance)
    {
        BaseEventHandler.RaiseEvent(OnStanceExitedRef, stance);
        BaseEventHandler.RaiseEvent(OnStanceExited);
    }

    internal static void RaiseOnTacSprintStarted()
    {
        BaseEventHandler.RaiseEvent(OnTacSprintStarted);
    }

    internal static void RaiseOnTacSprintEnded()
    {
        BaseEventHandler.RaiseEvent(OnTacSprintEnded);
    }

    internal static void RaiseOnStanceHitShoulder()
    {
        BaseEventHandler.RaiseEvent(OnStanceHitShoulder);
    }

    /// <summary>
    /// Chains from common lib reload events but allows StanceReloadSpeedSystem to handle timing
    /// </summary>
    internal static void RaiseStanceReloadReset()
    {
        BaseEventHandler.RaiseEvent(OnStanceReloadReset);
    }
    internal static void RaiseStanceChamberCheck()
    {
        BaseEventHandler.RaiseEvent(OnStanceChamberCheck);
    }
    internal static void RaiseStanceCheckAmmo()
    {
        BaseEventHandler.RaiseEvent(OnStanceCheckAmmo);
    }
    internal static void RaiseStanceChamber()
    {
        BaseEventHandler.RaiseEvent(OnStanceChamber);
    }
    internal static void RaiseStanceMagReload()
    {
        BaseEventHandler.RaiseEvent(OnStanceMagReload);
    }
    internal static void RaiseStanceTubeReload()
    {
        BaseEventHandler.RaiseEvent(OnStanceTubeReload);
    }
    internal static void RaiseStanceTopReload()
    {
        BaseEventHandler.RaiseEvent(OnStanceTopReload);
    }

    /// <summary>
    /// Chains from common lib reload events but allows StanceReloadSpeedSystem to handle timing
    /// </summary>
    internal static void RaiseStanceReload()
    {
        BaseEventHandler.RaiseEvent(OnStanceReload);
    }
}
