using System;
using RealismCommonLib.Utils;
using StanceOverhaul.Controllers;
using StanceOverhaul.Enums;
using UnityEngine;

namespace StanceOverhaul.Stances
{
    public interface IStance
    {
        public EStanceType StanceType { get; }
        public EStanceStaminaType StaminaMode { get; }
        public float StaminaRate { get; }
        public float WalkSpeedBonus { get; }
        public float SprintAccelBonus { get; }
        public float HipfireBonus { get; }
        public bool CanDoTacSprint { get; }
        public bool RememberStance { get; }
        public float StanceHitShoulderThreshold { get; }
        public float StanceDampingModifier { get; }
        public float StanceReturnSpeedModifier { get; }
        public float MagazineReloadSpeedModifier { get; }
        public float TubeReloadSpeedModifier { get; }
        public float TopReloadSpeedModifier { get; }
        public float RevolverReloadSpeedModifier { get; }
        public float WeaponManipSpeedModifier { get; }
        public float PumpBoltSpeedModifier { get; }

        public bool BlocksFiring { get; }

        public EStanceReloadType[] ReloadTypesThatPauseStance { get; }

        public abstract Vector3Curve EnterRotationCurve { get; }
        public abstract Vector3Curve EnterPositionCurve { get; }

        public abstract Vector3Curve ExitRotationCurve { get; }
        public abstract Vector3Curve ExitPositionCurve { get; }

        public abstract Vector3Curve? PistolEnterRotationCurve { get; }
        public abstract Vector3Curve? PistolEnterPositionCurve { get; }

        public abstract Vector3Curve? PistolExitRotationCurve { get; }
        public abstract Vector3Curve? PistolExitPositionCurve { get; }

        public abstract AnimationCurve ExitAimSpeedCurve { get; }
        public abstract AnimationCurve EnterAimSpeedCurve { get; }

        float BlendIntoThreshold(EStanceType nextStance);
        float TransitionFromModifier(EStanceType? previousStance);
        float TransitionToSpeedModifier(EStanceType? nextStance);

        void CorrectPositionCurveX();

        void OnEnter();
        void OnExit();
        void OnHoldUpdate(float deltaTime);
    }
}
