
using EFT;
using StanceOverhaul.Events;
using StanceOverhaul.Stances;
using RealismCommonLib.Utils;
using static RealismCommonLib.Plugin;
using static StanceOverhaul.Plugin;

namespace StanceOverhaul.SubSystem
{
    public class TacSprintSystem : ISubSystem
    {
        public const float TAC_SPRINT_WEIGHT_LIMIT = 5.1f;
        public const float TAC_SPRINT_WEIGHT_BULLPUP = 5.75f;
        public const int TAC_SPRINT_LENGTH_LIMIT = 6;
        public const float TAC_SPRINT_ERGO_LIMIT = 35f;
        public const float TAC_SPRINT_RADIATION_LIMIT = 50f;
        public const float TAC_SPRINT_TOXICITY_LIMIT = 50f;

        private DelayTimer _tacSprintDelayTimer = new DelayTimer(0.5f);

        public bool IsDoingTacSprint
        {
            get { return _isDoingTacSprint; }
            private set
            {
                if (_isDoingTacSprint != value)
                {
                    _isDoingTacSprint = value;
                    if (_isDoingTacSprint)
                        StanceEvents.RaiseOnTacSprintStarted();
                    else
                        StanceEvents.RaiseOnTacSprintEnded();
                }
            }
        }

        private bool _isDoingTacSprint = false;

        public bool HealthConditionPreventsTacSprint
        {
            get
            {
                return
                    HazardsStateInstance.TotalToxicity > TAC_SPRINT_TOXICITY_LIMIT ||
                    HazardsStateInstance.TotalRadiation > TAC_SPRINT_RADIATION_LIMIT ||
                    HazardsStateInstance.IsCoughingInGas ||
                    StanceControllerInstance.HealthStateAffectsStance;
            }
        }

        //TODO: weight limit should be factored by strength skill
        private bool CheckCanDoTacSprint(IStance? stance = null)
        {
            return PluginConfig.EnableTacSprint.Value
                     && PlayerStateInstance.IsSprinting
                     && PlayerStateInstance.WeaponIsReady
                     && (stance?.CanDoTacSprint ?? false)
                     && WeaponMeetsCriteria
                     && !HealthConditionPreventsTacSprint;
        }

        private bool WeaponMeetsCriteria
        {
            get
            {
                return WeaponStateInstance.TotalWeaponWeight <= (WeaponStateInstance.IsBullpup ? TAC_SPRINT_WEIGHT_BULLPUP : TAC_SPRINT_WEIGHT_LIMIT)
                    && WeaponStateInstance.WeaponLength <= TAC_SPRINT_LENGTH_LIMIT && !PlayerStateInstance.IsScav
                    && !HealthConditionPreventsTacSprint
                    && WeaponStateInstance.TotalErgo > TAC_SPRINT_ERGO_LIMIT;
            }
        }


        public void RunOnAwake()
        {
        }

        public void RunOnDestroy()
        {
        }

        private void ChangeSprintAnimation(bool tacSprint)
        {
            var animator = PlayerStateInstance.Player.BodyAnimatorCommon;
            float target = tacSprint ? 2f : WeaponStateInstance.WeaponLength;

            if (animator.GetFloat(PlayerAnimator.WEAPON_SIZE_MODIFIER_PARAM_HASH) != target)
                animator.SetFloat(PlayerAnimator.WEAPON_SIZE_MODIFIER_PARAM_HASH, target);
        }

        public void RunOnUpdate(float deltaTime)
        {
            //We were doing tac sprint, but a condition changed to block it like health state blocking it
            //Need to change tac sprint state

            var canDoTacSprint = CheckCanDoTacSprint(StanceControllerInstance.CurrentStance);

            if (!IsDoingTacSprint && canDoTacSprint)
            {
                _tacSprintDelayTimer.Stop();
                IsDoingTacSprint = true;
                ChangeSprintAnimation(true);
            }
            else if (!canDoTacSprint)
            {
                if (IsDoingTacSprint)
                {
                    _tacSprintDelayTimer.Start();
                    IsDoingTacSprint = false;
                }

                if (_tacSprintDelayTimer.Update())
                {

                    ChangeSprintAnimation(false);
                    _tacSprintDelayTimer.Stop();
                }
            }

            // if (IsDoingTacSprint)
            // {
            //     IsDoingTacSprint = true;
            //     player.BodyAnimatorCommon.SetFloat(PlayerAnimator.WEAPON_SIZE_MODIFIER_PARAM_HASH, 2f);
            //     _tacSprintTime = 0f;
            //     _canDoTacSprintTimer = true;
            // }
            // else if (RealismPluginConfig.EnableTacSprint.Value && _canDoTacSprintTimer)
            // {
            //     _tacSprintTime += Time.deltaTime;
            //     if (_tacSprintTime >= 0.5f)
            //     {
            //         player.BodyAnimatorCommon.SetFloat(PlayerAnimator.WEAPON_SIZE_MODIFIER_PARAM_HASH, WeaponStats.TotalWeaponLength);
            //         _tacSprintTime = 0f;
            //         _canDoTacSprintTimer = false;
            //     }
            //     IsDoingTacSprint = false;
            // }
            // else
            // {
            //     IsDoingTacSprint = false;
            // }
        }
    }
}
