using EFT;
using EFT.Animations;
using EFT.Ballistics;
using EFT.InventoryLogic;
using EFT.Visual;
using HarmonyLib;
using SPT.Reflection.Patching;
using StanceOverhaul.Events;
using StanceOverhaul.Enums;
using System;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using MultiFlare;
using RootMotion.FinalIK;
using static EFT.Player;
using static StanceOverhaul.Plugin;
using static RealismCommonLib.Plugin;
using ReloadClass = EFT.Player.FirearmController.Idling;

namespace StanceOverhaul.Patches
{
    public class HandIKPatch : ModulePatch
    {
        private static FieldInfo _iKArrayField;
        private static FieldInfo _iKPosField;
        private static FieldInfo _iKRotField;

        private static Vector3 _handPosTarget;
        private static Quaternion _handRotTarget;

        protected override MethodBase GetTargetMethod()
        {
            _iKArrayField = AccessTools.Field(typeof(Player), "_limbs");
            _iKPosField = AccessTools.Field(typeof(Player), "_ikPosition");
            _iKRotField = AccessTools.Field(typeof(Player), "_ikRotation");
            return AccessTools.Method(typeof(Player), nameof(Player.IkProcess));
        }

        [PatchPrefix]
        private static void Postfix(Player __instance)
        {

            if (!__instance.IsYourPlayer) return;

            StanceControllerInstance.RunAfterEFTTransformsSet(__instance.ProceduralWeaponAnimation, Time.deltaTime);

            // LimbIK[] limbs = (LimbIK[])_iKArrayField.GetValue(__instance);
            // Vector3 originalIkPosTarget = (Vector3)_iKPosField.GetValue(__instance);
            // Quaternion originalIkRotTarget = (Quaternion)_iKRotField.GetValue(__instance);

            // if (limbs == null)
            //     return;

            // var left = limbs[0];

            // left.solver.IKPosition += StanceControllerInstance.LeftHandTransformMarkerPosition;
            // left.solver.IKRotation *= StanceControllerInstance.LeftHandTransformMarkerRotation;

            //need to replicate method, and add my own grippose blending on top


        }
    }

    public class ElbowIKPatch : ModulePatch
    {
        private static FieldInfo _iKArrayField;
        private static FieldInfo _iKPosField;
        private static FieldInfo _iKRotField;

        private static Vector3 _handPosTarget;
        private static Quaternion _handRotTarget;

        protected override MethodBase GetTargetMethod()
        {
            _iKArrayField = AccessTools.Field(typeof(Player), "_limbs");
            _iKPosField = AccessTools.Field(typeof(Player), "_ikPosition");
            _iKRotField = AccessTools.Field(typeof(Player), "_ikRotation");
            return AccessTools.Method(typeof(Player), nameof(Player.AdjustElbows));
        }

        [PatchPostfix]
        private static void Postfix(Player __instance)
        {
            bool cancelForPistol = WeaponStateInstance.TreatAsPistol && ReloadStateInstance.IsInReloadOpertation;
            bool cancelForRifle = !WeaponStateInstance.TreatAsPistol && ReloadStateInstance.IsInReloadOpertation;

            if (PlayerStateInstance.IsMounting || PlayerStateInstance.IsUsingStationaryWeapon)
                return;

            LimbIK[] limbs = (LimbIK[])_iKArrayField.GetValue(__instance);

            var solver = limbs[0].solver; // IKSolverLimb
            Transform shoulder = solver.bone1.transform;
            Vector3 shoulderPos = shoulder.position;
            Vector3 targetPos = solver.IKPosition;

            Vector3 toTarget = targetPos - shoulderPos;
            if (toTarget.sqrMagnitude < 0.0001f) return;
            toTarget.Normalize();

            // Reference frame for "natural" elbow direction. Chest/spine bone is better
            // than root if the upper body can lean/twist independently of the hips.
            Transform torso = __instance.ProceduralWeaponAnimation.HandsContainer.TrackingTransform; // swap for a spine/chest bone if you have one

            // Elbow points down, outward (left, for the left arm), slightly forward.
            Vector3 elbowDir = (torso.up * -0.8f + (-torso.right) * 1f + torso.forward * -0.25f).normalized; // -0.6f + (-torso.right) * 0.5f + torso.forward * 0.25f

            // Guard against collinearity with the target direction (the degenerate case above)
            if (Mathf.Abs(Vector3.Dot(elbowDir, toTarget)) > 0.1f) // 0.9f
                elbowDir = Vector3.Slerp(elbowDir, torso.forward, -0.1f).normalized; //  0.5f

            solver.bendGoal.position = shoulderPos + elbowDir; // direction matters, magnitude doesn't
        }
    }
    public class PropSoundPatch : ModulePatch
    {
        private static FieldInfo _playerBridgeField;
        protected override MethodBase GetTargetMethod()
        {
            _playerBridgeField = AccessTools.Field(typeof(BaseSoundPlayer), "playersBridge");
            return AccessTools.Method(typeof(BaseSoundPlayer), nameof(BaseSoundPlayer.PlayRandomClip));
        }

        [PatchPrefix]
        private static bool Prefix(BaseSoundPlayer __instance, BaseSoundPlayer.SoundElement soundElement)
        {
            BaseSoundPlayer.IObserverToPlayerBridge player = (BaseSoundPlayer.IObserverToPlayerBridge)_playerBridgeField.GetValue(__instance);
            var clipMathces = soundElement == player.PropIn || soundElement == player.PropOut;
            if (player.iPlayer.IsYourPlayer && StanceControllerInstance.BlockLeftHandSFX && clipMathces)
            {
                return false;
            }

            return true;
        }
    }

    public class UpdateHipInaccuracyPatch : ModulePatch
    {
        private static FieldInfo _playerField;
        private static FieldInfo _tacticalModesField;
        private static FieldInfo _ergoField;

        private static float _deviceBonus = 1f;
        private static bool _laserActive = false;
        private static bool _irLightActive = false;
        private static bool _irLaserActive = false;
        private static bool _whiteLightActive = false;

