using EFT.Animations;
using UnityEngine;

namespace StanceOverhaul.SubSystem.Animator
{
    /// <summary>
    /// A clone of EFT's Spring, stepped once per physics tick (SpringUpdatePatch) like EFT's own hands springs.
    /// Above the tick rate a raw read only changes on frames with a tick, so reads are interpolated between the last two
    /// tick states and change every frame. Dynamics, tuning and the 2x rest value (Get() = Zero + Current) are unchanged;
    /// Current lags by up to one tick.
    /// </summary>
    public class InterpolatedSpring
    {
        private readonly Spring _spring;
        private Vector3 _previousCurrent;

        public InterpolatedSpring(Spring spring)
        {
            _spring = spring;
            _previousCurrent = spring.Current;
        }

        public Vector3 Zero
        {
            get => _spring.Zero;
            set => _spring.Zero = value;
        }

        public float ReturnSpeed
        {
            get => _spring.ReturnSpeed;
            set => _spring.ReturnSpeed = value;
        }

        public float Damping
        {
            get => _spring.Damping;
            set => _spring.Damping = value;
        }

        /// <summary>
        /// Current, interpolated to the render frame.
        /// </summary>
        public Vector3 Current => Vector3.Lerp(_previousCurrent, _spring.Current, TickAlpha);

        // Unity calls Player.FixedUpdate (EFT's tick count) in its physics loop, so Time.fixedTime is the latest tick's time
        // and this is how far the render frame is into the next tick
        private static float TickAlpha =>
            Time.fixedDeltaTime > 0f ? Mathf.Clamp01((Time.time - Time.fixedTime) / Time.fixedDeltaTime) : 1f;

        // called once per tick, so after a multi-tick frame _previousCurrent is one tick back, not one frame back
        public void FixedUpdate(float dt, int nFixedFrames)
        {
            _previousCurrent = _spring.Current;
            _spring.FixedUpdate(dt, nFixedFrames);
        }

        public void Reset()
        {
            _spring.Reset();
            _previousCurrent = _spring.Current;
        }

        public Vector3 Get() => Zero + Current;

        public Vector3 GetRelative() => Current;
    }
}
