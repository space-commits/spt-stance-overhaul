using UnityEngine;

namespace StanceOverhaul.SubSystem.Aiming
{
    // weapon root pose with and without Stance-Overhaul's stance/offset springs, both in world space, taken at EFT's
    // DeferredRotateWithCustomOrder in ApplyComplexRotation: the step that puts HandsRotation (and with it the stance rotation) on the gun,
    // after ApplyPosition has put HandsPosition (and the stance position) on it.
    // Lets CalculateCameraPositionPatch work out EFT's aiming z target as if the stance weren't on the gun
    public static class StanceFreePoseCapture
    {
        private static Vector3 _actualPosition;
        private static Quaternion _actualRotation = Quaternion.identity;
        private static Vector3 _freePosition;
        private static Quaternion _freeRotation = Quaternion.identity;

        private static int _freeFrame = -1;
        private static int _actualFrame = -1;

        // replays EFT's DeferredRotateWithCustomOrder (PWA L1563) from its inputs, without the stance rotation and stance position.
        // stancePosition: what SpringGetPatch added to HandsPosition.Get(), in weapon root parent space (excludes the base offset).
        // stanceRotation: what SpringGetPatch added to HandsRotation.Get()
        public static void CaptureFree(Transform root, Vector3 worldPivot, Vector3 rotation, Quaternion localBefore,
            Vector3 positionBefore, Quaternion rotationBefore, Vector3 stancePosition, Vector3 stanceRotation)
        {
            Transform parent = root.parent;

            // ApplyPosition put the stance position into the root's local position, and the pivot was taken from that root,
            // with no stance rotation applied yet, so both shift by the same world offset
            Vector3 stanceOffsetWorld = parent.TransformVector(stancePosition);
            Vector3 freePositionBefore = positionBefore - stanceOffsetWorld;
            Vector3 freePivot = worldPivot - stanceOffsetWorld;

            Vector3 freeRotationInput = rotation - stanceRotation;
            Quaternion freeLocal = Quaternion.Euler(freeRotationInput.x, 0f, freeRotationInput.z) * Quaternion.Euler(0f, freeRotationInput.y, 0f) * localBefore;
            Quaternion freeRotation = parent.rotation * freeLocal;
            Quaternion delta = freeRotation * Quaternion.Inverse(rotationBefore);

            _freeRotation = freeRotation;
            _freePosition = freePivot + delta * (freePositionBefore - freePivot);
            _freeFrame = Time.frameCount;
        }

        // EFT's own result of the same call
        public static void CaptureActual(Vector3 position, Quaternion rotation)
        {
            _actualPosition = position;
            _actualRotation = rotation;
            _actualFrame = Time.frameCount;
        }

        // stance-free version of the root's current pose. Everything after the capture (recoil, alignment, obstacle push) is carried over
        // as a rigid offset in the weapon's own frame, the same assumption RecoilPoseCapture.RemoveRecoil makes.
        // False when this frame's ApplyComplexRotation didn't run (stale capture)
        public static bool TryGetStanceFreePose(Transform root, out Vector3 position, out Quaternion rotation)
        {
            int frame = Time.frameCount;
            if (_freeFrame != frame || _actualFrame != frame)
            {
                position = root.position;
                rotation = root.rotation;
                return false;
            }

            Quaternion delta = _freeRotation * Quaternion.Inverse(_actualRotation);
            position = _freePosition + delta * (root.position - _actualPosition);
            rotation = delta * root.rotation;
            return true;
        }
    }
}
