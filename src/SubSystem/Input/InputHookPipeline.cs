using EFT.InputSystem;
using RealismCommonLib.PatchPipeline;
using StanceOverhaul.SubSystem;
using StanceOverhaul.SubSystem.StanceInput;
using System;
using UnityEngine;
using static StanceOverhaul.Plugin;

namespace StanceOverhaul.Controllers.PatchHooks
{
    internal class InputHookPipeline: ISubSystem
    {
        private IDisposable _inputVeto;
        private IDisposable _inputOverride;
        private IDisposable _inputOverrideHandler;
        
        public void RunOnAwake()
        {
            RegisterWithPipelines();
        }

        public void RunOnDestroy()
        {
            DeRegisterWithPipelines();
        }

        private void RegisterWithPipelines()
        {
            _inputVeto = Pipelines.InputVetoPipeline.Register(ShouldVetoInput);
            _inputOverride = Pipelines.InputOverridePipeline.Register(ShouldOverrideInput);
            _inputOverrideHandler = Pipelines.InputOverrideHandler.Register(HandleOverrideLogic);
        }

        private void DeRegisterWithPipelines()
        {
            DeRegisterWithPipeline(_inputVeto);
            DeRegisterWithPipeline(_inputOverride);
            DeRegisterWithPipeline(_inputOverrideHandler);
        }

        private void DeRegisterWithPipeline(IDisposable pipeline)
        {
            pipeline?.Dispose();
            pipeline = null;
        }

        private bool ShouldVetoInput(InputContext ctx)
        {
            switch (ctx.Command)
            {
                case ECommand.ToggleShooting:
                    return ShouldVetoFiring();
                case ECommand.ScrollNext:
                case ECommand.ScrollPrevious:
                    return ShouldVetoScrollInput();
                default:
                    return false;
            }

        }

        private bool ShouldOverrideInput(InputContext ctx)
        {
            switch (ctx.Command)
            {
                case ECommand.LeftStanceToggle:
                    return true;
                case ECommand.WeaponMounting:
                    return PluginConfig.OverrideMounting.Value;
                default:
                    return false;
            }
        }

        private void HandleOverrideLogic(InputContext ctx)
        {
            switch (ctx.Command)
            {
                case ECommand.LeftStanceToggle:
                    //input system subs to ToggleLeftShoulder event, so no need to call anything here
                    break;
                case ECommand.WeaponMounting:
                    MountingOverride();
                    break;

            }
        }

        private bool ShouldVetoScrollInput()
        {
            return
                Input.GetKey(PluginConfig.StanceWheelComboKeyBind.Value.MainKey) &&
                PluginConfig.UseMouseWheelPlusKey.Value;
        }

        private bool ShouldVetoFiring()
        {
            bool isInStanceThatCanBlockFiring = StanceControllerInstance?.CurrentStance?.BlocksFiring == true;

            bool shouldVeto = PluginConfig.BlockFiring.Value && isInStanceThatCanBlockFiring;

            if (shouldVeto)
            {
                StanceInputEvents.RaiseOnAttemptedToFireFromStance(); 
            }

            return shouldVeto;
        }

        private void MountingOverride()
        {
   /*         Player player = PlayerStateInstance.Player;

            if (StanceControllerInstance.IsBracing && !StanceControllerInstance.IsColliding)
            {
                if (WeaponStateInstance.BipodIsDeployed && StanceControllerInstance.BracingDirection != EBracingDirection.Top) return;

                StanceControllerInstance.IsMounting = !StanceControllerInstance.IsMounting;
                if (StanceControllerInstance.IsMounting) StanceControllerInstance.CancelAllStances();

                Vector3 wiggleDirection = StanceControllerInstance.IsMounting ? StanceControllerInstance.CoverWiggleDirection : StanceControllerInstance.CoverWiggleDirection * -1f;
                StanceControllerInstance.DoWiggleEffects(player, player.ProceduralWeaponAnimation, WeaponStateInstance.WeaponInstance, wiggleDirection, true, 1f, useGearSound: true);
            }
            if (!StanceControllerInstance.IsBracing && StanceControllerInstance.IsMounting) StanceControllerInstance.IsMounting = false;

            if (StanceControllerInstance.IsMounting && WeaponStateInstance.BipodIsDeployed)
            {
                ChangeScopeModeOnMount(player.ProceduralWeaponAnimation, PlayerStateInstance.FirearmController);

                //attempts to enable prone mounted animation
                *//*
                    MountPointData mountData = new MountPointData(StanceController.MountPos, StanceController.MountDir, EMountSideDirection.Forward);
                    Quaternion targetBodyRotation = Quaternion.AngleAxis(player.MovementContext.Yaw, Vector3.up);
                    player.MovementContext.PlayerMountingPointData.SetData(mountData, player.MovementContext.TransformPosition, player.MovementContext.PoseLevel, player.MovementContext.Yaw, PluginConfig.test10.Value, targetBodyRotation, new Vector2(0f, 0f), new Vector2(-3, 6), new Vector2(-10, 10));
                    player.MovementContext.EnterMountedState();
                    player.MovementContext.PlayerAnimator.SetProneBipodMount(true);*/

                /*       AccessTools.Field(typeof(MovementContext), "_inMountedState").SetValue(player.MovementContext, true);
                         player.MovementContext.PlayerAnimator.SetProneBipodMount(true);
                         fc.FirearmsAnimator.SetMounted(true);
                         player.ProceduralWeaponAnimation.SetMountingData(true, true);*//*
            }*/
        }

        //TODO: replace instance paramaters and use state instance
        /*private void ChangeScopeModeOnMount(ProceduralWeaponAnimation pwa, FirearmController fc)
        {
            int aimIndex = pwa.AimIndex;
            if (Mathf.Abs(pwa.ScopeAimTransforms[aimIndex].Rotation) >= EFTHardSettings.Instance.SCOPE_ROTATION_THRESHOLD)
            {
                for (int i = 0; i < pwa.ScopeAimTransforms.Count; i++)
                {
                    if (Mathf.Abs(pwa.ScopeAimTransforms[i].Rotation) < EFTHardSettings.Instance.SCOPE_ROTATION_THRESHOLD)
                    {
                        fc.ChangeAimingMode(i);
                        break;
                    }
                }
            }
        }*/

        public void RunOnUpdate(float deltaTime)
        {
        }
    }
}