        private static bool CheckVisibleLaser(List<Transform> tacticalModes)
        {
            foreach (Transform tacticalMode in tacticalModes)
            {
                // Skip disabled modes
                if (!tacticalMode.gameObject.activeInHierarchy) continue;

                // Try to find a "light" under the mode, here's hoping BSG stay consistent
                foreach (Transform child in tacticalMode.GetChildren())
                {
                    //Logger.LogWarning($"Visible laser Checking child: {child.name}");
                    if (child.name.StartsWith("VIS_"))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static bool CheckIRLight(List<Transform> tacticalModes)
        {
            foreach (Transform tacticalMode in tacticalModes)
            {
                // Skip disabled modes
                if (!tacticalMode.gameObject.activeInHierarchy) continue;

                // Try to find a "VolumetricLight", hopefully only visible flashlights have these
                IkLight irLight = tacticalMode.GetComponentInChildren<IkLight>();
                if (irLight != null)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool CheckIRLaser(List<Transform> tacticalModes)
        {
            foreach (Transform tacticalMode in tacticalModes)
            {
                // Skip disabled modes
                if (!tacticalMode.gameObject.activeInHierarchy) continue;

                // Try to find a "light" under the mode, here's hoping BSG stay consistent
                foreach (Transform child in tacticalMode.GetChildren())
                {
                    //Logger.LogWarning($"IR laser Checking child: {child.name}");
                    if (child.name.StartsWith("IR_"))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static bool CheckWhiteLight(List<Transform> tacticalModes)
        {
            foreach (Transform tacticalMode in tacticalModes)
            {
                // Skip disabled modes
                if (!tacticalMode.gameObject.activeInHierarchy) continue;

                // Try to find a "VolumetricLight", hopefully only visible flashlights have these
                if (tacticalMode.GetComponentInChildren<VolumetricLight>() != null) return true;
                if (tacticalMode.GetComponentInChildren<FlareLight>() != null) return true;
                if (tacticalMode.GetComponentInChildren<SpotLightFakeGI>() != null) return true;
            }
            return false;
        }

        private static void DebugLogActiveModesAndComponents(FirearmController firearmController)
        {
            Transform weaponRoot = firearmController.WeaponRoot;
            var controllers = weaponRoot.GetComponentsInChildrenActiveIgnoreFirstLevel<TacticalComboVisualController>();

            foreach (var controller in controllers)
            {
                Logger.LogWarning($"=== Controller: {controller.gameObject.name} ===");
                List<Transform> modes = _tacticalModesField.GetValue(controller) as List<Transform>;
                if (modes == null) continue;

                foreach (Transform mode in modes)
                {
                    bool active = mode.gameObject.activeInHierarchy;
                    Logger.LogWarning($"  Mode: {mode.name}, ActiveInHierarchy: {active}");

                    if (!active) continue;

                    // Log every component on the mode and all children
                    foreach (Transform child in mode.GetComponentsInChildren<Transform>(true))
                    {
                        Component[] comps = child.GetComponents<Component>();
                        foreach (Component comp in comps)
                        {
                            Logger.LogWarning($"    [{child.name}] Component: {comp.GetType().Name}, ActiveSelf: {child.gameObject.activeSelf}");
                        }
                    }
                }
            }
        }

        private static void CheckDevice(FirearmController firearmController)
        {
            //DebugLogActiveModesAndComponents(firearmController);

            _whiteLightActive = false;
            _laserActive = false;
            _irLightActive = false;
            _irLaserActive = false;

            Transform weaponRoot = firearmController.WeaponRoot;

            var controllers = weaponRoot.GetComponentsInChildrenActiveIgnoreFirstLevel<TacticalComboVisualController>();

            foreach (var controller in controllers)
            {
                List<Transform> modes = _tacticalModesField.GetValue(controller) as List<Transform>;
                if (modes == null)
                    continue;

                foreach (Transform mode in modes)
                {
                    if (!mode.gameObject.activeInHierarchy)
                        continue;

                    bool hasVisibleMarker = false;
                    bool hasIRMarker = false;

                    bool hasLaserBeam = false;
                    bool hasSpotlight = false;

                    foreach (Transform child in mode.GetComponentsInChildren<Transform>(true))
                    {
                        string name = child.name.ToUpperInvariant();

                        // Spectrum markers
                        if (name.StartsWith("VIS_"))
                        {
                            hasVisibleMarker = true;
                        }
                        else if (name.StartsWith("IR_") || name.StartsWith("IL_"))
                        {
                            hasIRMarker = true;
                        }

                        // Laser emitter
                        if (!hasLaserBeam && child.GetComponent<LaserBeam>() != null)
                        {
                            hasLaserBeam = true;
                        }

                        // Flashlight / illuminator
                        if (!hasSpotlight)
                        {
                            if (child.GetComponent<SpotLightFakeGI>() != null)
                            {
                                hasSpotlight = true;
                            }
                            else if (child.name.Equals("Spotlight", StringComparison.OrdinalIgnoreCase)
                                     && child.GetComponent<Light>() != null)
                            {
                                hasSpotlight = true;
                            }
                        }
                    }

                    // Visible laser
                    _laserActive |= hasVisibleMarker && hasLaserBeam;

                    // IR laser
                    _irLaserActive |= hasIRMarker && hasLaserBeam;

                    // IR illuminator
                    _irLightActive |= hasIRMarker && hasSpotlight;

                    // White flashlight
                    _whiteLightActive |=
                        hasSpotlight &&
                        !hasVisibleMarker &&
                        !hasIRMarker;
                }
            }
        }

        private static void Reset()
        {
            _deviceBonus = 1f;
            _whiteLightActive = false;
            _laserActive = false;
            _irLightActive = false;
            _irLaserActive = false;

        }

        private static void CalculateDeviceBonus(FirearmController __instance)
        {
            if (__instance.AimingDevices.Length > 0 && __instance.AimingDevices.Any(x => x.Light.IsActive))
            {
                CheckDevice(__instance);

                var nvgIsOn = GearStateInstance.NVGIsActive;
                var thermalIsOn = GearStateInstance.ThermalIsActive;

                if (nvgIsOn)
                {
                    _deviceBonus =
                        _irLaserActive || _laserActive ? PluginConfig.NVGIRLaserBonus.Value :
                        _irLightActive && (_irLaserActive || _laserActive) ? PluginConfig.NVGIRLightWithLaserBonus.Value :
                        _irLightActive ? PluginConfig.NVGIRLightBonus.Value :
                        _whiteLightActive ? PluginConfig.NVGWhiteLightBonus.Value : 1f;
                }
                else if (thermalIsOn)
                {
                    _deviceBonus = PluginConfig.ThermalDeviceBonus.Value;
                }
                else
                {
                    _deviceBonus =
                       _laserActive ? PluginConfig.NormalVisibleLaserBonus.Value :
                       _whiteLightActive && _laserActive ? PluginConfig.NormalWhiteLightWithLaserBonus.Value :
                       _whiteLightActive ? PluginConfig.NormalWhiteLightBonus.Value : 1f;
                }
            }
        }

        protected override MethodBase GetTargetMethod()
        {
            _ergoField = AccessTools.Field(typeof(FirearmController), "_deferredErgonomic");
            _playerField = AccessTools.Field(typeof(FirearmController), "_player");
            _tacticalModesField = AccessTools.Field(typeof(TacticalComboVisualController), "_ligthbeamsTransforms");
            return AccessTools.Method(typeof(FirearmController), nameof(FirearmController.UpdateHipInaccuracy));
        }

        [PatchPrefix]
        private static bool PatchPrefix(FirearmController __instance)
        {
            Player player = (Player)_playerField.GetValue(__instance);
            if (!player.IsYourPlayer || GearStateInstance == null)
                return true;

            Reset();

            CalculateDeviceBonus(__instance);

            //Logger.LogWarning($"NVG active: {GearStateInstance.NVGIsActive}, Thermal active: {GearStateInstance.ThermalIsActive}");

            //Logger.LogWarning($"Device Bonus: {_deviceBonus}, White Light: {_whiteLightActive}, IR Light: {_irLightActive}, IR Laser: {_irLaserActive}, Visible Laser: {_laserActive}");

            Deferred<float> ergo = (Deferred<float>)_ergoField.GetValue(__instance);

            __instance.HipInaccuracy = 1f - Mathf.Clamp01(ergo.Value / 250f - 0.15f);
            player.ProceduralWeaponAnimation.Breath.HipPenalty = __instance.HipInaccuracy;

            __instance.HipInaccuracy *= _deviceBonus * StanceControllerInstance.StanceHipfireBonus;
            player.ProceduralWeaponAnimation.Breath.HipPenalty *= _deviceBonus * StanceControllerInstance.StanceHipfireBonus;

            return false;
        }
    }

    public class UpdateWeaponVariablesPatch : ModulePatch
    {
        private static FieldInfo _playerField;
        private static FieldInfo _fcField;

        protected override MethodBase GetTargetMethod()
        {
            _playerField = AccessTools.Field(typeof(FirearmController), "_player");
            _fcField = AccessTools.Field(typeof(ProceduralWeaponAnimation), "_firearmController");
            return AccessTools.Method(typeof(ProceduralWeaponAnimation), nameof(ProceduralWeaponAnimation.UpdateWeaponVariables));
        }

        [PatchPostfix]
        private static void PatchPostfix(ProceduralWeaponAnimation __instance, float ____aimingSpeed)
        {
            FirearmController firearmController = (FirearmController)_fcField.GetValue(__instance);
            if (firearmController == null) return;
            Player player = (Player)_playerField.GetValue(firearmController);
            if (player == null || !player.IsYourPlayer) return;

            StanceControllerInstance.PwaOriginalAimSpeed = ____aimingSpeed;
        }
    }

    public class SpringUpdatePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(Spring), nameof(Spring.FixedUpdate));
        }

        [PatchPrefix]
        private static void PatchPrefix(Spring __instance, float dt, int nFixedFrames)
        {
            if (StanceControllerInstance == null || !StanceControllerInstance.AwakeRan) return;

            if (__instance == PlayerStateInstance.PWA.HandsContainer.HandsPosition)
            {
                StanceControllerInstance.StancePositionSpring.FixedUpdate(dt, nFixedFrames);
                StanceControllerInstance.OffsetPositionSpring.FixedUpdate(dt, nFixedFrames);
            }


            if (__instance == PlayerStateInstance.PWA.HandsContainer.HandsRotation)
            {
                StanceControllerInstance.StanceRotationSpring.FixedUpdate(dt, nFixedFrames);
                StanceControllerInstance.OffsetRotationSpring.FixedUpdate(dt, nFixedFrames);
            }
        }
    }

    public class SpringResetPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(Spring), nameof(Spring.Reset));
        }

        [PatchPostfix]
        private static void PatchPostfix(Spring __instance)
        {
            if (StanceControllerInstance == null || !StanceControllerInstance.AwakeRan) return;

            if (__instance == PlayerStateInstance.PWA.HandsContainer.HandsPosition)
            {
                StanceControllerInstance.StancePositionSpring.Reset();
                StanceControllerInstance.OffsetPositionSpring.Reset();
            }


            if (__instance == PlayerStateInstance.PWA.HandsContainer.HandsRotation)
            {
                StanceControllerInstance.StanceRotationSpring.Reset();
                StanceControllerInstance.OffsetRotationSpring.Reset();
            }
        }
    }

    public class SpringGetPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(Spring), nameof(Spring.Get));
        }

