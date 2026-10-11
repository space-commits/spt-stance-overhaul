using System;
using System.Collections.Generic;
using System.Text;
using RealismCommonLib.Events;

namespace StanceOverhaul.SubSystem.StanceInput
{
    public static class StanceInputEvents
    {
        public static event Action? ToggleHighReady;
        public static event Action? ToggleLowReady;
        public static event Action? ToggleShortStock;
        public static event Action? ToggleActiveAim;
        public static event Action? TogglePatrolStance;
        public static event Action? ToggleMelee;
        public static event Action? OnActiveAimKeyDown;
        public static event Action? OnActiveAimKeyUp;
        public static event Action? ToggleOffAllStances;
        public static event Action? OnAttemptedToFireFromStance;

        internal static void RaiseOnAttemptedToFireFromStance()
        {
            BaseEventHandler.RaiseEvent(OnAttemptedToFireFromStance);
        }

        internal static void RaiseToggleOffAllStances()
        {
            BaseEventHandler.RaiseEvent(ToggleOffAllStances);
        }

        internal static void RaiseTogglePatrolStance()
        {
            BaseEventHandler.RaiseEvent(TogglePatrolStance);
        }

        internal static void RaiseToggleHighReady()
        {
            BaseEventHandler.RaiseEvent(ToggleHighReady);
        }

        internal static void RaiseToggleLowReady()
        {
            BaseEventHandler.RaiseEvent(ToggleLowReady);
        }

        internal static void RaiseToggleShortStock()
        {
            BaseEventHandler.RaiseEvent(ToggleShortStock);
        }

        internal static void RaiseToggleActiveAim()
        {
            BaseEventHandler.RaiseEvent(ToggleActiveAim);
        }

        internal static void RaiseToggleMelee()
        {
            BaseEventHandler.RaiseEvent(ToggleMelee);
        }

        internal static void RaiseHoldActiveAimKeyDown()
        {
            BaseEventHandler.RaiseEvent(OnActiveAimKeyDown);
        }

        internal static void RaiseHoldActiveAimKeyUp()
        {
            BaseEventHandler.RaiseEvent(OnActiveAimKeyUp);
        }
    }
}
