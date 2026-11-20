namespace StanceOverhaul.Enums;

// What can be put on a slice of the stance selection wheel (see PluginConfig, "Stance Selection Wheel").
// None removes the slice, and its neighbours grow into its place.
public enum EWheelStance
{
    None,
    HighReady,
    LowReady,
    ActiveAim,
    ShortStock,
    Patrol,
    LeftShoulder
}
