using EFT.Animations;
using UnityEngine;

namespace StanceOverhaul.SubSystem.Aiming
{
    // weapon root pose either side of EFT's rotation recoil step in ApplyComplexRotation, stored in weapon root parent space
    // lets the aim system work out where the sight would be without recoil, so gun to camera doesn't fight it
    public static class RecoilPoseCapture
    {
        private static Vector3 _prePosition;
        private static Quaternion _preRotation = Quaternion.identity;
        private static Vector3 _postPosition;
        private static Quaternion _postRotation = Quaternion.identity;
        private static int _preFrame = -1;
        private static int _postFrame = -1;

        public static void CapturePreRecoil(Transform parent, Vector3 worldPosition, Quaternion worldRotation)
        {
            _prePosition = parent.InverseTransformPoint(worldPosition);
            _preRotation = Quaternion.Inverse(parent.rotation) * worldRotation;
            _preFrame = Time.frameCount;
        }

        public static void CapturePostRecoil(Transform parent, Vector3 worldPosition, Quaternion worldRotation)
        {
            _postPosition = parent.InverseTransformPoint(worldPosition);
            _postRotation = Quaternion.Inverse(parent.rotation) * worldRotation;
            _postFrame = Time.frameCount;
        }

        // sightInParent: sight position in weapon root parent space, measured after EFT has finished posing the weapon
        public static Vector3 RemoveRecoil(ProceduralWeaponAnimation pwa, Vector3 sightInParent)
        {
            Vector3 clean = sightInParent;

            // rotation recoil is a rigid rotation about RecoilPivot, post = recoil * pre, so undo it with pre * post^-1
            // stale captures (ApplyComplexRotation didn't run this frame) are skipped rather than applied
            int frame = Time.frameCount;
            if (_preFrame == frame && _postFrame == frame)
            {
                Quaternion undo = _preRotation * Quaternion.Inverse(_postRotation);
                clean = _prePosition + undo * (sightInParent - _postPosition);
            }

            // position recoil is added straight onto WeaponRootAnim.localPosition by ApplyPosition, so it's already in parent space
            // the captured pre pose includes it, and translation commutes with rotation about a pivot attached to the root
            var recoil = pwa.Shootingg?.CurrentRecoilEffect;
            if (recoil != null)
            {
                clean -= recoil.GetHandPositionRecoil();
                if (recoil.WeaponRecoilEffect != null)
                    clean -= recoil.WeaponRecoilEffect.GetPositionRecoil();
            }

            return clean;
        }
    }
}
