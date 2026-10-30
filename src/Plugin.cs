using BepInEx;
using StanceOverhaul.Controllers;
using StanceOverhaul.Patches;
using EFT;
using Unity;
using UnityEngine;
using static RealismCommonLib.Plugin;
using RootMotion.FinalIK;
using StanceOverhaul.Events;
using StanceOverhaul.Integration;
using RealismCommonLib.Integration;

namespace StanceOverhaul
{
    [BepInDependency(RealismCommonLib.Plugin.MOD_GUID)]
    [BepInPlugin(MOD_GUID, MOD_NAME, MOD_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        public static StanceController StanceControllerInstance;

        public const string MOD_GUID = "com.fontaine.spt-stance-overhaul";
        public const string MOD_NAME = "Fontaine-SPTStanceOverhaul";
        public const string MOD_VERSION = "0.0.2";

        void Awake()
        {
            PluginConfig.InitConfigBindings(Config);

            ModuleServices.Register<IStanceInfo>(new StanceInfoProvider());

            SubscribeToEvents();

            EnablePatches();
        }

        void EnablePatches()
        {
            //Spring
            new ZeroAdjustmentsPatch().Enable();
            new SpringResetPatch().Enable();
            new SpringGetPatch().Enable();
            new SpringUpdatePatch().Enable();

            //Animation
            new SetFireModePatch().Enable();
            new InitTransformsPatch().Enable();
            //new ComplexRotationsPatch().Enable();
            new ShouldMoveWeapCloserPatch().Enable();
            new HandIKPatch().Enable();
            new ElbowIKPatch().Enable();
            new PropSoundPatch().Enable();
            new VisualPassPatch().Enable();
            new PreRecoilPosePatch().Enable();
            new PostRecoilPosePatch().Enable();

            //State & Stats
            //new OperateStationaryWeaponPatch().Enable();
            new UpdateWeaponVariablesPatch().Enable();
            new UpdateHipInaccuracyPatch().Enable();
            new ApplyTransformsPatch().Enable();
            new CalculateCameraPositionPatch().Enable();
            new OperateStationaryWeaponPatch().Enable();
            new CameraDeltaTimePatch().Enable();

                        //new TacticalReloadPatch().Enable();
            /*            new SprintPatch().Enable();
                        new DisableAimOnReloadPatch().Enable();*/
            /*            new ChangeScopePatch().Enable();*/
            /*        new MountingAndCollisionPatch().Enable();
                    new CollisionPatch().Enable();*/

            /*            new WeaponOverlapViewPatch().Enable();
                        new WeaponOverlappingPatch().Enable();*/
            /*            new ShouldMoveWeapCloserPatch().Enable();*/
            /*       new WeaponLengthPatch().Enable();*/
            /*      new InitTransformsPatch().Enable();*/
            /*       new ChangePosePatch().Enable();
                   new SetTiltPatch().Enable();*/

            //new ProcessEffectorsPatch().Enable();

            //new SpringGetRelativePatch().Enable();

            /*            new ShiftWeaponRootPatch().Enable();
                        new HeadPatch().Enable();*/

            //new ApplyComplexRotationPatch().Enable();


            //new CameraPatch().Enable();
            /*
                        new CalibrateLocalPatch().Enable();
                        new CalibratePatch().Enable();
            */

            //new BlenderPatch().Enable();


            /*            new ScopeRotationPatch().Enable();



                        new ApplyAimingAlignmentPatch().Enable();*/


            //new ApplyPositionPatch().Enable();

            //new TacticalReloadMethodPatch().Enable();
            /*           new FOVPatch().Enable();
                       new IsAimingPatch().Enable();
                       new IsAimingPatch1().Enable();
                       new IsAimingPatch2().Enable();
                       new IsAimingPatch3().Enable();
                       new IsAimingPatch4().Enable();
                       new IsAimingPatch5().Enable();
                       new CamRecoilPatch().Enable();
                       new IntensityByAimingPatch().Enable();
                       new UpdateAimWeightPatch().Enable();*/
        }

        void SubscribeToEvents()
        {
            RealismCommonLib.Events.PlayerEvents.OnPlayerInitRef += AddStanceComponentsToPlayer;
        }

        void AddStanceComponentsToPlayer(Player player)
        {
            player.gameObject.AddComponent<StanceController>();

            StanceEvents.RaiseOnPlayerLoad(player);
        }
    }
}



