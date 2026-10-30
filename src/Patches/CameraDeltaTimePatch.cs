using EFT.Animations;
using HarmonyLib;
using SPT.Reflection.Patching;
using System.Reflection;
using UnityEngine;
using static RealismCommonLib.Plugin;

namespace StanceOverhaul.Patches
{
    // EFT passes LerpCamera the summed physics tick time on frames with ticks, otherwise _armsTime. Above the tick rate
    // the camera's per-frame movement alternates, which shows as the weapon juddering on screen, and it gets more time than
    // really passed, so ADS speed scales with fps. This replaces that time with one based on the frame's own delta time.
    public class CameraDeltaTimePatch : ModulePatch
    {
        private static readonly AccessTools.FieldRef<ProceduralWeaponAnimation, float> _overweightAimingMultiplier =
            AccessTools.FieldRefAccess<ProceduralWeaponAnimation, float>("_overweightAimingMultiplier");

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(FirstPersonStrategy), nameof(FirstPersonStrategy.ApplyCameraTransformations));
        }

        [PatchPrefix]
        private static void PatchPrefix(ProceduralWeaponAnimation pwa, ref float dt)
        {
            if (PlayerStateInstance?.PWA != pwa) return;

            dt = FrameRateIndependentDeltaTime(CameraLerpRate(pwa), Time.deltaTime);
        }

        // the xy rate LerpCamera multiplies dt by (PWA L1925)
        private static float CameraLerpRate(ProceduralWeaponAnimation pwa)
        {
            return pwa.IsAiming
                ? pwa.AimingSpeed * pwa.CameraSmoothBlender.Value * _overweightAimingMultiplier(pwa)
                : pwa.CameraSmoothOut;
        }

        // LerpCamera's Lerp(cam, target, dt * k) converges faster at low fps. Returns the dt that gives the per-frame factor
        // converging per second like one lerp per physics tick (vanilla at 60 fps), at any frame rate.
        // Exact for camera xy, approximate for camera z, which uses its own rate.
        private static float FrameRateIndependentDeltaTime(float k, float renderDt)
        {
            float fixedDt = Time.fixedDeltaTime;
            if (k <= 0f || fixedDt <= 0f) return renderDt;

            float tickFactor = Mathf.Min(k * fixedDt, 0.99f);
            float frameFactor = 1f - Mathf.Pow(1f - tickFactor, renderDt / fixedDt);
            return frameFactor / k;
        }
    }
}
