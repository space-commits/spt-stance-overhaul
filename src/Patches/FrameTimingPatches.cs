using EFT;
using EFT.Animations;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.IO;
using System.Reflection;
using UnityEngine;
using static StanceOverhaul.Plugin;
using static RealismCommonLib.Plugin;

namespace StanceOverhaul.Patches
{
    // Option F test (Stance-Spring-Smoothing-Plan.md): EFT passes LerpCamera the summed physics tick time on frames
    // with ticks, otherwise _armsTime. With the toggle on, it gets the frame's own delta time instead.
    public class CameraDeltaTimePatch : ModulePatch
    {
        private static AccessTools.FieldRef<ProceduralWeaponAnimation, float> _overweightAimingMultiplier =
            AccessTools.FieldRefAccess<ProceduralWeaponAnimation, float>("_overweightAimingMultiplier");

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(FirstPersonStrategy), nameof(FirstPersonStrategy.ApplyCameraTransformations));
        }

        [PatchPrefix]
        private static void PatchPrefix(ProceduralWeaponAnimation pwa, ref float dt)
        {
            if (PlayerStateInstance?.PWA != pwa) return;

            float eftDt = dt;
            float k = CameraLerpRate(pwa);

            if (PluginConfig.CameraUsesRenderDeltaTime.Value)
            {
                dt = PluginConfig.CameraLerpFrameRateIndependent.Value
                    ? FrameRateIndependentDeltaTime(k, Time.deltaTime)
                    : Time.deltaTime;
            }

            FrameTimingLog.OnCameraDeltaTime(eftDt, dt, k); // TEMP: frame timing logging
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

    // TEMP: frame timing logging
    public class ProcessEffectorsTimingPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(ProceduralWeaponAnimation), nameof(ProceduralWeaponAnimation.ProcessEffectors));
        }

        [PatchPrefix]
        private static void PatchPrefix(ProceduralWeaponAnimation __instance, float deltaTime, int nFixedFrames)
        {
            if (PlayerStateInstance?.PWA != __instance) return;
            FrameTimingLog.OnProcessEffectors(deltaTime, nFixedFrames);
        }
    }

    // TEMP: frame timing logging
    // Writes to BepInEx/FrameTiming.log (appended, survives game restarts). Header and summaries also go to the BepInEx log.
    // Units: times in ms, positions in mm, rotations in degrees. One line per frame while aiming, a summary every second.
    internal static class FrameTimingLog
    {
        private const float SummaryInterval = 1f;
        private static readonly string FilePath = Path.Combine(BepInEx.Paths.BepInExRootPath, "FrameTiming.log");

        private static StreamWriter? _writer;

        private static int _peFrame = -1;
        private static float _peDt;
        private static int _ticks;
        private static Vector3 _stancePosCurrentBefore;
        private static Vector3 _stanceRotCurrentBefore;

        private static int _camFrame = -1;
        private static float _camDtEft;
        private static float _camDtUsed;
        private static float _camK;

        private static Vector3 _lastCameraPos;
        private static string _lastHeader = "";

        private static float _summaryTimer;
        private static int _frames;
        private static int _ticks0;
        private static int _ticks1;
        private static int _ticks2Plus;
        private static float _realTime;
        private static float _camTimeEft;
        private static float _camTimeUsed;

        private static bool Enabled =>
            PluginConfig.LogFrameTiming.Value && StanceControllerInstance != null && StanceControllerInstance.AwakeRan;

        // hooked up from PluginConfig, so turning the toggle on is confirmed in the BepInEx log straight away
        public static void OnToggleChanged()
        {
            if (PluginConfig.LogFrameTiming.Value)
            {
                _lastHeader = "";
                Write($"===== frame timing logging started {DateTime.Now:yyyy-MM-dd HH:mm:ss} =====");
                ModLogger.LogWarning($"[FrameTiming] logging on, writing to {FilePath}");
            }
            else
            {
                Write($"===== frame timing logging stopped {DateTime.Now:yyyy-MM-dd HH:mm:ss} =====");
                _writer?.Flush();
                _writer?.Dispose();
                _writer = null;
                ModLogger.LogWarning("[FrameTiming] logging off");
            }
        }

        public static void OnProcessEffectors(float deltaTime, int nFixedFrames)
        {
            if (!Enabled) return;

            _peFrame = Time.frameCount;
            _peDt = deltaTime;
            _ticks = nFixedFrames;
            // before this frame's tick burst steps the springs
            _stancePosCurrentBefore = StanceControllerInstance.StancePositionSpring.RawCurrent;
            _stanceRotCurrentBefore = StanceControllerInstance.StanceRotationSpring.RawCurrent;
        }

        public static void OnCameraDeltaTime(float eftDt, float usedDt, float k)
        {
            if (!Enabled) return;

            _camFrame = Time.frameCount;
            _camDtEft = eftDt;
            _camDtUsed = usedDt;
            _camK = k;
        }

        // called from the IkProcess prefix: the camera is final for this frame
        public static void OnFrameEnd(ProceduralWeaponAnimation pwa)
        {
            if (!Enabled || pwa == null || pwa.HandsContainer?.CameraTransform == null) return;

            LogHeaderIfChanged();

            bool hasPe = _peFrame == Time.frameCount;
            bool hasCam = _camFrame == Time.frameCount;
            UpdateSummary(hasPe, hasCam);

            Vector3 cameraPos = pwa.HandsContainer.CameraTransform.localPosition;
            Vector3 cameraStep = cameraPos - _lastCameraPos;
            _lastCameraPos = cameraPos;

            if (!pwa.IsAiming) return;

            // stPosCur/stPosGet are what's applied (interpolated when enabled), stPosStep/stRotStep are the raw tick steps
            Vector3 stancePosCurrent = StanceControllerInstance.StancePositionSpring.Current;
            Vector3 stanceRotCurrent = StanceControllerInstance.StanceRotationSpring.Current;
            Vector3 stancePosStep = hasPe ? StanceControllerInstance.StancePositionSpring.RawCurrent - _stancePosCurrentBefore : Vector3.zero;
            Vector3 stanceRotStep = hasPe ? StanceControllerInstance.StanceRotationSpring.RawCurrent - _stanceRotCurrentBefore : Vector3.zero;
            Vector3 stancePosGet = StanceControllerInstance.StancePositionSpring.Get();
            float alpha = Time.fixedDeltaTime > 0f ? (Time.time - Time.fixedTime) / Time.fixedDeltaTime : -1f;

            Write(
                $"f={Time.frameCount} rdt={Ms(Time.deltaTime)} " +
                $"ticks={(hasPe ? _ticks.ToString() : "-")} peDt={(hasPe ? Ms(_peDt) : "-")} " +
                $"camDt={(hasCam ? Ms(_camDtEft) + "->" + Ms(_camDtUsed) : "-")} camK={_camK:F1} camT={(hasCam ? (_camDtUsed * _camK).ToString("F3") : "-")} a={alpha:F2} " +
                $"stPosCur=({Mm(stancePosCurrent.x)},{Mm(stancePosCurrent.y)},{Mm(stancePosCurrent.z)}) " +
                $"stPosStep=({Mm(stancePosStep.x)},{Mm(stancePosStep.y)},{Mm(stancePosStep.z)}) stPosGet.x={Mm(stancePosGet.x)} " +
                $"stRotCur.y={stanceRotCurrent.y:F3} stRotStep.y={stanceRotStep.y:F3} " +
                $"cam=({Mm(cameraPos.x)},{Mm(cameraPos.y)},{Mm(cameraPos.z)}) camStep=({Mm(cameraStep.x)},{Mm(cameraStep.y)},{Mm(cameraStep.z)}) " +
                $"tgt=({Mm(pwa._vCameraTarget.x)},{Mm(pwa._vCameraTarget.y)},{Mm(pwa._vCameraTarget.z)}) " +
                $"alt={StanceControllerInstance.AltAimWeight:F2} ls={StanceControllerInstance.LeftShoulderPoseWeight:F2} " +
                $"stance={StanceControllerInstance.CurrentStanceType} F={PluginConfig.CameraUsesRenderDeltaTime.Value} FRI={PluginConfig.CameraLerpFrameRateIndependent.Value} " +
                $"IS={PluginConfig.InterpolateStanceSprings.Value}");
        }

        private static void LogHeaderIfChanged()
        {
            Player player = PlayerStateInstance?.Player;
            if (player == null) return;

            string header =
                $"armsQueue={player.ArmsUpdateQueue} armsMode={player.ArmsUpdateMode} updateQueue={player.UpdateQueue} " +
                $"fixedDt={Ms(Time.fixedDeltaTime)} timeScale={Time.timeScale:F2}";

            if (header == _lastHeader) return;
            _lastHeader = header;
            Write($"HEADER {header}");
            ModLogger.LogWarning($"[FrameTiming] {header}");
        }

        private static void UpdateSummary(bool hasPe, bool hasCam)
        {
            _frames++;
            _realTime += Time.deltaTime;
            if (hasPe)
            {
                if (_ticks == 0) _ticks0++;
                else if (_ticks == 1) _ticks1++;
                else _ticks2Plus++;
            }
            if (hasCam)
            {
                _camTimeEft += _camDtEft;
                _camTimeUsed += _camDtUsed;
            }

            _summaryTimer += Time.deltaTime;
            if (_summaryTimer < SummaryInterval) return;

            // camTime/real > 1 means the camera lerp is fed more time than actually passed
            string summary =
                $"SUMMARY frames={_frames} fps={(_frames / Mathf.Max(_realTime, 0.0001f)):F0} " +
                $"ticks0={_ticks0} ticks1={_ticks1} ticks2+={_ticks2Plus} " +
                $"camTimeEft/real={(_camTimeEft / Mathf.Max(_realTime, 0.0001f)):F2} camTimeUsed/real={(_camTimeUsed / Mathf.Max(_realTime, 0.0001f)):F2} " +
                $"F={PluginConfig.CameraUsesRenderDeltaTime.Value} FRI={PluginConfig.CameraLerpFrameRateIndependent.Value} IS={PluginConfig.InterpolateStanceSprings.Value}";
            Write(summary);
            _writer?.Flush();
            ModLogger.LogWarning($"[FrameTiming] {summary}");

            _summaryTimer = 0f;
            _frames = _ticks0 = _ticks1 = _ticks2Plus = 0;
            _realTime = _camTimeEft = _camTimeUsed = 0f;
        }

        private static void Write(string line)
        {
            try
            {
                _writer ??= new StreamWriter(FilePath, append: true);
                _writer.WriteLine(line);
            }
            catch (Exception e)
            {
                ModLogger.LogError($"[FrameTiming] can't write {FilePath}: {e.Message}");
                PluginConfig.LogFrameTiming.Value = false;
            }
        }

        private static string Ms(float seconds) => (seconds * 1000f).ToString("F2");
        private static string Mm(float metres) => (metres * 1000f).ToString("F2");
    }
}
