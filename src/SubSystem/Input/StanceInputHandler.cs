using System;
using System.Linq;
using RealismCommonLib.Events;
using StanceOverhaul.Events;
using StanceOverhaul.Enums;
using StanceOverhaul.Stances;
using StanceOverhaul.State;
using static RealismCommonLib.Plugin;
using static StanceOverhaul.Plugin;

namespace StanceOverhaul.SubSystem.StanceInput
{
    // Yes, the input system is AI-slopified, except it saved me hours of work dealing with persistent bugs and actually works, sue me!
    //
    //
    // Turns player input and game events into stance changes.
    //
    // The handler keeps two kinds of state and nothing else:
    //
    //   Intent       _chosen            the stance the player has toggled on (null = none)
    //                _beforeActiveAim   the stance to go back to when active aim is switched off
    //                _leftShoulderFromLean / _beforeLeanLeftShoulder
    //                                   left shoulder was put on by a full left lean, and what to go back to when it ends
    //
    //   Suppressors  aiming (plus a short settle delay, only for stances without IStance.DoesNotCancelOnAds),
    //                a reload that pauses the stance, an item in hand.
    //                While one applies, the stance is put away but the intent is left alone.
    //
    // Every event edits intent and/or a suppressor and then calls Reconcile(), the only method that talks to
    // StanceState:
    //
    //      wanted = suppressed ? nothing : (chosen ?? default stance)
    //
    // Putting a stance back is therefore never a separate code path, it is just "recompute". Overlapping
    // interrupts (ADS during a reload, a weapon swap while aiming, ...) need no special cases: whichever
    // suppressor ends last lets the stance back in.
    //
    // This handler is assumed to be the only thing requesting stances from StanceState.
    internal class StanceInputHandler : ISubSystem
    {
        // After ADS ends, wait this long before bringing the stance back. If the aim state flickers while the
        // ADS transition settles, the stance is never re-requested, so it can't flash on screen.
        private const float RestoreAfterADSDelay = 0.05f;

        private readonly StanceState _stanceState;

        // ---- Intent ----
        private IStance? _chosen;
        private IStance? _beforeActiveAim;
        private bool _leftShoulderFromLean;
        private IStance? _beforeLeanLeftShoulder;

        // ---- Suppressors ----
        private bool _isAiming;
        private float _aimSettleTimer;
        private bool _reloadPausesStance;
        private bool _itemInHand;

        public StanceInputHandler(StanceState stanceState)
        {
            _stanceState = stanceState;
        }

        // ================= Lifecycle =================

        public void RunOnAwake()
        {
            SubscribeToEvents();
        }

        public void RunOnDestroy()
        {
            UnSubscribeToEvents();
        }

        public void RunOnUpdate(float deltaTime)
        {
            if (_aimSettleTimer <= 0f)
                return;

            _aimSettleTimer -= deltaTime;

            if (_aimSettleTimer <= 0f)
                Reconcile();
        }

        private void SubscribeToEvents()
        {
            PlayerEvents.OnWeaponSwap += OnWeaponSwap;
            PlayerEvents.OnSwappedFromItemToGun += OnSwappedBackToGun;
            PlayerEvents.OnSwappedFromGunToItem += OnSwappedToItem;
            PlayerEvents.AimStateChanged += OnAimStateChanged;
            PlayerEvents.OnShotFired += OnFireAttempt;
            StanceInputEvents.OnAttemptedToFireFromStance += OnFireAttempt;
            StanceInputEvents.TogglePatrolStance += TogglePatrolStance;
            StanceInputEvents.ToggleHighReady += ToggleHighReady;
            StanceInputEvents.ToggleLowReady += ToggleLowReady;
            StanceInputEvents.ToggleShortStock += ToggleShortStock;
            StanceInputEvents.ToggleActiveAim += ToggleActiveAim;
            StanceInputEvents.OnActiveAimKeyDown += EnterActiveAim;
            StanceInputEvents.OnActiveAimKeyUp += ExitActiveAim;
            InputEvents.ToggleLeftStanceInput += ToggleLeftShoulder;
            PlayerEvents.OnFullLeftLeanChanged += OnFullLeftLeanChanged;
            StanceEvents.OnStanceReload += OnReloadStarted;
            StanceEvents.OnStanceReloadReset += OnReloadFinished;
            StanceEvents.OnTransformsInit += OnWeaponInit;
            StanceEvents.OnStationaryWeaponOperated += _stanceState.CancelAll;
            // TODO: StanceInputEvents.ToggleMelee
            // TODO: StanceInputEvents.ToggleMounting, decide if it will override BSG mounting
        }

