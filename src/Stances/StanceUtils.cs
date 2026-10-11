using RealismCommonLib.Utils;
using UnityEngine;
using static RealismCommonLib.Plugin;
using static StanceOverhaul.Plugin;

namespace StanceOverhaul.Stances;

public static class StanceUtils
{
    /// <summary>
    /// When weapon X axis offset (left-right) is greater than 0, stances which have a particular x offset will not have the intended
    /// positioning (for example Active Aiming is supposed to be centered). 
    /// This method normalizes the x offset of a position curve to account for the weapon offset.
    /// </summary>
    /// <param name="curve"></param>
    /// <returns></returns>
    public static Vector3Curve NormalizeXPositionCurve(Vector3Curve curve)
    {
        var newXEnterCurve = new AnimationCurve();
        foreach (var key in curve.XCurve.keys)
        {
            var newValue = key.value;
            if (key.value < 0f)
            {
                newValue -= StanceControllerInstance.BaseWeaponOffsetPosition.x;
            }
            newXEnterCurve.AddKey(key.time, newValue);
        }

        return new Vector3Curve(newXEnterCurve, curve.YCurve, curve.ZCurve);
    }
}