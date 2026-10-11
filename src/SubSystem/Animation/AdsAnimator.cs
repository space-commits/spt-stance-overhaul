using EFT.Animations;
using EFT;
using StanceOverhaul.Stances;
using System;
using System.Collections.Generic;
using System.Text;
using static EFT.Player;
using UnityEngine;

namespace StanceOverhaul.SubSystem.Animator
{
    internal class AdsAnimator
    {
    }

/*    private void DoADSWiggle(ProceduralWeaponAnimation pwa, Player player, FirearmController fc, float factor)
    {
        if (IsIdle() && !IsChonker && !WeaponStateInstance.IsPistol)
        {
            CanResetDamping = false;
            float mountingFactor = IsMounting ? 0.1f : IsBracing ? 0.25f : 1f;
            float headGearFactor = GearStateInstance.FaceShieldIsActive || GearStateInstance.NVGIsActive || GearStateInstance.HasGasMask ? 3f : 1f;
            float baseLine = Mathf.Clamp(3.5f * factor * headGearFactor * mountingFactor * WeaponStateInstance.TotalAimStabilityModi, 0.1f, 17f);
            float rndX = UnityEngine.Random.Range(baseLine * 0.9f, baseLine);
            float rndY = UnityEngine.Random.Range(baseLine * 0.9f, baseLine);
            Vector3 wiggleDir = new Vector3(-rndX, -rndY, 0f);

            if (pwa.IsAiming && !_didAimWiggle)
            {
                if (!FiringStateInstance.IsFiringFromStance && TargetStance != EStance.LeftShoulder) DoWiggleEffects(player, pwa, fc.Weapon, wiggleDir, wiggleFactor: factor, isADS: true);
                _didAimWiggle = true;
            }
            else if (!pwa.IsAiming && _didAimWiggle)
            {
                _didAimWiggle = false;
            }
            _doDampingTimer = true;
        }
    }*/
}