        private void UnSubscribeToEvents()
        {
            PlayerEvents.OnWeaponSwap -= OnWeaponSwap;
            PlayerEvents.OnSwappedFromItemToGun -= OnSwappedBackToGun;
            PlayerEvents.OnSwappedFromGunToItem -= OnSwappedToItem;
            PlayerEvents.AimStateChanged -= OnAimStateChanged;
            PlayerEvents.OnShotFired -= OnFireAttempt;
            StanceInputEvents.OnAttemptedToFireFromStance -= OnFireAttempt;
            StanceInputEvents.TogglePatrolStance -= TogglePatrolStance;
            StanceInputEvents.ToggleHighReady -= ToggleHighReady;
            StanceInputEvents.ToggleLowReady -= ToggleLowReady;
            StanceInputEvents.ToggleShortStock -= ToggleShortStock;
            StanceInputEvents.ToggleActiveAim -= ToggleActiveAim;
            StanceInputEvents.OnActiveAimKeyDown -= EnterActiveAim;
            StanceInputEvents.OnActiveAimKeyUp -= ExitActiveAim;
            InputEvents.ToggleLeftStanceInput -= ToggleLeftShoulder;
            PlayerEvents.OnFullLeftLeanChanged -= OnFullLeftLeanChanged;
            StanceEvents.OnStanceReload -= OnReloadStarted;
            StanceEvents.OnStanceReloadReset -= OnReloadFinished;
            StanceEvents.OnTransformsInit -= OnWeaponInit;
            StanceEvents.OnStationaryWeaponOperated -= _stanceState.CancelAll;
        }

        // ================= Rules and policy =================

        // The stance that is on by default whenever nothing is chosen and nothing is suppressing it.
        // Null = no default. To make a rifle default (e.g. low ready) later, return it here under a config
        // check, nothing else needs to change.
        private static IStance? DefaultStance =>
            WeaponStateInstance.TreatAsPistol ? StanceControllerInstance.PistolCompress : null;

        // Aiming, including the short settle window after it ends.
        private bool IsAimSuppressing => _isAiming || _aimSettleTimer > 0f;

        private bool IsSuppressed(IStance stance)
        {
            if (_itemInHand || _reloadPausesStance)
                return true;

            // Stances compatible with ADS (left shoulder, active aim) are left alone by aiming,
            // StanceState fades out the parts of their pose they flag with AimPositionScale/AimRotationScale
            return IsAimSuppressing && !stance.DoesNotCancelOnADS;
        }

        // Stances that come back after aiming (and after item use, if RememberStanceItem is on).
        // Active aim always counts, whatever its RememberStance flag says.
        private static bool IsRemembered(IStance? stance)
        {
            return stance != null &&
                   (stance.RememberStance || stance.StanceType == EStanceType.ActiveAiming);
        }

        private static bool IsActiveAim(IStance? stance)
        {
            return stance?.StanceType == EStanceType.ActiveAiming;
        }

        // A stance that blocks firing is dropped when the player tries to shoot, so the shot can go through.
        // (Flip this if you want the opposite: "firing cancels stances that do NOT block it".)
        private static bool FiringDropsStance(IStance stance)
        {
            return stance.BlocksFiring;
        }

        // What the player may toggle on the current weapon: pistols only get patrol (plus their default,
        // which is never toggled), rifles never get the pistol stance.
        private static bool IsAllowedOnCurrentWeapon(IStance stance)
        {
            return WeaponStateInstance.TreatAsPistol
                ? stance.StanceType == StanceControllerInstance.PatrolStance.StanceType
                : stance.StanceType != EStanceType.PistolCompress;
        }

        // ================= Core =================

