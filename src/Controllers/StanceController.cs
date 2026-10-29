using EFT;
using EFT.Animations;
using HarmonyLib;
using RealismCommonLib.Events;
using RealismCommonLib.Utils;
using StanceOverhaul.Controllers.PatchHooks;
using StanceOverhaul.SubSystem;
using StanceOverhaul.Enums;
using StanceOverhaul.SubSystem.Aiming;
using StanceOverhaul.SubSystem.StanceInput;
using StanceOverhaul.SubSystem.Animator;
using StanceOverhaul.Stances;
using StanceOverhaul.State;
using StanceOverhaul.Events;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using static RealismCommonLib.Plugin;

namespace StanceOverhaul.Controllers
{
    public class StanceController : MonoBehaviour
    {
        public const float HIGH_READY_RADIATION_LIMIT = 80f;
        public const float HIGH_READY_TOXICITY_LIMIT = 80f;
        public const float LEFT_SHOULDER_SWAY_MULTI = 1.3f;
        public const float BASE_OFFSET_SPRING_FACTOR = 2f;

        private EStanceType _targetStance = EStanceType.None;

        private static FieldInfo _pwaAimField = AccessTools.Field(typeof(ProceduralWeaponAnimation), "_aimingSpeed");

        public bool AwakeRan { get; private set; } = false;

        //TODO: move to aim controller
        public float PwaAimSpeed
        {
            get
            {
                return (float)_pwaAimField.GetValue(PlayerStateInstance.PWA);
            }
            set
            {
                _pwaAimField.SetValue(PlayerStateInstance.PWA, value);
            }
        }

        public float PwaOriginalAimSpeed { get; set; }

        public bool IsBlindFiring
        {
            get
            {
                return PlayerStateInstance.PWA.BlindfireBlender.Value > 0f;
            }
        }

        public bool IsInThirdPerson
        {
            get
            {
                return PlayerStateInstance.Player.PointOfView == EPointOfView.ThirdPerson;
            }
        }

        //TODO: move to low ready stance class if force low ready will be a thing
        //TODO: move to stance health controller
        public bool CanDoHighReadyInjuredAnim
        {
            get
            {
                return CurrentStanceType == EStanceType.HighReady && HealthConditionForcesLowReady;
            }
        }

        //TODO move to aim subsystem
        public bool AdsIsBlocked
        {
            get
            {
                return !AimStateInstance.PlayerCanAim;
            }
        }

        //TODO move to mounting/bracing controller
        public Vector3 MountPos { get; set; }
        public Vector3 MountDir { get; set; }

        //TODO move to collision controller
        public float BaseWeaponLength { get; set; }
        //TODO use this somewhere
        public float StanceModifiedWeaponLength { get; set; }

        //TODO: implement in a stance state controller
        public float StanceSwayFactor
        {
            get
            {
                return
                    CurrentStanceType == EStanceType.LeftShoulder ? LEFT_SHOULDER_SWAY_MULTI : 1f;
            }
        }

        //TODO: move to a stance health controller
        public bool HealthStateAffectsStance
        {
            get
            {
                return
                    HealthStateInstance.ArmsAreIncapacitated ||
                    HealthStateInstance.HasOverdosed ||
                    HealthStateInstance.IsPoisoned ||
                    HealthStateInstance.IsDehydrated ||
                    HealthStateInstance.IsExhausted;
            }
        }

        //TODO will no longer force low ready? will make default stance instead of ready?
        //TODO: move to a stance health controller
        public bool HealthConditionForcesLowReady
        {
            get
            {
                return
                    HazardsStateInstance.TotalToxicity > HIGH_READY_TOXICITY_LIMIT ||
                    HazardsStateInstance.TotalRadiation > HIGH_READY_RADIATION_LIMIT ||
                    HealthStateAffectsStance;
            }
        }

        public bool CanMoveGunToCamera
        {
            get
            {
                // left shoulder is not gated here, it's blended by stance progress via LeftShoulderPoseWeight
                return PluginConfig.EnableAltAim.Value && !PlayerStateInstance.IsMounting && !PlayerStateInstance.IsUsingStationaryWeapon;
            }
        }

