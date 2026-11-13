using RealismCommonLib.Events;
using StanceOverhaul.Enums;
using StanceOverhaul.State;
using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using Unity;
using static RealismCommonLib.Plugin;
using static StanceOverhaul.Plugin;
using Comfort.Common;
using EFT.InputSystem;
using EFT.UI.Screens;

namespace StanceOverhaul.SubSystem.StanceInput
{
    public class StanceInputListener : ISubSystem
    {
        private const string ScrollAxisName = "Mouse Scroll";

        private bool _activeAimHoldInProgress;
        private bool _meleeIsToggleable = true;
        private float _meleeTimer = 0f;

        private bool _speedIsWheelOnly;

        private List<KeyCode[]> _aimControls = new List<KeyCode[]>();
        private List<KeyCode[]> _scrollBlockingControls = new List<KeyCode[]>();
        private List<KeyCode[]> _fireControl = new List<KeyCode[]>();

        private List<EGameKey> _scrollBlockingActions = new List<EGameKey>()
        {
            EGameKey.Duck,
            EGameKey.Next,
            EGameKey.Previous,
            EGameKey.ScopeZoomIn,
            EGameKey.ScopeZoomOut,
            EGameKey.ReloadWeapon,
        };


        private Action _unbindKeybinds;

        public bool IsHoldingFireKey
        {
            get
            {
                return AnyControlHeld(_fireControl);
            }
        }

        public bool StanceInputBlocked
        {
            get
            {
                return AimStateInstance.IsAiming
                    || PlayerStateInstance.IsSprinting
                    || PlayerStateInstance.IsInventoryOpen
                    || !PlayerStateInstance.WeaponIsReady
                    || PlayerStateInstance.IsUsingStationaryWeapon
                    || EftScreenManager.Instance.CheckCurrentScreen(EEftScreenType.MainMenu)
                    || Cursor.visible;
            }
        }

        public void RunOnAwake()
        {
            _unbindKeybinds = Singleton<EFT.Settings.SettingsManager>.Instance.Control.Settings
                .UserKeyBindings.Bind(CacheGameKeybinds);
        }

        public void RunOnDestroy()
        {
            _unbindKeybinds?.Invoke();
            _unbindKeybinds = null;
        }

        public void RunOnUpdate(float deltaTime)
        {
            StanceInputUpdate(deltaTime);
        }

        private void CacheGameKeybinds(List<KeyGroup> keyBindings)
        {
            _aimControls.Clear();
            _scrollBlockingControls.Clear();
            _fireControl.Clear();
            _speedIsWheelOnly = false;

            foreach (KeyGroup group in keyBindings)
            {
                foreach (InputSource variant in group.variants)
                {
                    // No keys to hold, so AnyControlHeld can't detect this binding
                    if (variant.keyCode.Count == 0)
                    {
                        if (variant.axisName == ScrollAxisName && (group.keyName == EGameKey.Next || group.keyName == EGameKey.Previous))
                            _speedIsWheelOnly = true;

                        continue;
                    }

                    if (_scrollBlockingActions.Contains(group.keyName))
                        _scrollBlockingControls.Add(variant.keyCode.ToArray());
                    else if (group.keyName == EGameKey.Aim)
                        _aimControls.Add(variant.keyCode.ToArray());
                    else if (group.keyName == EGameKey.Shoot)
                        _fireControl.Add(variant.keyCode.ToArray());
                }
            }
        }

        private static bool AnyControlHeld(List<KeyCode[]> controls)
        {
            foreach (KeyCode[] control in controls)
            {
                if (control.All(Input.GetKey)) return true;
            }
            return false;
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

            bool canDoMouseWheelOnly = !PluginConfig.UseMouseWheelPlusKey.Value && !_speedIsWheelOnly && !AnyControlHeld(_scrollBlockingControls);
            bool canDoMouseWheelPlusKey = Input.GetKey(PluginConfig.StanceWheelComboKeyBind.Value.MainKey) && PluginConfig.UseMouseWheelPlusKey.Value;

            if (canDoMouseWheelPlusKey || canDoMouseWheelOnly)
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

            bool activeAimOverridesAds = AnyControlHeld(_aimControls) && StanceControllerInstance.AdsIsBlocked;
            // Configs from before the selection wheel still have active aim on the wheel's default key.
            // The wheel wins, or every press that opens it would also enter active aim.
            KeyCode activeAimKey = PluginConfig.ActiveAimKeybind.Value.MainKey;
            bool keyIsUsedByWheel = activeAimKey != KeyCode.None && activeAimKey == PluginConfig.StanceSelectorKeybind.Value.MainKey;

            bool keyIsHeld = !keyIsUsedByWheel
                && Input.GetKey(activeAimKey)
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