        private void SetChosen(IStance? stance)
        {
            _chosen = stance;
            _beforeActiveAim = null; // only meaningful while active aim is chosen, callers set it afterwards

            // Any other change of intent (a key press, a weapon swap, ...) means the lean no longer owns left shoulder
            _leftShoulderFromLean = false;
            _beforeLeanLeftShoulder = null;
        }

        // Makes StanceState match what should be showing right now. Idempotent, safe to call from anywhere.
        private void Reconcile()
        {
            var target = _chosen ?? DefaultStance;
            var wanted = target != null && !IsSuppressed(target) ? target : null;
            var active = _stanceState.ActiveStance;

            if (wanted == null)
            {
                if (active != null)
                    _stanceState.CancelAll();
            }
            else if (active?.StanceType != wanted.StanceType)
            {
                // Never request what is already active: RequestStance toggles.
                _stanceState.RequestStance(wanted);
            }
        }

        // ================= Player input =================

        // Shared by every stance key. Keys are ignored while the stance they would turn on is suppressed.
        private void ToggleStance(IStance? stance)
        {
            if (stance == null || !IsAllowedOnCurrentWeapon(stance) || IsSuppressed(stance))
                return;

            // Decide on/off from what is really active, so a stance that got cancelled behind our back can be
            // re-entered with a single press.
            bool switchingOff = _stanceState.ActiveStance?.StanceType == stance.StanceType;

            SetChosen(switchingOff ? null : stance);
            Reconcile();
        }

        private void TogglePatrolStance()
        {
            ToggleStance(StanceControllerInstance.PatrolStance);
        }

        private void ToggleLeftShoulder()
        {
            ToggleStance(StanceControllerInstance.LeftShoulder);
        }

        private void ToggleHighReady()
        {
            ToggleStance(StanceControllerInstance.HighReady);
        }

        private void ToggleLowReady()
        {
            ToggleStance(StanceControllerInstance.LowReady);
        }

        private void ToggleShortStock()
        {
            ToggleStance(StanceControllerInstance.ShortStock);
        }

        private void ToggleActiveAim()
        {
            if (_stanceState.ActiveStance?.StanceType == EStanceType.ActiveAiming)
                ExitActiveAim();
            else
                EnterActiveAim();
        }

        private void EnterActiveAim()
        {
            var activeAim = StanceControllerInstance.ActiveAim;

            if (!IsAllowedOnCurrentWeapon(activeAim) || IsSuppressed(activeAim))
                return;

            if (!IsActiveAim(_chosen))
            {
                // Only a remembered stance is worth going back to
                var previous = IsRemembered(_chosen) ? _chosen : null;

                SetChosen(activeAim);
                _beforeActiveAim = previous;
            }

            Reconcile();
        }

        // Also the key-up path for hold-to-active-aim, so it is honoured even while the stance is put away
        // (released during ADS or a reload): what comes back is the stance we had before active aim.
        private void ExitActiveAim()
        {
            if (!IsActiveAim(_chosen))
                return;

            SetChosen(_beforeActiveAim);
            Reconcile();
        }

        // ================= Leaning =================

        private void OnFullLeftLeanChanged(bool isFullyLeaningLeft)
        {
            if (isFullyLeaningLeft)
                EnterLeanLeftShoulder();
            else
                ExitLeanLeftShoulder();
        }

        private void EnterLeanLeftShoulder()
        {
            var leftShoulder = StanceControllerInstance.LeftShoulder;

            if (!PluginConfig.LeanLeftTogglesLeftShoulder.Value || !CanLeanEnterLeftShoulder(leftShoulder))
                return;

            // Only a remembered stance is worth going back to
            var previous = IsRemembered(_chosen) ? _chosen : null;

            SetChosen(leftShoulder);
            _leftShoulderFromLean = true;
            _beforeLeanLeftShoulder = previous;

            Reconcile();
        }

        // Not gated by the config, so a lean that started with it on still cleans up after itself.
        private void ExitLeanLeftShoulder()
        {
            // Cleared by SetChosen if anything (e.g. the left shoulder key) changed the stance during the lean
            if (!_leftShoulderFromLean)
                return;

            SetChosen(_beforeLeanLeftShoulder);
            Reconcile();
        }