        // 0 = not in left shoulder, 1 = fully at left shoulder pose, follows the stance's enter/exit progress
        public float LeftShoulderPoseWeight
        {
            get
            {
                return _stanceState != null ? _stanceState.GetPoseWeight(EStanceType.LeftShoulder) : 0f;
            }
        }

        // eased version of CanMoveGunToCamera, used to blend the camera target with the gun offset
        public float AltAimWeight
        {
            get
            {
                return _aimSystem != null ? _aimSystem.AltAimWeight : 0f;
            }
        }

        //TODO: use const for weight
        //replace with debuffs and animations
        /*       public bool ShouldForceLowReady
               {
                   get
                   {
                       return (HealthConditionForcesLowReady || (WeaponStateInstance.TotalWeaponWeight >= 10f && !IsMounting))
                           && !AimStateInstance.IsAiming
                           && !FiringStateInstance.IsFiringFromStance
                           && TargetStance != EStance.PistolCompressed
                           && TargetStance != EStance.PatrolStance
                           && TargetStance != EStance.ShortStock
                           && TargetStance != EStance.ActiveAiming
                           && MeleeIsToggleable
                           && !IsBracing;
                   }
               }*/

        public Vector3 StanceRotation
        {
            get
            {
                return _stanceState.StanceRotation;
            }
        }

        public Vector3 StancePosition
        {
            get
            {
                return _stanceState.StancePosition;
            }
        }

        public Vector3 BaseWeaponOffsetPosition
        {
            get
            {
                return _weaponOffsetAnimator.BaseWeaponOffsetPosition;
            }
        }

        /// <summary>
        /// The base offset as added to HandsPosition.Get(), applied directly instead of through a spring.
        /// It used to go through StancePositionSpring, which settles at Get() = 2 x Zero, so the factor keeps its on-screen size.
        /// </summary>
        public Vector3 AppliedBaseOffsetPosition
        {
            get
            {
                ModLogger.LogWarning($"Base: {BASE_OFFSET_SPRING_FACTOR * _weaponOffsetAnimator.EasedBaseWeaponOffsetPosition}");
                return BASE_OFFSET_SPRING_FACTOR * _weaponOffsetAnimator.EasedBaseWeaponOffsetPosition;
            }
        }

        /// <summary>
        /// Everything SpringGetPatch adds to HandsPosition.Get() apart from the base offset, in weapon root parent space.
        /// </summary>
        public Vector3 StanceAndOffsetPosition
        {
            get
            {
                return StancePositionSpring.Get() + OffsetPositionSpring.Get();
            }
        }

        public Vector3 DetailsOffsetPosition
        {
            get
            {
                return _weaponOffsetAnimator.DetailsPositionOffset;
            }
        }

        public Vector3 DetailsOffsetRotation
        {
            get
            {
                return _weaponOffsetAnimator.DetailsRotationOffset;
            }
        }

        public bool BlockLeftHandSFX
        {
            get
            {
                return _leftHandAnimator.BlockLeftHandSFX;
            }
        }

        public bool IsDoingLeftHandOverride
        {
            get
            {
                return _leftHandAnimator.IsDoingOverride;
            }
        }

        private List<ISubSystem> _stateControllers = new List<ISubSystem>();
        private InputHookPipeline _inputHookPipeline;
        private StanceInputHandler _inputHandler;
        private StanceInputListener _inputListener;
        private StanceStaminaSystem _staminaSystem;
        private StanceMovementSystem _movementSystem;
        private TacSprintSystem _tacSprintSystem;
        private StatsSystem _statsSystem;
        private StanceAimSystem _aimSystem;
        private StanceState _stanceState;
        private StanceAudioSystem _stanceAudioSystem;
        private StanceReloadSpeedSystem _reloadSpeedSystem;
        private WeaponOffsetAnimator _weaponOffsetAnimator;
        private LeftHandAnimaor _leftHandAnimator;

        public Spring StancePositionSpring { get; private set; }
        public Spring StanceRotationSpring { get; private set; }
        public Spring OffsetPositionSpring { get; private set; }
        public Spring OffsetRotationSpring { get; private set; }

