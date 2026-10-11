using RealismCommonLib.Events;
using StanceOverhaul.Enums;
using StanceOverhaul.State;
using System.Linq;
using UnityEngine;
using static RealismCommonLib.Plugin;
using static StanceOverhaul.Plugin;

namespace StanceOverhaul.SubSystem.StanceInput
{
    internal class StanceInputListener : ISubSystem
    {
        private bool _activeAimHoldInProgress;
        private bool _meleeIsToggleable = true;
        private float _meleeTimer = 0f;

        private bool StanceInputBlocked
        {
            get
            {
                return AimStateInstance.IsAiming
                    || PlayerStateInstance.IsSprinting
                    || PlayerStateInstance.IsInventoryOpen
                    || !PlayerStateInstance.WeaponIsReady
                    || PlayerStateInstance.IsUsingStationaryWeapon;
            }
        }

        public void RunOnAwake()
        {
        }

        public void RunOnDestroy()
        {
        }

        public void RunOnUpdate(float deltaTime)
        {
            StanceInputUpdate(deltaTime);
        }

        public void StanceInputUpdate(float deltaTime)
        {
            MeleeCooldownTimer(deltaTime);

            if (!StanceInputBlocked)
            {
                CheckForActimeAimInput(); // always runs first to keep _activeAimHoldInProgress current

                if (!_activeAimHoldInProgress || PluginConfig.ToggleActiveAim.Value)
                {
                    CheckForPatrolInput();
                    CheckForMeleeInput();

                    if (!WeaponStateInstance.TreatAsPistol)
                    {
                        CheckScrollInput();
                        CheckForHighReadyInput();
                        CheckForLowReadyInput();
                        CheckForShortStockInput();
                    }
                }
            }
        }

        //TODO: Replace with time gate?
        public void MeleeCooldownTimer(float deltaTime)
        {
            if (_meleeIsToggleable) return;

            _meleeTimer += deltaTime;

            if (_meleeTimer >= 0.25f)
            {
                _meleeIsToggleable = true;
                _meleeTimer = 0f;
            }
        }

        public void CheckScrollInput()
        {
            if (!PluginConfig.UseMouseWheelStance.Value) return;

            //TODO: get actual player keybinds
            bool isHoldingMagSelect = UnityEngine.Input.GetKey(KeyCode.R);
            bool isHoldingHeightStance = Input.GetKey(KeyCode.C);
            bool isHoldingKeyModifier = Input.GetKey(KeyCode.C);

            if (UnityEngine.Input.GetKey(PluginConfig.StanceWheelComboKeyBind.Value.MainKey) &&
                PluginConfig.UseMouseWheelPlusKey.Value ||
                !PluginConfig.UseMouseWheelPlusKey.Value && !Input.GetKey(KeyCode.LeftControl) && !Input.GetKey(KeyCode.LeftAlt))
            {
                float scrollDelta = Input.mouseScrollDelta.y;

                if (Mathf.Abs(scrollDelta) < 0.1f)
                    return;

                int dir = scrollDelta > 0 ? 1 : -1;

                HandleScrollInput(dir);
            }
        }

        private void HandleScrollInput(float direction)
        {
            var current = StanceControllerInstance.CurrentStanceType;

            if (direction < 0)
            {
                if (current == EStanceType.LowReady)
                    return;

                if (current == EStanceType.HighReady)
                {
                    StanceInputEvents.RaiseToggleHighReady();
                    return;
                }

                StanceInputEvents.RaiseToggleLowReady();
                return;
            }

            if (direction > 0)
            {
                if (current == EStanceType.HighReady)
                    return;

                if (current == EStanceType.LowReady)
                {
                    StanceInputEvents.RaiseToggleLowReady();
                    return;
                }

                // Idle or any other stance → HighReady
                StanceInputEvents.RaiseToggleHighReady();
                return;
            }
        }

        public void CheckForPatrolInput()
        {
            if (Input.GetKeyDown(PluginConfig.PatrolKeybind.Value.MainKey) && PluginConfig.PatrolKeybind.Value.Modifiers.All(Input.GetKey))
            {
                StanceInputEvents.RaiseTogglePatrolStance();
            }
        }

        public void CheckForLowReadyInput()
        {
            if (Input.GetKeyDown(PluginConfig.LowReadyKeybind.Value.MainKey) && PluginConfig.LowReadyKeybind.Value.Modifiers.All(Input.GetKey))
            {
                StanceInputEvents.RaiseToggleLowReady();
            }
        }

        public void CheckForHighReadyInput()
        {
            if (Input.GetKeyDown(PluginConfig.HighReadyKeybind.Value.MainKey) && PluginConfig.HighReadyKeybind.Value.Modifiers.All(Input.GetKey))
            {
                StanceInputEvents.RaiseToggleHighReady();
            }
        }

        public void CheckForShortStockInput()
        {
            if (Input.GetKeyDown(PluginConfig.ShortStockKeybind.Value.MainKey) && PluginConfig.ShortStockKeybind.Value.Modifiers.All(Input.GetKey))
            {
                StanceInputEvents.RaiseToggleShortStock();
            }
        }

        public void CheckForMeleeInput()
        {
            if (!_meleeIsToggleable) return;

            if (Input.GetKeyDown(PluginConfig.MeleeKeybind.Value.MainKey) && PluginConfig.MeleeKeybind.Value.Modifiers.All(Input.GetKey))
            {
                StanceInputEvents.RaiseToggleMelee();
                _meleeIsToggleable = false;
            }
        }

        public void CheckForActimeAimInput()
        {
            //TODO: add active aim varient for pistols
            if (WeaponStateInstance.TreatAsPistol)
                return;

            bool activeAimOverridesAds = (Input.GetKeyDown(KeyCode.Mouse1) || Input.GetKey(KeyCode.Mouse1))
               && StanceControllerInstance.AdsIsBlocked;
            bool keyIsHeld = Input.GetKey(PluginConfig.ActiveAimKeybind.Value.MainKey)
                && PluginConfig.ActiveAimKeybind.Value.Modifiers.All(Input.GetKey);
            bool anyHeld = keyIsHeld || activeAimOverridesAds;

            if (!PluginConfig.ToggleActiveAim.Value)
            {
                if (!_activeAimHoldInProgress && anyHeld)
                {
                    _activeAimHoldInProgress = true;
                    StanceInputEvents.RaiseHoldActiveAimKeyDown();
                }
                else if (_activeAimHoldInProgress && !anyHeld)
                {
                    _activeAimHoldInProgress = false;
                    StanceInputEvents.RaiseHoldActiveAimKeyUp();
                }
            }
            else
            {
                if (!_activeAimHoldInProgress && anyHeld)
                {
                    StanceInputEvents.RaiseToggleActiveAim();
                }

                _activeAimHoldInProgress = anyHeld;
            }
        }
    }
}