        private bool CanLeanEnterLeftShoulder(IStance leftShoulder)
        {
            var isLeft = _chosen?.StanceType == EStanceType.LeftShoulder || _stanceState.ActiveStance?.StanceType == EStanceType.LeftShoulder;
            var weaponAllowsLeft = !PlayerStateInstance.WeaponIsReady || WeaponStateInstance.IsPistol;
            var statePreventsLeft = PlayerStateInstance.IsUsingStationaryWeapon || PlayerStateInstance.IsMounting;

            if (isLeft || weaponAllowsLeft || statePreventsLeft)
                return false;

            // Same rules as the key: no stockless machine pistols, not while an item is in hand or a reload pauses it.
            // Aiming does not block it, left shoulder survives ADS.
            return IsAllowedOnCurrentWeapon(leftShoulder) && !IsSuppressed(leftShoulder);
        }

        // ================= Aiming =================

        private void OnAimStateChanged()
        {
            bool aiming = AimStateInstance.IsAiming;

            if (aiming == _isAiming)
                return;

            _isAiming = aiming;

            if (aiming)
                OnADSStarted();
            else
                _aimSettleTimer = _stanceState.IsTransitioning ? RestoreAfterADSDelay : 0.001f; // restored from RunOnUpdate, so a flicker can't flash the stance
        }

        private void OnADSStarted()
        {
            // Aiming again before the stance came back: drop the pending restore
            _aimSettleTimer = 0f;

            // Only remembered stances (and ones that are compatible with ADS) outlive aiming
            if (_chosen != null && !_chosen.DoesNotCancelOnADS && !IsRemembered(_chosen))
                SetChosen(null);

            // High Ready -> Active Aim -> ADS: forget High Ready, active aim stays through ADS and is still on after it
            _beforeActiveAim = null;

            Reconcile();
        }

        // ================= Firing =================

        // Both "a shot was fired" and "tried to fire from a stance" end up here.
        private void OnFireAttempt()
        {
            if (_chosen == null || !FiringDropsStance(_chosen))
                return;

            // RememberStanceFiring: shooting while aiming doesn't forget the stance waiting to come back.
            if (PluginConfig.RememberStanceFiring.Value && IsAimSuppressing)
                return;

            // The default stance is the baseline and is never dropped, Reconcile puts it back if needed
            SetChosen(null);
            Reconcile();
        }

        // ================= Reload =================

        private void OnReloadStarted()
        {
            // Look at the stance the player is in, even if ADS currently has it put away
            var target = _chosen ?? DefaultStance;

            _reloadPausesStance = target != null &&
                                  target.ReloadTypesThatPauseStance.Contains(StanceControllerInstance.CurrentReloadType);

            Reconcile();
        }

        private void OnReloadFinished()
        {
            _reloadPausesStance = false;
            Reconcile();
        }

        // ================= Items and weapons =================

        private void OnSwappedToItem()
        {
            _itemInHand = true;

            // Swapping hands ends aiming and aborts any reload. Don't wait for events that might not come.
            _isAiming = false;
            _aimSettleTimer = 0f;
            _reloadPausesStance = false;

            if (_chosen != null && !(PluginConfig.RememberStanceItem.Value && IsRemembered(_chosen)))
                SetChosen(null);

            Reconcile();
        }

        private void OnSwappedBackToGun()
        {
            _itemInHand = false;
            Reconcile();
        }

        // Swapping weapons wipes everything, including the suppressors, so nothing from the previous weapon
        // (an ADS that never reported ending, a reload that never finished) can leak into the new one.
        // The default stance of the new weapon, if it has one, comes straight back through Reconcile.
        private void OnWeaponSwap()
        {
            SetChosen(null);
            _isAiming = false;
            _aimSettleTimer = 0f;
            _reloadPausesStance = false;
            _itemInHand = false;

            Reconcile();
        }

        // The new weapon is ready: make sure the stance state matches it (pistol compressed up, or a stance from
        // the previous weapon gone).
        private void OnWeaponInit()
        {
            Reconcile();
        }
    }
}