        private List<StanceBase> _stances = new List<StanceBase>();
        public PatrolStance PatrolStance { get; private set; }
        public LeftShoulder LeftShoulder { get; private set; }
        public LowReady LowReady { get; private set; }
        public HighReady HighReady { get; private set; }
        public ActiveAim ActiveAim { get; private set; }
        public ShortStock ShortStock { get; private set; }
        public PistolCompress PistolCompress { get; private set; }

        public EStanceReloadType CurrentReloadType => _reloadSpeedSystem.CurrentReloadType;
        public EStanceType CurrentStanceType => _stanceState.ActiveStanceType;
        public IStance? CurrentStance => _stanceState.ActiveStance;
        public StatsSystem StatsHandlerInstance => _statsSystem;
        public bool IsDoingTacSprint => _tacSprintSystem.IsDoingTacSprint;

        // never while aiming, for any stance, so a hipfire bonus can't leak into ADS
        public float StanceHipfireBonus => AimStateInstance.IsAiming ? 1f : _stanceState.ActiveStance?.HipfireBonus ?? 1f;

        void Awake()
        {
            Plugin.StanceControllerInstance = this;

            InitSprings();
            InitStateControllers();
            SubscribeToInputEvents();
            SubscribeToStanceEvents();
            InitStances();

            AwakeRan = true;
        }

        void Update()
        {
            float regularTime = Time.deltaTime;

            if (!CanDoUpdate()) return;

            RunUpdates(regularTime);
        }

        void OnDestroy()
        {
            RunStanceDispose();
            RunControllerOnDestroy();
            UnsubscribeFromInputEvents();
            UnsubscribeFromStanceEvents();
        }

        private void InitStances()
        {
            PatrolStance =
                InitStance(() => new PatrolStance());

            LeftShoulder =
                InitStance(() => new LeftShoulder());

            HighReady =
                InitStance(() => new HighReady());

            LowReady =
                InitStance(() => new LowReady());

            ActiveAim =
                InitStance(() => new ActiveAim());

            ShortStock =
                InitStance(() => new ShortStock());

            PistolCompress =
                InitStance(() => new PistolCompress());

        }

        private T InitStance<T>(Func<T> factory) where T : StanceBase
        {
            var instance = factory();
            _stances.Add(instance);
            return instance;
        }

        private void InitSprings()
        {
            StancePositionSpring = Cloner.ShallowClone(PlayerStateInstance.PWA.HandsContainer.HandsPosition);
            StanceRotationSpring = Cloner.ShallowClone(PlayerStateInstance.PWA.HandsContainer.HandsRotation);

            OffsetPositionSpring = Cloner.ShallowClone(PlayerStateInstance.PWA.HandsContainer.HandsPosition);
            OffsetRotationSpring = Cloner.ShallowClone(PlayerStateInstance.PWA.HandsContainer.HandsRotation);
        }

        private void InitStateControllers()
        {
            _stanceState =
                InitStateController(() => new StanceState());

            _inputHookPipeline =
                InitStateController(() => new InputHookPipeline());

            _statsSystem =
               InitStateController(() => new StatsSystem());

            _staminaSystem =
                InitStateController(() => new StanceStaminaSystem());

            _movementSystem =
                InitStateController(() => new StanceMovementSystem());

            _tacSprintSystem =
                InitStateController(() => new TacSprintSystem());

            _aimSystem =
                InitStateController(() => new StanceAimSystem());

            _inputHandler =
                InitStateController(() => new StanceInputHandler(_stanceState));

            _inputListener =
                InitStateController(() => new StanceInputListener());

            _stanceAudioSystem =
                InitStateController(() => new StanceAudioSystem());

            _reloadSpeedSystem =
                InitStateController(() => new StanceReloadSpeedSystem());

            _weaponOffsetAnimator =
                InitStateController(() => new WeaponOffsetAnimator());

            _leftHandAnimator =
               InitStateController(() => new LeftHandAnimaor());

            RunControllerAwake();
        }

