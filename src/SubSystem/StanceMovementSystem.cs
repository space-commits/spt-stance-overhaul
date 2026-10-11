using RealismCommonLib.ModifierHandlers;
using StanceOverhaul.Events;
using StanceOverhaul.Stances;
using Comfort.Common;
using static RealismCommonLib.Plugin;
using static StanceOverhaul.Plugin;
using EFT;

namespace StanceOverhaul.SubSystem
{

    //TODO weapon stats should affect this
    internal class StanceMovementSystem : ISubSystem
    {
        private FloatMultiplierHandle _walkSpeed;
        private FloatMultiplierHandle _sprintSpeed;
        private FloatMultiplierHandle _preSprintAccelSpeed;
        private FloatMultiplierHandle _sprintAccelSpeed;

        public void RunOnAwake()
        {
            StanceEvents.OnStanceEntered += OnStanceEntered;
            StanceEvents.OnStanceExited += OnStanceExited;
            StanceEvents.OnTacSprintStarted += OnExternalStateChange;
            StanceEvents.OnTacSprintEnded += OnExternalStateChange;
            RealismCommonLib.Events.PlayerEvents.OnWeaponEquipped += OnExternalStateChange;

            _walkSpeed = StatModifiers.WalkSpeedModifier.Add(1f);
            _sprintSpeed = StatModifiers.SprintSpeedModifier.Add(1f);
            _preSprintAccelSpeed = StatModifiers.PreSprintAccelModifier.Add(1f);
            _sprintAccelSpeed = StatModifiers.SprintAccelModifier.Add(1f);
        }

        public void RunOnDestroy()
        {
            StanceEvents.OnStanceEntered -= OnStanceEntered;
            StanceEvents.OnStanceExited -= OnStanceExited;
            StanceEvents.OnTacSprintStarted -= OnExternalStateChange;
            StanceEvents.OnTacSprintEnded -= OnExternalStateChange;
            RealismCommonLib.Events.PlayerEvents.OnWeaponEquipped -= OnExternalStateChange;

            StatModifiers.WalkSpeedModifier.Remove(_walkSpeed);
            StatModifiers.SprintSpeedModifier.Remove(_sprintSpeed);
            StatModifiers.PreSprintAccelModifier.Remove(_preSprintAccelSpeed);
            StatModifiers.SprintAccelModifier.Remove(_sprintAccelSpeed);
        }

        public void RunOnUpdate(float deltaTime)
        {
        }

        private void OnExternalStateChange()
        {
            OnStanceEntered(StanceControllerInstance.CurrentStance ?? null);
        }

        private void OnStanceEntered(IStance? stance)
        {
            var physical = PlayerStateInstance.Player?.Physical;
            if (physical == null) return;

            //TODO: if in future tac sprint can be done wihout a stance, this will need to be handled in this method and still apply bonuses rather than short circuiting
            if (stance == null)
            {
                OnStanceExited();
                return;
            }

            GlobalConfiguration.InertiaSettings inertia = Singleton<GlobalConfiguration>.Instance.Inertia;

            //Todo: move to common lib
            physical.SprintAcceleration = inertia.SprintAccelerationLimits.InverseLerp(physical.Inertia);
            physical.PreSprintAcceleration = inertia.PreSprintAccelerationLimits.Evaluate(physical.Inertia);

            float tacSprintAccelBonus = StanceControllerInstance.IsDoingTacSprint ? PluginConfig.TacSprintAccelBonus.Value : 1f;

            //Todo: move to common lib
            physical.SprintAcceleration *= stance.SprintAccelBonus * tacSprintAccelBonus;
            physical.PreSprintAcceleration *= stance.SprintAccelBonus * tacSprintAccelBonus;

            _walkSpeed.Multiplier = stance.WalkSpeedBonus;

            _sprintSpeed.Multiplier = StanceControllerInstance.IsDoingTacSprint ? PluginConfig.TacSprintSpeedBonus.Value : 1f;

            //ModLogger.LogWarning($"WalkSpeedBonus={stance.WalkSpeedBonus}, SprintAccelBonus={stance.SprintAccelBonus}, IsDoingTacSprint={StanceControllerInstance.IsDoingTacSprint}, TacSprintAccelBonus={tacSprintAccelBonus}, TacSprintSpeedBonus={PluginConfig.TacSprintSpeedBonus.Value}");

            // _preSprintAccelSpeed.Multiplier = stance.SprintAccelBonus; //*  IsDoingTacSprint ? TAC_SPRINT_ACCEL : 1f;
            // _sprintAccelSpeed.Multiplier = stance.SprintAccelBonus; // *  IsDoingTacSprint ? TAC_SPRINT_ACCEL : 1f;

            //_sprintSpeed.Multiplier = IsDoingTacSprint ? TAC_SPRINT_SPEED_BONUS * (1f + PlayerStateInstance.Player.Skills.EnduranceBuffRestoration.Value) : 1f;
        }

        private void OnStanceExited()
        {
            var physical = PlayerStateInstance.Player?.Physical;
            if (physical == null) return;

            GlobalConfiguration.InertiaSettings inertia = Singleton<GlobalConfiguration>.Instance.Inertia;

            physical.SprintAcceleration = inertia.SprintAccelerationLimits.InverseLerp(physical.Inertia);
            physical.PreSprintAcceleration = inertia.PreSprintAccelerationLimits.Evaluate(physical.Inertia);

            _walkSpeed.Multiplier = 1f;
            _sprintSpeed.Multiplier = 1f;
            // _preSprintAccelSpeed.Multiplier = 1f;
            // _sprintAccelSpeed.Multiplier = 1f;
        }
    }
}