        [PatchPostfix]
        private static void Postfix(Spring __instance, ref Vector3 __result)
        {
            if (StanceControllerInstance == null || !StanceControllerInstance.AwakeRan) return;

            if (__instance == PlayerStateInstance.PWA.HandsContainer.HandsPosition)
            {
                __result += StanceControllerInstance.StancePositionSpring.Get() + StanceControllerInstance.OffsetPositionSpring.Get(); // StanceControllerInstance.BaseWeaponOffsetPosition
            }

            if (__instance == PlayerStateInstance.PWA.HandsContainer.HandsRotation)
            {
                __result += StanceControllerInstance.StanceRotationSpring.Get() + StanceControllerInstance.OffsetRotationSpring.Get();
            }
        }
    }

    public class SpringGetRelativePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(Spring), nameof(Spring.GetRelative));
        }

        [PatchPostfix]
        private static void Postfix(Spring __instance, ref Vector3 __result)
        {
            if (StanceControllerInstance == null || !StanceControllerInstance.AwakeRan) return;

            if (__instance == PlayerStateInstance.PWA.HandsContainer.HandsPosition)
            {
                __result += StanceControllerInstance.StancePositionSpring.GetRelative() + StanceControllerInstance.OffsetPositionSpring.GetRelative(); //  + StanceControllerInstance.BaseWeaponOffsetPosition unsure if stance or base offset it should be applied here, used for camera positioning 
            }

            if (__instance == PlayerStateInstance.PWA.HandsContainer.HandsRotation)
            {
                __result += StanceControllerInstance.StanceRotationSpring.GetRelative() + StanceControllerInstance.OffsetRotationSpring.GetRelative();
            }
        }
    }

    public class ZeroAdjustmentsPatch : ModulePatch
    {
        private static Vector3 _targetPosition = Vector3.zero;

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(ProceduralWeaponAnimation), nameof(ProceduralWeaponAnimation.ZeroAdjustments));
        }

        [PatchPostfix]
        private static void PatchPostfix(ProceduralWeaponAnimation __instance)
        {
            if (PlayerStateInstance.PWA == __instance)
            {
                StanceControllerInstance.StancePositionSpring.Zero = StanceControllerInstance.StancePosition + StanceControllerInstance.BaseWeaponOffsetPosition; //  not sure if should be applied to stance zero or not
                StanceControllerInstance.StanceRotationSpring.Zero = StanceControllerInstance.StanceRotation;
                StanceControllerInstance.OffsetPositionSpring.Zero = StanceControllerInstance.DetailsOffsetPosition;
                StanceControllerInstance.OffsetRotationSpring.Zero = StanceControllerInstance.DetailsOffsetRotation;
            }
        }
    }

    public class CalculateCameraPositionPatch : ModulePatch
    {
        private static FieldInfo _playerField;
        private static FieldInfo _fcField;
        private static FieldInfo _poseBlendField;
        private static FieldInfo _cameraByFOVOffsetField;

        protected override MethodBase GetTargetMethod()
        {
            _playerField = AccessTools.Field(typeof(FirearmController), "_player");
            _fcField = AccessTools.Field(typeof(ProceduralWeaponAnimation), "_firearmController");
            _poseBlendField = AccessTools.Field(typeof(ProceduralWeaponAnimation), "_animatorPoseBlend");
            _cameraByFOVOffsetField = AccessTools.Field(typeof(ProceduralWeaponAnimation), "_cameraByFOVOffset");
            return AccessTools.Method(typeof(ProceduralWeaponAnimation), nameof(ProceduralWeaponAnimation.CalculateCameraPosition));
        }

        [PatchPostfix]
        private static void PatchPostfix(ProceduralWeaponAnimation __instance)
        {
            FirearmController firearmController = (FirearmController)_fcField.GetValue(__instance);
            if (firearmController == null) return;
            Player player = (Player)_playerField.GetValue(firearmController);
            if (player != null && player.IsYourPlayer && __instance.HandsContainer.WeaponRootAnim)
            {
                // blended with the vanilla (camera to sights) target by the same weight the gun offset uses
                float altAimWeight = StanceControllerInstance.AltAimWeight;
                if (altAimWeight > 0f)
                {
                    Vector3 animatorBlendTarget = Vector3.zero;
                    foreach (ValueTuple<AnimatorPose, float, bool> valueTuple in __instance.ActiveBlends)
                    {
                        float blendValue = valueTuple.Item1.Blend.Evaluate(valueTuple.Item2);
                        animatorBlendTarget += valueTuple.Item1.CameraPosition * blendValue;
                        //__instance.HandsContainer.CameraRotation.Zero += valueTuple.Item1.CameraRotation * blendValue;
                    }

                    var animatorPoseBlend = (float)_poseBlendField.GetValue(__instance);
                    var cameraByFOVOffset = (Vector3)_cameraByFOVOffsetField.GetValue(__instance);
                    var xyOverride = __instance.HandsContainer.CameraOffset + cameraByFOVOffset + __instance.TurnAway.CameraShift;
                    xyOverride = (animatorPoseBlend > 0f) ? (xyOverride + animatorBlendTarget) : xyOverride;
                    Vector3 vanillaTarget = __instance._vCameraTarget;
                    __instance._vCameraTarget = new Vector3(
                        Mathf.Lerp(vanillaTarget.x, xyOverride.x, altAimWeight),
                        Mathf.Lerp(vanillaTarget.y, xyOverride.y, altAimWeight),
                        vanillaTarget.z);

                }

            }
        }
    }

    //TODO: move to common lib to allow other modules to patch this
    public class TacticalReloadPatch : ModulePatch
    {
        private static FieldInfo _playerField;
        private static FieldInfo _fcField;

        protected override MethodBase GetTargetMethod()
        {
            _playerField = AccessTools.Field(typeof(FirearmController), "_player");
            _fcField = AccessTools.Field(typeof(ProceduralWeaponAnimation), "_firearmController");
            return typeof(ProceduralWeaponAnimation).GetMethod("get_TacticalReload", BindingFlags.Instance | BindingFlags.Public);
        }

        [PatchPrefix]
        private static bool PatchPrefix(ProceduralWeaponAnimation __instance, ref bool __result)
        {
            FirearmController firearmController = (FirearmController)_fcField.GetValue(__instance);
            if (firearmController == null) return false;
            Player player = (Player)_playerField.GetValue(firearmController);
            if (player != null && player.IsYourPlayer)
            {
                /*   if (StanceControllerInstance.IsMounting && WeaponStateInstance.BipodIsDeployed && StanceControllerInstance.BracingDirection == EBracingDirection.Top)
                   {
                       __result = true;
                       return false;
                   }*/
            }
            return true;
        }
    }

    //TODO: move to common lib to allow other modules to patch this
    //Needed to block sprint animation when doing bayonet charge
    class SprintPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return typeof(PlayerAnimator).GetMethod("EnableSprint", BindingFlags.Instance | BindingFlags.Public);
        }

        [PatchPrefix]
        private static bool Prefix(PlayerAnimator __instance, bool enabled)
        {
            /*       if (enabled && StanceControllerInstance.CanDoMeleeDetection && WeaponStateInstance.HasBayonet && StanceControllerInstance.IsReadyForBayonetCharge)
                   {
                       return false;
                   }*/
            return true;
        }
    }

    //TODO: move to common lib to allow other modules to patch this
    //Allow reoading while aiming if mounting

    public class DisableAimOnReloadPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return typeof(ReloadClass).GetMethod("DisableAimingOnReload");
        }

        [PatchPrefix]
        private static bool PatchPreFix(ReloadClass __instance)
        {

            /*           if (__instance.Player && StanceControllerInstance.IsMounting)
                       {
                           return false;
                       }*/
            return true;
        }
    }

    //TODO: condier simplification or moving to common lib
    //force scope swtich and block when using canted sight while mounting
    public class ChangeScopePatch : ModulePatch
    {
        private static FieldInfo _playerField;
        private static FieldInfo _weaponManagerClassField;
        protected override MethodBase GetTargetMethod()
        {
            _playerField = AccessTools.Field(typeof(FirearmController), "_player");
            _weaponManagerClassField = AccessTools.Field(typeof(FirearmController), "weaponManagerClass");
            return typeof(Player.FirearmController).GetMethod("ChangeAimingMode", new Type[] { });
        }

        [PatchPrefix]
        private static bool PatchPreFix(FirearmController __instance)
        {
            Player player = (Player)_playerField.GetValue(__instance);
            /*         if (player.IsYourPlayer && PluginConfig.OverrideMounting.Value)
                     {
                         if (WeaponStateInstance.BipodIsDeployed && StanceControllerInstance.IsMounting)
                         {
                             WeaponManagerClass weaponManagerClass = (WeaponManagerClass)_weaponManagerClassField.GetValue(__instance);
                             var scopeIndex = __instance.Item.AimIndex.Value + 1;
                             if (scopeIndex >= weaponManagerClass.ProceduralWeaponAnimation.ScopeAimTransforms.Count)
                             {
                                 scopeIndex = 0;
                             }
                             while (scopeIndex != __instance.Item.AimIndex.Value && Mathf.Abs(weaponManagerClass.ProceduralWeaponAnimation.ScopeAimTransforms[scopeIndex].Rotation) >= EFTHardSettings.Instance.SCOPE_ROTATION_THRESHOLD)
                             {
                                 scopeIndex++;
                                 if (scopeIndex >= weaponManagerClass.ProceduralWeaponAnimation.ScopeAimTransforms.Count)
                                 {
                                     scopeIndex = 0;
                                 }
                             }
                             if (scopeIndex == __instance.Item.AimIndex.Value || Mathf.Abs(weaponManagerClass.ProceduralWeaponAnimation.ScopeAimTransforms[scopeIndex].Rotation) >= EFTHardSettings.Instance.SCOPE_ROTATION_THRESHOLD)
                             {
                                 return false;
                             }
                             __instance.Item.AimIndex.Value = scopeIndex;
                             __instance.UpdateSensitivity();
                             player.RaiseSightChangedEvent(player.ProceduralWeaponAnimation.CurrentAimingMod);
                             return false;
                         }
                     }*/
            return true;
        }
    }

    //TODO: move logic to own class
    //Entry point for moutning and collision overrides 
    public class MountingAndCollisionPatch : ModulePatch
    {
        private static FieldInfo _playerField;
        private static FieldInfo _fcField;
        private static FieldInfo _blendField;
        private static FieldInfo _smoothInField;
        private static FieldInfo _smoothOutField;
        private static float _mountClamp = 0f;
        private static float _collidingModifier = 1f;
        private static float _finalStateEndTimer = 0f;
        private static float _finalStateDelayTimer = 0f;
        private static bool _delayFinalState = false;
        private static float _adsResetTimer = 0f;
        private static float _collisionOverrideTimer = 0f;
        private static float _collisionTimer = 0f;
        private static float _collisionResetTimer = 0f;
        private static float _previousOverlapValue = 0f;
        private static float _currentOverlapValue = 0f;
        private static float _smoothedOverlapValue = 0f;
        private static float _lastDistance = 0f;
        private static bool _isColliding = false;
        private static bool _wasInFinalState = false;
        private static float _stanceFactor = 0f;
        private static float _stanceInverseFactor = 0f;

        private static Vector3 _initialPos = new Vector3(0.01f, -0.075f, -0.13f);
        private static Vector3 _initialRot = new Vector3(-0.025f, 0.005f, 0.005f);
        private static Vector3 _finalPos = Vector3.zero;
        private static Vector3 _finalRot = Vector3.zero;

        private static Vector3 _collisionPos = Vector3.zero;
        private static Vector3 _collisionRot = Vector3.zero;

        protected override MethodBase GetTargetMethod()
        {
            _playerField = AccessTools.Field(typeof(FirearmController), "_player");
            _fcField = AccessTools.Field(typeof(ProceduralWeaponAnimation), "_firearmController");
            _blendField = AccessTools.Field(typeof(TurnAwayEffector), "_blendSpeed");
            _smoothInField = AccessTools.Field(typeof(TurnAwayEffector), "_inSmoothTime");
            _smoothOutField = AccessTools.Field(typeof(TurnAwayEffector), "_outSmoothTime");
            return typeof(ProceduralWeaponAnimation).GetMethod("AvoidObstacles", BindingFlags.Instance | BindingFlags.Public);
        }

        /*        private static void DoMounting(Player player, ProceduralWeaponAnimation pwa)
                {
                    if (StanceControllerInstance.IsMounting)
                    {
                        _mountClamp = Mathf.Lerp(_mountClamp, 2.5f, 0.1f);
                    }
                    else
                    {
                        _mountClamp = Mathf.Lerp(_mountClamp, 0f, 0.1f);
                    }
                    float pivotPoint = WeaponStateInstance.BipodIsDeployed ? 1.5f : 0.75f;
                    float aimPivot = WeaponStateInstance.BipodIsDeployed ? 0.15f : 0.25f;
                    StanceControllerInstance.MountingPivotUpdate(player, pwa, _mountClamp, StanceControllerInstance.GetDeltaTime(), pivotPoint, aimPivot);
                }

                private static void ModifyBSGCollisions(ProceduralWeaponAnimation pwa, FirearmController fc)
                {
                    _currentOverlapValue = fc.OverlapValue;
                    float _smoothingFactor = 0.1f; //0.1f
                    _smoothedOverlapValue = _smoothedOverlapValue + _smoothingFactor * (_currentOverlapValue - _smoothedOverlapValue);
                    //_smoothedOverlapValue = !__instance.OverlappingAllowsBlindfire ? Mathf.Max(_smoothedOverlapValue, _previousOverlapValue) : Mathf.Min(_smoothedOverlapValue, _previousOverlapValue); //this was for when I was trying my own rotaiton + position

                    bool isIncreasing = _smoothedOverlapValue > _previousOverlapValue;
                    bool isDecreasing = _smoothedOverlapValue < _previousOverlapValue;
                    bool isStable = MathUtils.AreFloatsEqual(_smoothedOverlapValue, _previousOverlapValue, 0.0001f);
                    float normalSpeed = 0.1f * Time.deltaTime; //0.1f
                    float delaySpeed = 0.2f * Time.deltaTime; //0.2f
                    float slowDown = 0.15f; //0.05
                    float resetTime = 2.5f; //2
                    float delayTime = 0.15f; //0.1

                    if (isStable)
                    {
                        _collisionTimer = 0;
                        _collisionResetTimer = 0f;
                        _collidingModifier = Mathf.MoveTowards(_collidingModifier, 1f, normalSpeed);

                    }
                    else if (isIncreasing)
                    {
                        _collisionTimer += Time.deltaTime;
                        if (_collisionTimer <= delayTime)
                        {
                            _collidingModifier = Mathf.MoveTowards(_collidingModifier, slowDown, delaySpeed);
                        }
                        else
                        {
                            _collidingModifier = Mathf.MoveTowards(_collidingModifier, 1f, normalSpeed);
                        }

                        _collisionResetTimer = 0f;
                    }
                    else if (isDecreasing)
                    {
                        _collisionTimer = 0;
                        _collisionResetTimer += Time.deltaTime;
                        if (_collisionResetTimer <= resetTime)
                        {
                            _collidingModifier = Mathf.MoveTowards(_collidingModifier, slowDown, delaySpeed);
                        }
                        else
                        {
                            _collidingModifier = Mathf.MoveTowards(_collidingModifier, 1f, normalSpeed);
                        }
                    }
                    _previousOverlapValue = _smoothedOverlapValue;

                    _blendField.SetValue(pwa.TurnAway, 4.5f * _collidingModifier);
                    _smoothInField.SetValue(pwa.TurnAway, 14f * _collidingModifier);
                    _smoothOutField.SetValue(pwa.TurnAway, 8f * _collidingModifier);
                }

                private static void SetStanceSpeedModi(bool isPistol)
                {
                    if (isPistol)
                    {
                        _stanceFactor = 1f;
                        _stanceInverseFactor = 1f;
                        StanceControllerInstance.CameraMovmentForCollisionSpeed = 0.06f;
                    }
                    else if (StanceControllerInstance.TargetStance == EStance.ShortStock || StanceControllerInstance.StoredStance == EStance.ShortStock)
                    {
                        _stanceFactor = 1.15f;
                        _stanceInverseFactor = 0.85f;
                        StanceControllerInstance.CameraMovmentForCollisionSpeed = 0.1f;
                    }
                    else if (StanceControllerInstance.TargetStance == EStance.HighReady || StanceControllerInstance.StoredStance == EStance.HighReady)
                    {
                        _stanceFactor = 1.1f;
                        _stanceInverseFactor = 0.89f;
                        StanceControllerInstance.CameraMovmentForCollisionSpeed = 0.2f;
                    }
                    else if (StanceControllerInstance.TargetStance == EStance.LowReady || StanceControllerInstance.StoredStance == EStance.LowReady)
                    {
                        _stanceFactor = 1.07f;
                        _stanceInverseFactor = 0.92f;
                        StanceControllerInstance.CameraMovmentForCollisionSpeed = 0.16f;
                    }
                    else if (StanceControllerInstance.TargetStance == EStance.ActiveAiming || StanceControllerInstance.StoredStance == EStance.ActiveAiming)
                    {
                        _stanceFactor = 1.03f;
                        _stanceInverseFactor = 0.95f;
                        StanceControllerInstance.CameraMovmentForCollisionSpeed = 0.08f;
                    }
                    else if (StanceControllerInstance.TargetStance == EStance.PatrolStance)
                    {
                        _stanceFactor = 1f;
                        _stanceInverseFactor = 1f;
                        StanceControllerInstance.CameraMovmentForCollisionSpeed = 0.1f;
                    }
                    else
                    {
                        _stanceFactor = 1f;
                        _stanceInverseFactor = 1f;
                        StanceControllerInstance.CameraMovmentForCollisionSpeed = 0.07f;
                    }
                }*/

        /*       private static void AssignFinalTransforms(bool isPistol, float length)
               {
                   if (isPistol)
                   {
                       _finalPos = new Vector3(0.15f, -0.6f, 0.1f);
                       _finalRot = new Vector3(-0.9f, -0.01f, -0.01f);
                   }
                   else if (StanceControllerInstance.TargetStance == EStance.ShortStock || StanceControllerInstance.StoredStance == EStance.ShortStock)
                   {
                       _finalPos = new Vector3(0f, 0f, -0.5f);
                       _finalRot = new Vector3(0.01f, 0.1f, -0.05f);

                   }
                   else if (StanceControllerInstance.TargetStance == EStance.HighReady || StanceControllerInstance.StoredStance == EStance.HighReady)
                   {
                       _finalPos = new Vector3(0.08f, -0.34f, -0.4f);
                       _finalRot = new Vector3(-0.25f, -0.05f, -0.025f);
                   }
                   else if (StanceControllerInstance.TargetStance == EStance.LowReady || StanceControllerInstance.StoredStance == EStance.LowReady)
                   {
                       _finalPos = new Vector3(0f, 0f, -0.15f);
                       _finalRot = new Vector3(0.15f, -0.4f, 0f);
                   }
                   else if (StanceControllerInstance.TargetStance == EStance.ActiveAiming || StanceControllerInstance.StoredStance == EStance.ActiveAiming)
                   {
                       *//*                _finalPos = new Vector3(0.35f, 0.0f, 0.2f);
                                       _finalRot = new Vector3(0f, 0f, -0.9f);*//*
                       _finalPos = new Vector3(0.05f, -0.2f, 0.1f);
                       _finalRot = new Vector3(-0.5f, -0.5f, -0.5f);
                   }
                   else if (StanceControllerInstance.TargetStance == EStance.PatrolStance)
                   {
                       _finalPos = Vector3.zero;
                       _finalRot = Vector3.zero;
                   }
                   else
                   {
                       _finalPos = new Vector3(0f, 0.05f, -0.15f);
                       _finalRot = new Vector3(0.2f, -0.1f, -0.1f);
                   }
               }*/

        /*        private static void CollisionOverride(ProceduralWeaponAnimation pwa, FirearmController fc)
                {
                    _blendField.SetValue(pwa.TurnAway, 0f);
                    _smoothInField.SetValue(pwa.TurnAway, 0f);
                    _smoothOutField.SetValue(pwa.TurnAway, 0f);

                    Vector3 rayStart = pwa.HandsContainer.WeaponRoot.position;
                    Vector3 forward = -pwa.HandsContainer.WeaponRoot.transform.up;
                    bool treatAsPistol = WeaponStateInstance.TreatAsPistol;
                    float weaponLn = StanceControllerInstance.StanceModifiedWeaponLength;
                    float weaponLengthFactor = weaponLn * (treatAsPistol ? 1.05f : 1.25f); //stance should be a factor here too

                    _isColliding = false;
                    RaycastHit raycastHit;
                    if (!StanceControllerInstance.IsMounting && EFTPhysicsClass.Raycast(new Ray(rayStart, forward), out raycastHit, weaponLengthFactor, LayerMaskClass.HighPolyWithTerrainMask))
                    {
                        _lastDistance = raycastHit.distance;
                        _isColliding = true;
                    }

                    SetStanceSpeedModi(treatAsPistol);
                    bool isStocklessRifle = !WeaponStateInstance.IsPistol && !WeaponStateInstance.HasShoulderContact;
                    float stanceBonus = (isStocklessRifle ? 1.15f : 1f) * _stanceFactor;
                    float stanceInverseBonus = (isStocklessRifle ? 0.85f : 1f) * _stanceInverseFactor;
                    float collisionTimerSpeed = 0.015f * WeaponStateInstance.ErgoFactor * stanceInverseBonus;
                    float adsTimerSpeed = 0.03f * WeaponStateInstance.ErgoFactor * _stanceInverseFactor;
                    float finalStateTimerSpeed = 0.5f * _stanceFactor;

                    if (_isColliding)
                    {
                        _collisionOverrideTimer = 0;
                        StanceControllerInstance.IsColliding = true;
                    }
                    else
                    {
                        _collisionOverrideTimer += Time.deltaTime;
                        if (_collisionOverrideTimer >= collisionTimerSpeed)
                        {
                            StanceControllerInstance.IsColliding = false;
                        }
                        else
                        {
                            StanceControllerInstance.IsColliding = true;
                        }
                    }

                    StanceControllerInstance.StopCameraMovement = false;
                    if (_isColliding) //&& _lastDistance <= 1.5f
                    {
                        _adsResetTimer = 0;
                        StanceControllerInstance.StopCameraMovement = true;
                    }
                    else
                    {
                        _adsResetTimer += Time.deltaTime;
                        if (_adsResetTimer >= adsTimerSpeed) //this delay needs to factor in the weapon's ADS speed. 0.25 feels good for SKS with supp, 0.5 at least for full length mosin
                        {
                            StanceControllerInstance.StopCameraMovement = false;
                        }
                        else StanceControllerInstance.StopCameraMovement = true;
                    }

                    //weapon length ranges around 0.5-1.4, need to modify collision reaction based on the length of the weapon, particularly the threshold.
                    //shorter guns need to react less, maybe have a different Pow value for inverseDistance derived from length
                    //length = 1.4, threshold = 0.65
                    //try using relative distance instead ? distance / length of raycast

                    AssignFinalTransforms(treatAsPistol, weaponLn);
                    float speed = 0.15f * WeaponStateInstance.TotalErgo * stanceBonus;
                    float baseThrehold = treatAsPistol ? 0.45f : 0.5f;
                    float threshold = baseThrehold * Mathf.Pow(weaponLn, 1.15f);
                    bool doInitialState = _lastDistance >= threshold || _delayFinalState;

                    float distanceFactor = Mathf.InverseLerp(weaponLn, 0, _lastDistance);
                    float smoothedInverseDistance = Mathf.Pow(distanceFactor, 0.45f);

                    float intitalPosY = (weaponLn * -0.4f) * (1 - (_lastDistance / (weaponLengthFactor)));
                    float intitalPosZ = (weaponLn * -0.65f) * (1 - (_lastDistance / (weaponLengthFactor))); // treatAsPistol ? -0.145f : treatAsPistol && pwa.IsAiming ? -0.16f : -0.13f;
                    Vector3 initialPos = new Vector3(0.025f, 0f, 0f) * smoothedInverseDistance;
                    initialPos.y = intitalPosY;
                    initialPos.z = intitalPosZ;
                    Vector3 lastPos = _finalPos * smoothedInverseDistance;

                    Vector3 initialRot = _initialRot * smoothedInverseDistance;
                    Vector3 lastRot = _finalRot * smoothedInverseDistance; //
                    Vector3 targetRot = !StanceControllerInstance.IsColliding ? Vector3.zero : doInitialState ? initialRot : lastRot;

                    bool isInFinalState;
                    Vector3 targetPos = Vector3.zero;

                    if (!StanceControllerInstance.IsColliding) isInFinalState = false;
                    else if (doInitialState) isInFinalState = false;
                    else
                    {
                        isInFinalState = true;
                        _wasInFinalState = true;
                    }

                    bool reset = !StanceControllerInstance.IsColliding && !_wasInFinalState;
                    bool initial = doInitialState && !_wasInFinalState;
                    targetPos = reset ? Vector3.zero : initial ? initialPos : lastPos;
                    targetRot = reset ? Vector3.zero : initial ? initialRot : lastRot;

                    _collisionPos = Vector3.Lerp(_collisionPos, targetPos, speed * Time.deltaTime);
                    pwa.HandsContainer.WeaponRoot.localPosition += _collisionPos;

                    _collisionRot = Vector3.Lerp(_collisionRot, targetRot, speed * Time.deltaTime);
                    Quaternion newRot = Quaternion.identity;
                    newRot.x = _collisionRot.x;
                    newRot.y = _collisionRot.y;
                    newRot.z = _collisionRot.z;

                    pwa.HandsContainer.WeaponRoot.localRotation *= newRot;

                    if (_isColliding)
                    {
                        _finalStateDelayTimer += Time.deltaTime;
                        if (_finalStateDelayTimer >= 0.05f)
                        {
                            _delayFinalState = false;
                        }
                        else _delayFinalState = true;
                    }
                    else
                    {
                        _finalStateDelayTimer = 0f;
                        _delayFinalState = true;
                    }

                    if (_wasInFinalState && !isInFinalState)
                    {
                        _finalStateEndTimer += Time.deltaTime;
                        if (_finalStateEndTimer >= finalStateTimerSpeed)
                        {
                            _wasInFinalState = false;
                        }
                    }
                    else
                    {
                        _finalStateEndTimer = 0f;
                    }
                }
        */
        [PatchPostfix]
        private static void PatchPostfix(ProceduralWeaponAnimation __instance)
        {
            FirearmController firearmController = (FirearmController)_fcField.GetValue(__instance);
            if (firearmController == null) return;
            Player player = (Player)_playerField.GetValue(firearmController);
            if (player != null && player.IsYourPlayer && player.MovementContext.CurrentState.Name != EPlayerState.Stationary)
            {
                /*          if (PluginConfig.OverrideCollision.Value && FOVFixEnabled) CollisionOverride(__instance, firearmController);
                          else if (PluginConfig.OverrideCollision.Value) ModifyBSGCollisions(__instance, firearmController);
                          DoMounting(player, __instance);*/
            }
        }
    }

    //Block firemode change animation when in high ready and sprinting
    //This still allows mode to change, and fire selector animtates. This should be blocked too.
    public class SetFireModePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return typeof(FirearmsAnimator).GetMethod("SetFireMode", BindingFlags.Instance | BindingFlags.Public);
        }

        [PatchPrefix]
        private static bool Prefix(FirearmsAnimator __instance, Weapon.EFireMode fireMode, bool skipAnimation = false)
        {
            __instance.ResetLeftHand();
            skipAnimation = StanceControllerInstance.IsDoingTacSprint ? true : skipAnimation;
            AnimationControllerParametersTable.SetFireMode(__instance.Animator, (float)fireMode);
            if (!skipAnimation)
            {
                AnimationControllerParametersTable.TriggerFiremodeSwitch(__instance.Animator);
            }
            return false;
        }
    }

    //Entry point to disable stances when starting operating stationary weapon
    //This could be moved to common lib, and check ran in update for using stationary wepaon
    //TODO: replace with event that stancecontroller listens to, or inputhandler
    public class OperateStationaryWeaponPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return typeof(Player).GetMethod("OperateStationaryWeapon", BindingFlags.Instance | BindingFlags.Public);
        }

        [PatchPostfix]
        private static void PatchPostfix(Player __instance)
        {
            if (__instance.IsYourPlayer)
            {
                StanceEvents.RaiseOnStationaryWeaponOperated();
            }
        }
    }

    //Used as entry point for melee hit and mounting
    //Should move logic to own classes
    //Could move some of this to gneric player update in monobehaviour
    public class CollisionPatch : ModulePatch
    {
        private static FieldInfo _playerField;
        private static FieldInfo _hitIgnoreField;

        private static int _timer = 0;
        private static MaterialType[] _allowedMats = { MaterialType.Helmet, MaterialType.BodyArmor, MaterialType.Body, MaterialType.Glass, MaterialType.GlassShattered, MaterialType.GlassVisor };

        private static Vector3 _startLeftDir = new Vector3(0.143f, 0f, 0f);
        private static Vector3 _startRightDir = new Vector3(-0.143f, 0f, 0f);
        private static Vector3 _startDownDir = new Vector3(0f, 0f, -0.19f);

        private static Vector3 _wiggleLeftDir = new Vector3(2.5f, 7.5f, -5) * 0.5f;
        private static Vector3 _wiggleRightDir = new Vector3(2.5f, -7.5f, -5f) * 0.5f;
        private static Vector3 _wiggleDownDir = new Vector3(7.5f, 2.5f, -5f) * 0.5f;

        private static int PlayerMask = LayerMask.NameToLayer("Player");

        protected override MethodBase GetTargetMethod()
        {
            _playerField = AccessTools.Field(typeof(EFT.Player.FirearmController), "_player");
            _hitIgnoreField = AccessTools.Field(typeof(EFT.Player.FirearmController), "_isOverlapHitIgnoredTest");

            return typeof(Player.FirearmController).GetMethod("OverlapLn", BindingFlags.Instance | BindingFlags.Public);
        }

        /*       private static void SetMountingStatus(EBracingDirection coverDir)
               {
                   if (!StanceControllerInstance.IsMounting)
                   {
                       StanceControllerInstance.BracingDirection = coverDir;
                   }
                   StanceControllerInstance.IsBracing = true;
               }

               private static Vector3 GetWiggleDir(EBracingDirection coverDir)
               {
                   {
                       switch (coverDir)
                       {
                           case EBracingDirection.Right:
                               return _wiggleRightDir;
                           case EBracingDirection.Left:
                               return _wiggleLeftDir;
                           case EBracingDirection.Top:
                               return _wiggleDownDir;
                           default: return Vector3.zero;
                       }
                   }
               }

               private static bool IsBracingProne(Player player)
               {
                   if (player.IsInPronePose)
                   {
                       SetMountingStatus(EBracingDirection.Top);
                       return true;
                   }
                   return false;
               }*/

        /*     private static bool CheckForCoverCollision(EBracingDirection coverDir, Vector3 start, Vector3 direction, Vector3 spherePos, float radius)
             {
                 RaycastHit raycastHit;
                 if (Physics.Linecast(start, direction, out raycastHit, EFTHardSettings.Instance.WEAPON_OCCLUSION_LAYERS))
                 {
                     if (raycastHit.collider.gameObject.layer != PlayerMask)
                     {
                         SetMountingStatus(coverDir);
                         StanceControllerInstance.CoverWiggleDirection = GetWiggleDir(coverDir);
                         return true;
                     }

                 }

                 Collider[] hitColliders = Physics.OverlapSphere(spherePos, radius, EFTHardSettings.Instance.WEAPON_OCCLUSION_LAYERS);
                 foreach (var hitCollider in hitColliders)
                 {
                     if (hitCollider.gameObject.layer != PlayerMask)
                     {
                         SetMountingStatus(coverDir);
                         StanceControllerInstance.CoverWiggleDirection = GetWiggleDir(coverDir);
                         return true;
                     }
                 }

                 return false;
             }*/

        // TODO: move to a difference class and possible integrate with  BSG's mounting system
        /*        private static void DetectBracing(FirearmController fc, Player player, float ln)
                {
                    _timer += 1;
                    if (_timer >= 60)
                    {
                        _timer = 0;
                        Transform weapTransform = player.ProceduralWeaponAnimation.HandsContainer.WeaponRootAnim;
                        Vector3 linecastDirection = weapTransform.TransformDirection(Vector3.up);

                        Vector3 downDir = WeaponStateInstance.BipodIsDeployed ? new Vector3(_startDownDir.x, _startDownDir.y, _startDownDir.z + -0.21f) : _startDownDir;

                        Vector3 startDown = weapTransform.position + weapTransform.TransformDirection(downDir);
                        Vector3 startLeft = weapTransform.position + weapTransform.TransformDirection(_startLeftDir);
                        Vector3 startRight = weapTransform.position + weapTransform.TransformDirection(_startRightDir);

                        Vector3 sphereDown = weapTransform.position + weapTransform.TransformDirection(new Vector3(0f, -0.45f, -0.1f));
                        Vector3 sphereLeft = weapTransform.position + weapTransform.TransformDirection(new Vector3(0.05f, -0.5f, -0.065f));
                        Vector3 sphereRight = weapTransform.position + weapTransform.TransformDirection(new Vector3(-0.05f, -0.5f, -0.065f));

                        Vector3 forwardDirection = startDown - linecastDirection * ln;
                        Vector3 leftDirection = startLeft - linecastDirection * ln;
                        Vector3 rightDirection = startRight - linecastDirection * ln;

                        if (PluginConfig.OverrideMounting.Value)
                        {
                            if (IsBracingProne(player) ||
                            CheckForCoverCollision(EBracingDirection.Top, startDown, forwardDirection, sphereDown, 0.045f) ||
                            CheckForCoverCollision(EBracingDirection.Left, startLeft, leftDirection, sphereLeft, 0.09f) ||
                            CheckForCoverCollision(EBracingDirection.Right, startRight, rightDirection, sphereRight, 0.09f))
                            {
                                return;
                            }
                        }
                        StanceControllerInstance.IsBracing = false;
                    }

                    if (StanceControllerInstance.IsBracing || StanceControllerInstance.IsMounting)
                    {
                        float mountOrientationBonus = StanceControllerInstance.BracingDirection == EBracingDirection.Top ? 0.75f : 1f;
                        float mountingRecoilLimit = StanceControllerInstance.TreatWeaponAsPistolStance ? 0.25f : 0.75f;
                        float recoilBonus =
                            StanceControllerInstance.IsMounting && fc.Weapon.IsBeltMachineGun && WeaponStateInstance.BipodIsDeployed ? 0.4f :
                            StanceControllerInstance.IsMounting && fc.Weapon.IsBeltMachineGun ? 0.75f :
                            StanceControllerInstance.IsMounting && WeaponStateInstance.BipodIsDeployed ? 0.45f :
                            StanceControllerInstance.IsMounting ? 0.85f :
                            0.95f;
                        float swayBonus = StanceControllerInstance.IsMounting && WeaponStateInstance.BipodIsDeployed ? 0.05f : StanceControllerInstance.IsMounting ? 0.35f : 0.65f;
                        StanceControllerInstance.BracingRecoilBonus = Mathf.Lerp(StanceControllerInstance.BracingRecoilBonus, recoilBonus * mountOrientationBonus, 0.04f);
                        StanceControllerInstance.BracingSwayBonus = Mathf.Lerp(StanceControllerInstance.BracingSwayBonus, swayBonus * mountOrientationBonus, 0.04f);
                    }
                    else
                    {
                        StanceControllerInstance.BracingSwayBonus = Mathf.Lerp(StanceControllerInstance.BracingSwayBonus, 1f, 0.05f);
                        StanceControllerInstance.BracingRecoilBonus = Mathf.Lerp(StanceControllerInstance.BracingRecoilBonus, 1f, 0.05f);
                    }
                }
        */
        //move to a mellee stance class as much as possible
        /*        private static void DoMelee(FirearmController fc, Player player, float ln)
                {
                    if (StanceControllerInstance.TargetStance == EStance.Melee && StanceControllerInstance.CanDoMeleeDetection && !StanceControllerInstance.MeleeHitSomething)
                    {
                        Transform weapTransform = player.ProceduralWeaponAnimation.HandsContainer.WeaponRootAnim;
                        Vector3 linecastDirection = weapTransform.TransformDirection(Vector3.up);
                        Vector3 startMeleeDir = new Vector3(0, -0.5f, -0.025f);
                        Vector3 meleeStart = weapTransform.position + weapTransform.TransformDirection(startMeleeDir);
                        Vector3 meleeDir = meleeStart - linecastDirection * (ln - (WeaponStateInstance.HasBayonet ? 0.1f : 0.25f));

                        BallisticCollider hitBalls = null;
                        RaycastHit raycastHit;
                        if (Physics.Linecast(meleeStart, meleeDir, out raycastHit, CollisionLayerClass.HitMask))
                        {
                            Collider col = raycastHit.collider;
                            BaseBallistic baseballComp = col.GetComponent<BaseBallistic>();
                            if (baseballComp != null)
                            {
                                hitBalls = baseballComp.Get(raycastHit.point);
                            }
                            float weaponWeight = fc.Weapon.TotalWeight;
                            float damage = 19f + WeaponStateInstance.BaseMeleeDamage * (1f + player.Skills.StrengthBuffMeleePowerInc) * (1f + (weaponWeight / 10f));
                            damage *= player.Physical.HandsStamina.Exhausted ? Singleton<BackendConfigSettingsClass>.Instance.Stamina.ExhaustedMeleeDamageMultiplier : 1f;
                            damage *= player.Speed;
                            float pen = 15f + WeaponStateInstance.BaseMeleePen * (1f + (weaponWeight / 10f));
                            bool shouldSkipHit = false;

                            if (hitBalls as BodyPartCollider != null)
                            {
                                player.ExecuteSkill(new Action(() => player.Skills.FistfightAction.Complete(1f)));
                            }

                            if (hitBalls.TypeOfMaterial == MaterialType.Glass || hitBalls.TypeOfMaterial == MaterialType.GlassShattered)
                            {
                                int rndNum = UnityEngine.Random.Range(1, 11);
                                if (rndNum > (4f + WeaponStateInstance.BaseMeleeDamage))
                                {
                                    shouldSkipHit = true;
                                }
                            }

                            bool isAllowableHit = _allowedMats.Contains(hitBalls.TypeOfMaterial) && !shouldSkipHit;
                            if ((WeaponStateInstance.HasBayonet || isAllowableHit) && raycastHit.collider.gameObject.layer != PlayerMask)
                            {
                                Vector3 position = fc.CurrentFireport.position;
                                Vector3 vector = fc.WeaponDirection;
                                Vector3 shotPosition = position;
                                fc.AdjustShotVectors(ref shotPosition, ref vector);
                                Vector3 shotDirection = vector;
                                DamageInfoStruct damageInfo = new DamageInfoStruct
                                {
                                    SourceId = fc.Weapon.Id,
                                    DamageType = EDamageType.Melee,
                                    Damage = damage,
                                    PenetrationPower = pen,
                                    ArmorDamage = 10f + (damage / 10f),
                                    Direction = shotDirection.normalized,
                                    HitCollider = col,
                                    HitPoint = raycastHit.point,
                                    Player = Singleton<GameWorld>.Instance.GetAlivePlayerBridgeByProfileID(player.ProfileId),
                                    HittedBallisticCollider = hitBalls,
                                    HitNormal = raycastHit.normal,
                                    Weapon = fc.Item as Item,
                                    IsForwardHit = true,
                                    StaminaBurnRate = 5f
                                };
                                ShotInfoClass result = Singleton<GameWorld>.Instance.HackShot(damageInfo);
                            }
                            float vol = WeaponStateInstance.HasBayonet ? 10f : 12f;
                            Singleton<BetterAudio>.Instance.PlayDropItem(baseballComp.SurfaceSound, JsonType.EItemDropSoundType.Rifle, raycastHit.point, vol);
                            *//*                  StanceControllerInstance.DoWiggleEffects(player, player.ProceduralWeaponAnimation, fc, new Vector3(-10f, 10f, 0f), true, 1.5f);
                            *//*
                            player.Physical.ConsumeAsMelee(0.2f * (1f + (weaponWeight * 0.1f)));

                            StanceControllerInstance.CanDoMeleeDetection = false;
                            StanceControllerInstance.MeleeHitSomething = true;
                            return;
                        }
                    }
                }*/


        [PatchPrefix]
        private static void PatchPrefix(Player.FirearmController __instance, Vector3 origin, float ln, Vector3? weaponUp = null)
        {
            Player player = (Player)_playerField.GetValue(__instance);
            if (player.IsYourPlayer)
            {
                //DoMelee(__instance, player, ln);
                //DetectBracing(__instance, player, ln);
            }
        }
    }

    //TODO: If using BSG's mounting, skip check
    //Override to prevent BSG collision check when mounting or doing own collision detectio
    public class WeaponOverlapViewPatch : ModulePatch
    {
        private static FieldInfo _playerField;

        protected override MethodBase GetTargetMethod()
        {
            _playerField = AccessTools.Field(typeof(EFT.Player.FirearmController), "_player");

            return typeof(Player.FirearmController).GetMethod("WeaponOverlapView", BindingFlags.Instance | BindingFlags.Public);
        }

        [PatchPrefix]
        private static bool PatchPrefix(Player.FirearmController __instance)
        {
            Player player = (Player)_playerField.GetValue(__instance);
            /*            if (player.IsYourPlayer && (StanceControllerInstance.CurrentStance == EStance.Mounting || StanceControllerInstance.IsColliding))
                        {
                            return false;
                        }*/

            /*     too jank to use TurnAway, but I could possible try to extract how they rotate the arms
                   if (StanceControllerInstance.CurrentStanceType == EStance.PatrolStance) 
                   {
                       Vector3 vector = player.ProceduralWeaponAnimation.HandsContainer.HandsPosition.Get();
                       player.ProceduralWeaponAnimation.OverlappingAllowsBlindfire = false;
                       player.ProceduralWeaponAnimation.TurnAway.OverlapsWithPlayer = true;
                       player.ProceduralWeaponAnimation.TurnAway.OriginZShift = vector.y;
                       player.ProceduralWeaponAnimation.TurnAway.OverlapDepth = PluginConfig.test18.Value;

                       _blendField.SetValue(player.ProceduralWeaponAnimation.TurnAway, PluginConfig.test15.Value);
                       _smoothInField.SetValue(player.ProceduralWeaponAnimation.TurnAway, PluginConfig.test16.Value);
                       _smoothOutField.SetValue(player.ProceduralWeaponAnimation.TurnAway, PluginConfig.test17.Value);

                       return false;
                   }*/



            return true;
        }
    }

    //Entry point to get original and new weapon length, should move to common lib
    public class WeaponLengthPatch : ModulePatch
    {
        private static FieldInfo playerField;
        private static FieldInfo weapLn;

        protected override MethodBase GetTargetMethod()
        {
            playerField = AccessTools.Field(typeof(EFT.Player.FirearmController), "_player");
            weapLn = AccessTools.Field(typeof(EFT.Player.FirearmController), "WeaponLn");
            return typeof(Player.FirearmController).GetMethod("method_10", BindingFlags.Instance | BindingFlags.Public);
        }

        [PatchPostfix]
        private static void PatchPostfix(Player.FirearmController __instance)
        {
            Player player = (Player)playerField.GetValue(__instance);
            float length = (float)weapLn.GetValue(__instance);
            if (player.IsYourPlayer)
            {
                StanceControllerInstance.BaseWeaponLength = length;
                //StanceControllerInstance.StanceModifiedWeaponLength = length < 0.92f ? length * 0.95f : length * 1.05f; //length >= 0.92f ? length * 1.12f : length
            }
        }
    }

    // Modify weapon length used for collision based on stance
    // Do NOT do this, it has unintended consequences, weapon length determines origin of bullet. 
    // Need to modify how collision code uses it factored by stance
    public class WeaponOverlappingPatch : ModulePatch
    {
        private static FieldInfo playerField;
        private static FieldInfo weaponLnField;

        protected override MethodBase GetTargetMethod()
        {
            playerField = AccessTools.Field(typeof(EFT.Player.FirearmController), "_player");
            weaponLnField = AccessTools.Field(typeof(EFT.Player.FirearmController), "WeaponLn");
            return typeof(Player.FirearmController).GetMethod("WeaponOverlapping", BindingFlags.Instance | BindingFlags.Public);
        }

        [PatchPrefix]
        private static void Prefix(Player.FirearmController __instance)
        {
            Player player = (Player)playerField.GetValue(__instance);
            if (player.IsYourPlayer)
            {
                // if (StanceControllerInstance.CurrentStanceType == EStanceType.PatrolStance)
                // {
                //     weaponLnField.SetValue(__instance, StanceControllerInstance.StanceModifiedWeaponLength * 0.75f);
                //     return;
                // }

                // if (WeaponStateInstance.TreatAsPistol)
                // {
                //     weaponLnField.SetValue(__instance, StanceControllerInstance.StanceModifiedWeaponLength * 0.85f);
                // }
                // else
                // {
                //     if (FikaIsPresent) //collisions acts funky with stances from another client's perspective
                //     {
                //         weaponLnField.SetValue(__instance, StanceControllerInstance.StanceModifiedWeaponLength * 0.8f);
                //         return;
                //     }
                //     if (StanceControllerInstance.CurrentStanceType == EStanceType.ShortStock)
                //     {
                //         weaponLnField.SetValue(__instance, StanceControllerInstance.StanceModifiedWeaponLength * 0.9f);
                //         return;
                //     }
                //     if (StanceControllerInstance.CurrentStanceType == EStanceType.HighReady)
                //     {
                //         weaponLnField.SetValue(__instance, StanceControllerInstance.StanceModifiedWeaponLength * 0.95f);
                //         return;
                //     }
                //     if (StanceControllerInstance.CurrentStanceType == EStanceType.LowReady)
                //     {
                //         weaponLnField.SetValue(__instance, StanceControllerInstance.StanceModifiedWeaponLength * 0.98f);
                //         return;
                //     }
                // }
                // weaponLnField.SetValue(__instance, StanceControllerInstance.StanceModifiedWeaponLength);
            }
        }
    }


    //Prevent BSG from moving weapon closer based on having a stock, because it looks shit
    //TODO: should reproduce this behaviour myself, different weapon hold and position if not stock
    public class ShouldMoveWeapCloserPatch : ModulePatch
    {
        private static FieldInfo _playerField;
        private static FieldInfo _fcField;

        protected override MethodBase GetTargetMethod()
        {
            _playerField = AccessTools.Field(typeof(EFT.Player.FirearmController), "_player");
            _fcField = AccessTools.Field(typeof(ProceduralWeaponAnimation), "_firearmController");
            return typeof(EFT.Animations.ProceduralWeaponAnimation).GetMethod("CheckShouldMoveWeaponCloser", BindingFlags.Instance | BindingFlags.Public);
        }

        [PatchPostfix]
        private static void PatchPostfix(ProceduralWeaponAnimation __instance, ref bool ____shouldMoveWeaponCloser)
        {
            FirearmController firearmController = (FirearmController)_fcField.GetValue(__instance);
            if (firearmController == null) return;
            Player player = (Player)_playerField.GetValue(firearmController);
            if (player != null && player.MovementContext.CurrentState.Name != EPlayerState.Stationary && player.IsYourPlayer)
            {
                ____shouldMoveWeaponCloser = false;
            }
        }
    }

    //TODO: possibly move to common lib to allow multiple modules to modify it
    //Get initial weapon position when initializing PWA
    public class InitTransformsPatch : ModulePatch
    {
        private static FieldInfo _playerField;
        private static FieldInfo _fcField;

        protected override MethodBase GetTargetMethod()
        {
            _playerField = AccessTools.Field(typeof(EFT.Player.FirearmController), "_player");
            _fcField = AccessTools.Field(typeof(ProceduralWeaponAnimation), "_firearmController");
            return typeof(EFT.Animations.ProceduralWeaponAnimation).GetMethod("InitTransforms", BindingFlags.Instance | BindingFlags.Public);
        }

        [PatchPostfix]
        private static void PatchPostfix(EFT.Animations.ProceduralWeaponAnimation __instance)
        {
            FirearmController firearmController = (FirearmController)_fcField.GetValue(__instance);
            if (firearmController == null) return;
            Player player = (Player)_playerField.GetValue(firearmController);
            if (player != null && player.MovementContext.CurrentState.Name != EPlayerState.Stationary && player.IsYourPlayer)
            {
                StanceEvents.RaiseOnTransformsInit(firearmController);
                //if (!Plugin.FOVFixPresent) __instance.HandsContainer.CameraOffset = new Vector3(0.04f, 0.04f, 0.025f);
            }
        }
    }

    public class VisualPassPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return typeof(Player).GetMethod("VisualPass", BindingFlags.Instance | BindingFlags.Public);
        }

        [PatchPostfix]
        private static void PatchPostfix(Player __instance)
        {
            if (__instance.IsYourPlayer && __instance.MovementContext.CurrentState.Name != EPlayerState.Stationary)
            {
                //StanceControllerInstance.RunAfterEFTTransformsSet(__instance.ProceduralWeaponAnimation, Time.deltaTime);
            }
        }
    }

    public class ApplyTransformsPatch : ModulePatch
    {
        private static FieldInfo _playerField;
        private static FieldInfo _fcField;

        protected override MethodBase GetTargetMethod()
        {
            _playerField = AccessTools.Field(typeof(EFT.Player.FirearmController), "_player");
            _fcField = AccessTools.Field(typeof(ProceduralWeaponAnimation), "_firearmController");
            return typeof(FirstPersonStrategy).GetMethod("ApplyTransformations", BindingFlags.Instance | BindingFlags.Public);
        }

        [PatchPostfix]
        private static void PatchPostfix(FirstPersonStrategy __instance, ProceduralWeaponAnimation pwa, float dt)
        {
            FirearmController firearmController = (FirearmController)_fcField.GetValue(pwa);
            if (firearmController == null) return;
            Player player = (Player)_playerField.GetValue(firearmController);
            if (player != null && player.MovementContext.CurrentState.Name != EPlayerState.Stationary && player.IsYourPlayer)
            {
                //StanceControllerInstance.RunAfterEFTTransformsSet(pwa, dt);
            }
        }
    }

    public class LerpCameraPatch : ModulePatch
    {
        private static FieldInfo _playerField;
        private static FieldInfo _fcField;

        protected override MethodBase GetTargetMethod()
        {
            _playerField = AccessTools.Field(typeof(FirearmController), "_player");
            _fcField = AccessTools.Field(typeof(ProceduralWeaponAnimation), "_firearmController");
            return typeof(EFT.Animations.ProceduralWeaponAnimation).GetMethod("LerpCamera", BindingFlags.Instance | BindingFlags.Public);
        }

        [PatchPostfix]
        private static void Postfix(EFT.Animations.ProceduralWeaponAnimation __instance, float dt, float ____overweightAimingMultiplier,
            float ____aimingSpeed, float ____aimSwayStrength, Player.ValueBlender
            ____aimSwayBlender, Vector3 ____aimSwayDirection, Vector3 ____headRotationVec,
            Vector3 ____vCameraTarget, Player.ValueBlenderDelay ____tacticalReload,
            Quaternion ____cameraIdenity, Quaternion ____rotationOffset, Vector2 ____cameraShiftToLineOfSight,
            float ____lineOfSightDeltaAngle, Vector3 ____shotDirection, bool ____adjustCollimatorsToTrajectory,
            Transform ____bone0, Transform ____bone1)
        {
            FirearmController firearmController = (FirearmController)_fcField.GetValue(__instance);
            if (firearmController == null) return;
            Player player = (Player)_playerField.GetValue(firearmController);
            if (player != null && player.IsYourPlayer && firearmController.Weapon != null)
            {

            }
        }
    }

    //TODO: move to common lib
    //entry point for pose changes
    //move to common lib + use event
    public class ChangePosePatch : ModulePatch
    {
        private static FieldInfo movementContextField;
        private static FieldInfo playerField;

        protected override MethodBase GetTargetMethod()
        {
            movementContextField = AccessTools.Field(typeof(MovementState), "MovementContext");
            playerField = AccessTools.Field(typeof(MovementContext), "_player");
            return typeof(MovementState).GetMethod("ChangePose", BindingFlags.Instance | BindingFlags.Public);
        }


        [PatchPrefix]
        private static void Prefix(MovementState __instance)
        {
            MovementContext movementContext = (MovementContext)movementContextField.GetValue(__instance);
            Player player = (Player)playerField.GetValue(movementContext);

            if (player.IsYourPlayer)
            {   //TODO replace with event, and have mount stance class sub to it
                //StanceControllerInstance.IsMounting = false;
            }
        }
    }

    //TODO: move to common lib and make event driven
    //used to determine threshold for cancelling mounting
    public class SetTiltPatch : ModulePatch
    {
        private static FieldInfo movementContextField;
        private static FieldInfo playerField;
        public static float tiltBeforeMount = 0f;

        protected override MethodBase GetTargetMethod()
        {
            movementContextField = AccessTools.Field(typeof(MovementState), "MovementContext");
            playerField = AccessTools.Field(typeof(MovementContext), "_player");
            return typeof(MovementState).GetMethod("SetTilt", BindingFlags.Instance | BindingFlags.Public);
        }

        [PatchPrefix]
        private static void Prefix(MovementState __instance, float tilt)
        {
            MovementContext movementContext = (MovementContext)movementContextField.GetValue(__instance);
            Player player = (Player)playerField.GetValue(movementContext);

            if (player.IsYourPlayer)
            {
                float tiltTolerance = WeaponStateInstance.BipodIsDeployed ? 0.5f : 2.5f;
                if (!StanceControllerInstance)
                {
                    tiltBeforeMount = tilt;
                }
                else if (Math.Abs(tiltBeforeMount - tilt) > tiltTolerance)
                {
                    //TODO replace with event, and have mount stance class sub to it
                    //StanceControllerInstance.IsMounting = false;
                    tiltBeforeMount = 0f;
                }
            }
        }
    }

    //TODO move 3rd person logic to controller, and own class. It's likely not needed at all, since I am now doing it all in component update. If so, just adjust stances if third person.
    //used for applying player and bot third person proc animations
    //bot portion should move to component added to bots
    //player third person portion should be standardized and integrated with first persn stance controller
    /*    public class ApplySimpleRotationPatch : ModulePatch
        {
            private static FieldInfo aimSpeedField;
            private static FieldInfo blindFireStrength;
            private static FieldInfo scopeRotationField;
            private static FieldInfo weapRotationField;
            private static FieldInfo isAimingField;
            private static FieldInfo weaponPositionField;
            private static FieldInfo currentRotationField;
            private static FieldInfo firearmControllerField;
            private static FieldInfo playerField;

            private static Vector3 mountWeapPosition = Vector3.zero;

            private static Vector3 lowReadyTargetRotation = new Vector3(18.0f, 5.0f, -1.0f);
            private static Quaternion lowReadyTargetQuaternion = Quaternion.Euler(lowReadyTargetRotation);
            private static Vector3 lowReadyTargetPostion = new Vector3(0.06f, 0.04f, 0.0f);
            private static Vector3 highReadyTargetRotation = new Vector3(-15.0f, 3.0f, 3.0f);
            private static Quaternion highReadyTargetQuaternion = Quaternion.Euler(highReadyTargetRotation);
            private static Vector3 highReadyTargetPostion = new Vector3(0.05f, 0.1f, -0.12f);
            private static Vector3 activeAimTargetRotation = new Vector3(0.0f, -40.0f, 0.0f);
            private static Quaternion activeAimTargetQuaternion = Quaternion.Euler(activeAimTargetRotation);
            private static Vector3 activeAimTargetPostion = new Vector3(0.0f, 0.0f, 0.0f);
            private static Vector3 shortStockTargetRotation = new Vector3(0.0f, -28.0f, 0.0f);
            private static Quaternion shortStockTargetQuaternion = Quaternion.Euler(shortStockTargetRotation);
            private static Vector3 shortStockTargetPostion = new Vector3(0.05f, 0.18f, -0.2f);
            private static Vector3 tacPistolTargetRotation = new Vector3(0.0f, -20.0f, 0.0f);
            private static Quaternion tacPistolTargetQuaternion = Quaternion.Euler(tacPistolTargetRotation);
            private static Vector3 tacPistolTargetPosition = new Vector3(-0.1f, 0.1f, -0.05f);
            private static Vector3 normalPistolTargetRotation = new Vector3(0f, -5.0f, 0.0f);
            private static Quaternion normalPistolTargetQuaternion = Quaternion.Euler(normalPistolTargetRotation);
            private static Vector3 normalPistolTargetPosition = new Vector3(-0.05f, 0.0f, 0.0f);

            protected override MethodBase GetTargetMethod()
            {
                aimSpeedField = AccessTools.Field(typeof(EFT.Animations.ProceduralWeaponAnimation), "_aimingSpeed");
                blindFireStrength = AccessTools.Field(typeof(EFT.Animations.ProceduralWeaponAnimation), "_blindfireStrength");
                weaponPositionField = AccessTools.Field(typeof(EFT.Animations.ProceduralWeaponAnimation), "_temporaryPosition");
                scopeRotationField = AccessTools.Field(typeof(EFT.Animations.ProceduralWeaponAnimation), "_targetScopeRotation");
                weapRotationField = AccessTools.Field(typeof(EFT.Animations.ProceduralWeaponAnimation), "_temporaryRotation");
                isAimingField = AccessTools.Field(typeof(EFT.Animations.ProceduralWeaponAnimation), "_isAiming");
                currentRotationField = AccessTools.Field(typeof(EFT.Animations.ProceduralWeaponAnimation), "_cameraIdenity");
                firearmControllerField = AccessTools.Field(typeof(ProceduralWeaponAnimation), "_firearmController");
                playerField = AccessTools.Field(typeof(FirearmController), "_player");

                return typeof(EFT.Animations.ProceduralWeaponAnimation).GetMethod("ApplySimpleRotation", BindingFlags.Instance | BindingFlags.Public);
            }

            [PatchPostfix]
            private static void Postfix(EFT.Animations.ProceduralWeaponAnimation __instance, float dt)
            {
                FirearmController firearmController = (FirearmController)firearmControllerField.GetValue(__instance);
                if (firearmController == null)
                {
                    return;
                }
                Player player = (Player)playerField.GetValue(firearmController);
                if (player != null && player.MovementContext.CurrentState.Name != EPlayerState.Stationary)
                {
                    float pitch = (float)blindFireStrength.GetValue(__instance);
                    Quaternion scopeRotation = (Quaternion)scopeRotationField.GetValue(__instance);
                    Vector3 weaponPosition = (Vector3)weaponPositionField.GetValue(__instance);
                    Quaternion weapRotation = (Quaternion)weapRotationField.GetValue(__instance);

                    if (player.IsYourPlayer)
                    {
                        StanceControllerInstance.IsInThirdPerson = true;

                        float aimSpeed = (float)aimSpeedField.GetValue(__instance);
                        bool isAiming = (bool)isAimingField.GetValue(__instance);

                        bool isInStance =
                            StanceControllerInstance.TargetStance == EStance.HighReady ||
                            StanceControllerInstance.TargetStance == EStance.LowReady ||
                            StanceControllerInstance.TargetStance == EStance.ShortStock ||
                            StanceControllerInstance.TargetStance == EStance.ActiveAiming ||
                            StanceControllerInstance.TargetStance == EStance.Melee;
                        bool isInShootableStance =
                            StanceControllerInstance.TargetStance == EStance.ShortStock ||
                            StanceControllerInstance.TargetStance == EStance.ActiveAiming ||
                            StanceControllerInstance.TreatWeaponAsPistolStance ||
                            StanceControllerInstance.TargetStance == EStance.Melee;
                        bool cancelBecauseShooting = !(PluginConfig.RememberStanceFiring.Value && isAiming) && FiringStateInstance.IsFiringFromStance && !isInShootableStance;
                        bool doStanceRotation = (isInStance || !StanceControllerInstance.AllStancesReset || StanceControllerInstance.TargetStance == EStance.PistolCompressed) && !cancelBecauseShooting;
                        bool cancelStance =
                            (StanceControllerInstance.PauseActiveAim && StanceControllerInstance.TargetStance == EStance.ActiveAiming) ||
                            (StanceControllerInstance.PauseHighReady && StanceControllerInstance.TargetStance == EStance.HighReady) ||
                            (StanceControllerInstance.PauseLowReady && StanceControllerInstance.TargetStance == EStance.LowReady) ||
                            (StanceControllerInstance.PauseShortStock && StanceControllerInstance.TargetStance == EStance.ShortStock); //|| (StanceControllerInstance.CancelPistolStance && StanceControllerInstance.PistolIsCompressed)

                        StanceControllerInstance.CurrentStanceRotation = Quaternion.Slerp(StanceControllerInstance.CurrentStanceRotation, __instance.IsAiming && StanceControllerInstance.AllStancesReset ? scopeRotation : doStanceRotation ? StanceControllerInstance.StanceTargetRotation : Quaternion.identity, doStanceRotation ? StanceControllerInstance.StanceRotationSpeed * PluginConfig.StanceRotationSpeedMulti.Value : __instance.IsAiming ? 8f * aimSpeed * dt : 8f * dt);

                        __instance.HandsContainer.WeaponRootAnim.SetPositionAndRotation(weaponPosition, weapRotation * StanceControllerInstance.CurrentStanceRotation);

                        if (StanceControllerInstance.TreatWeaponAsPistolStance && PluginConfig.EnableAltPistol.Value) // && StanceControllerInstance.CurrentStance != EStance.PatrolStance
                        {
                            if (StanceControllerInstance.TargetStance == EStance.PistolCompressed && !AimStateInstance.IsAiming && !StanceControllerInstance.IsResettingPistol && !StanceControllerInstance.IsBlindFiring)
                            {
                                StanceControllerInstance.StanceBlender.Target = 1f;
                            }
                            else
                            {
                                StanceControllerInstance.StanceBlender.Target = 0f;
                            }

                            if ((StanceControllerInstance.TargetStance != EStance.PistolCompressed && !AimStateInstance.IsAiming && !StanceControllerInstance.IsResettingPistol) || (StanceControllerInstance.IsBlindFiring))
                            {
                                StanceControllerInstance.StanceCurrentPosition = Vector3.Lerp(StanceControllerInstance.StanceCurrentPosition, Vector3.zero, 5f * dt);
                            }

                            StanceControllerInstance.HasResetActiveAim = true;
                            StanceControllerInstance.HasResetHighReady = true;
                            StanceControllerInstance.HasResetLowReady = true;
                            StanceControllerInstance.HasResetShortStock = true;
                            StanceControllerInstance.DoPistolStances(true, __instance, dt, player, firearmController, Vector3.zero);
                        }
                        else if (!StanceControllerInstance.TreatWeaponAsPistolStance || WeaponStateInstance.HasShoulderContact)
                        {
                            if ((!isInStance && StanceControllerInstance.AllStancesReset) || (cancelBecauseShooting && !isInShootableStance) || AimStateInstance.IsAiming || cancelStance || StanceControllerInstance.IsBlindFiring)
                            {
                                StanceControllerInstance.StanceBlender.Target = 0f;
                            }
                            else if (isInStance)
                            {
                                StanceControllerInstance.StanceBlender.Target = 1f;
                            }

                            if (((!isInStance && StanceControllerInstance.AllStancesReset) && !cancelBecauseShooting && !AimStateInstance.IsAiming) || (StanceControllerInstance.IsBlindFiring))
                            {
                                StanceControllerInstance.StanceCurrentPosition = Vector3.Lerp(StanceControllerInstance.StanceCurrentPosition, Vector3.zero, 5f * dt);
                            }

                            StanceControllerInstance.HasResetPistolPos = true;
                            StanceControllerInstance.DoRifleStances(player, firearmController, true, __instance, dt, Vector3.zero);
                        }
                    }
           *//*         else if (player.IsAI && !player.AIData.UseZombieSimpleAnimator)
                    {
                        Quaternion targetRotation = Quaternion.identity;
                        Quaternion currentRotation = (Quaternion)currentRotationField.GetValue(__instance);
                        aimSpeedField.SetValue(__instance, 1f);

                        FaceShieldComponent fsComponent = player.FaceShieldObserver.Component;
                        NightVisionComponent nvgComponent = player.NightVisionObserver.Component;
                        bool nvgIsOn = nvgComponent != null && (nvgComponent.Togglable == null || nvgComponent.Togglable.On);
                        bool fsIsON = fsComponent != null && (fsComponent.Togglable == null || fsComponent.Togglable.On);

                        float lastDistance = player.AIData.BotOwner.AimingManager.CurrentAiming.LastDist2Target;
                        Vector3 distanceVect = player.AIData.BotOwner.AimingManager.CurrentAiming.RealTargetPoint - player.AIData.BotOwner.MyHead.position;
                        float realDistance = distanceVect.magnitude;

                        bool isTacBot = StanceControllerInstance._botsToUseTacticalStances.IndexOf(player.AIData.BotOwner.Profile.Info.Settings.Role) != -1;
                        bool isPeace = player.AIData.BotOwner.Memory.IsPeace;
                        bool notShooting = !player.AIData.BotOwner.ShootData.Shooting && Time.time - player.AIData.BotOwner.ShootData.LastTriggerPressd > 15f;
                        bool isInStance = false;
                        float stanceSpeed = 1f;

                        if (player.MovementContext.BlindFire == 0 && player.MovementContext.StationaryWeapon == null)
                        {
                            if (isPeace && !player.IsSprintEnabled && !__instance.IsAiming && !firearmController.IsInReloadOperation() && !firearmController.IsInventoryOpen() && !firearmController.IsInInteractionStrictCheck() && !firearmController.IsInSpawnOperation() && !firearmController.IsHandsProcessing()) // && player.AIData.BotOwner.WeaponManager.IsWeaponReady &&  player.AIData.BotOwner.WeaponManager.InIdleState()
                            {
                                isInStance = true;
                                player.MovementContext.SetPatrol(true);
                            }
                            else
                            {
                                player.MovementContext.SetPatrol(false);
                                if (firearmController.Weapon.WeapClass != "pistol")
                                {
                                    ////low ready//// 
                                    if (!isTacBot && !firearmController.IsInReloadOperation() && !player.IsSprintEnabled && !__instance.IsAiming && notShooting && (lastDistance >= 25f || lastDistance == 0f))    // (Time.time - player.AIData.BotOwner.Memory.LastEnemyTimeSeen) > 1f
                                    {
                                        isInStance = true;
                                        stanceSpeed = 12f * dt;
                                        targetRotation = lowReadyTargetQuaternion;
                                        __instance.HandsContainer.HandsPosition.Zero = __instance.PositionZeroSum + pitch * lowReadyTargetPostion;
                                    }

                                    ////high ready////
                                    if (isTacBot && !firearmController.IsInReloadOperation() && !__instance.IsAiming && notShooting && (lastDistance >= 25f || lastDistance == 0f))
                                    {
                                        isInStance = true;
                                        player.BodyAnimatorCommon.SetFloat(PlayerAnimator.WEAPON_SIZE_MODIFIER_PARAM_HASH, 2);
                                        stanceSpeed = 10.8f * dt;
                                        targetRotation = highReadyTargetQuaternion;
                                        __instance.HandsContainer.HandsPosition.Zero = __instance.PositionZeroSum + pitch * highReadyTargetPostion;
                                    }
                                    else
                                    {
                                        player.BodyAnimatorCommon.SetFloat(PlayerAnimator.WEAPON_SIZE_MODIFIER_PARAM_HASH, (float)firearmController.Item.CalculateCellSize().X);
                                    }

                                    ///active aim//// 
                                    if (isTacBot && (((nvgIsOn || fsIsON) && !player.IsSprintEnabled && !firearmController.IsInReloadOperation() && lastDistance < 25f && lastDistance > 2f && lastDistance != 0f) || (__instance.IsAiming && (nvgIsOn && __instance.CurrentScope.IsOptic || fsIsON))))
                                    {
                                        isInStance = true;
                                        stanceSpeed = 6f * dt;
                                        targetRotation = activeAimTargetQuaternion;
                                        __instance.HandsContainer.HandsPosition.Zero = __instance.PositionZeroSum + pitch * activeAimTargetPostion;
                                    }

                                    ///short stock//// 
                                    if (isTacBot && !player.IsSprintEnabled && !firearmController.IsInReloadOperation() && lastDistance <= 2f && lastDistance != 0f)
                                    {
                                        isInStance = true;
                                        stanceSpeed = 12f * dt;
                                        targetRotation = shortStockTargetQuaternion;
                                        __instance.HandsContainer.HandsPosition.Zero = __instance.PositionZeroSum + pitch * shortStockTargetPostion;
                                    }
                                }
                                else
                                {
                                    if (!isTacBot && !player.IsSprintEnabled && !__instance.IsAiming && notShooting)
                                    {
                                        isInStance = true;
                                        stanceSpeed = 6f * dt;
                                        targetRotation = normalPistolTargetQuaternion;
                                        __instance.HandsContainer.HandsPosition.Zero = __instance.PositionZeroSum + pitch * normalPistolTargetPosition;
                                    }

                                    if (isTacBot && !player.IsSprintEnabled && !__instance.IsAiming && notShooting)
                                    {
                                        isInStance = true;
                                        stanceSpeed = 6f * dt;
                                        targetRotation = tacPistolTargetQuaternion;
                                        __instance.HandsContainer.HandsPosition.Zero = __instance.PositionZeroSum + pitch * tacPistolTargetPosition;
                                    }
                                }
                            }
                        }

                        currentRotation = Quaternion.Slerp(currentRotation, __instance.IsAiming && !isInStance ? scopeRotation : isInStance ? targetRotation : Quaternion.identity, isInStance ? stanceSpeed : 8f * dt);
                        __instance.HandsContainer.WeaponRootAnim.SetPositionAndRotation(weaponPosition, weapRotation * currentRotation);
                        currentRotationField.SetValue(__instance, currentRotation);
                    }*//*
                }
            }
        }*/
}


