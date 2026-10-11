using StanceOverhaul.Events;
using static RealismCommonLib.Plugin;

namespace StanceOverhaul.SubSystem;

public class StanceAudioSystem : ISubSystem
{
      public void RunOnAwake()
        {
            StanceEvents.OnStanceHitShoulder += OnShoulderHit;
        }

        public void RunOnDestroy()
        {
            StanceEvents.OnStanceHitShoulder -= OnShoulderHit;
        }

        public void RunOnUpdate(float deltaTime)
        {
        }

        private void OnShoulderHit()
        {
            AudioControllerInstance.PlayADSSound(PlayerStateInstance.Player, PluginConfig.StanceSfxModifier.Value);
        }

}