        private T InitStateController<T>(Func<T> factory) where T : SubSystem.ISubSystem
        {
            var instance = factory();
            _stateControllers.Add(instance);
            return instance;
        }

        private void RunControllerAwake()
        {
            foreach (SubSystem.ISubSystem controller in _stateControllers)
            {
                controller.RunOnAwake();
            }
        }

        private void RunUpdates(float deltaTime)
        {
            foreach (SubSystem.ISubSystem controller in _stateControllers)
            {
                controller.RunOnUpdate(deltaTime);
            }
        }

        private void RunControllerOnDestroy()
        {
            foreach (SubSystem.ISubSystem controller in _stateControllers)
            {
                controller.RunOnDestroy();
            }
        }

        private void RunStanceDispose()
        {
            foreach (StanceBase stance in _stances)
            {
                stance.Dispose();
            }
        }

        public void RunAfterEFTTransformsSet(ProceduralWeaponAnimation pwa, float dt)
        {
            _aimSystem.MoveGunToCamera(pwa, dt);
        }

        private void SubscribeToStanceEvents()
        {
            StanceEvents.OnPrimaryStanceChanged += UpdatePlayerStateOnStanceChange;
            PlayerEvents.AimStateChanged += UpdateHipInaccuracyOnAimChange;
        }

        private void UnsubscribeFromStanceEvents()
        {
            StanceEvents.OnPrimaryStanceChanged -= UpdatePlayerStateOnStanceChange;
            PlayerEvents.AimStateChanged -= UpdateHipInaccuracyOnAimChange;
        }

        private void UpdatePlayerStateOnStanceChange()
        {
            PlayerStateInstance.ResetProceduralState();
        }

        // hip inaccuracy is a snapshot taken by UpdateHipInaccuracy, re-take it so StanceHipfireBonus follows the aim state
        // (and a refresh during ADS, e.g. a scope change, doesn't leave the bonus off after aiming)
        private void UpdateHipInaccuracyOnAimChange()
        {
            PlayerStateInstance.FirearmController?.UpdateHipInaccuracy();
        }

        private void SubscribeToInputEvents()
        {
            PlayerEvents.OnWeaponEquipped -= OnWeaponSwap;
            InputEvents.WeaponSwapInput += OnWeaponSwap;
            InputEvents.ToggleStepOutInput += OnToggleStepOut;
            InputEvents.ChangeStanceInput += OnChangeStance;
            InputEvents.ToggleBipodInput += OnToggleBipod;
            InputEvents.ToggleBreathingInput += OnToggleBreathing;
        }

        private void UnsubscribeFromInputEvents()
        {
            PlayerEvents.OnWeaponEquipped -= OnWeaponSwap;
            InputEvents.WeaponSwapInput -= OnWeaponSwap;
            InputEvents.ToggleStepOutInput -= OnToggleStepOut;
            InputEvents.ChangeStanceInput -= OnChangeStance;
            InputEvents.ToggleBipodInput -= OnToggleBipod;
            InputEvents.ToggleBreathingInput -= OnToggleBreathing;
        }

        //TODO: check if GameStateInstance.WeaponIsReady is essentially the same thing,
        //would make check in StanceStateUpdate redundant
        private bool CanDoUpdate()
        {
            Player player = PlayerStateInstance.Player;
            return player != null && GameStateInstance.PlayerIsInRaidOrHideout;
        }

        //TODO: replace all usages of DidWeaponSwap with events
        private void OnWeaponSwap()
        {
        }

        private void OnToggleStepOut()
        {
        }

        private void OnChangeStance()
        {
        }

        private void OnToggleBipod()
        {
        }

        //TODO: move to wiggle effects class
        private void OnToggleBreathing()
        {
            if (AimStateInstance.IsAiming)
            {
                Player player = PlayerStateInstance.Player;
                if (player.Physical.HoldingBreath) return;

                //TODO: replace with animation bringing gun closer into shoulder
                //if (!IsChonker) DoWiggleEffects(player, player.ProceduralWeaponAnimation, WeaponStateInstance.WeaponInstance, new Vector3(0.25f, 0.25f, 0.5f), wiggleFactor: 0.5f);
            }
        }


    }
}

