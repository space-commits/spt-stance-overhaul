using RealismCommonLib.Integration;
using StanceOverhaul.Controllers;
using StanceOverhaul.Enums;

namespace StanceOverhaul.Integration
{
    /// <summary>
    /// Exposes stance data to other modules through the common lib (ModuleServices.Stance).
    /// </summary>
    internal sealed class StanceInfoProvider : IStanceInfo
    {
        // Unity null check: the static isn't cleared on destroy, so after a raid it points at a destroyed component
        //TODO: replace this workaround with non-static controller instances
        private static StanceController? Controller
        {
            get
            {
                StanceController controller = Plugin.StanceControllerInstance;
                return controller != null && controller.AwakeRan ? controller : null;
            }
        }

        public bool IsLeftShoulder
        {
            get
            {
                StanceController? controller = Controller;
                return controller != null && controller.CurrentStanceType == EStanceType.LeftShoulder;
            }
        }
    }
}
