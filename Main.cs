using System.Linq;
using MelonLoader;
using M1A1Abrams;      // the assembly attributes below name the mod type
using GHPC.State;
using System.Collections;
using UnityEngine;
using GHPC.Vehicle;
using ModUtil;
using Presets;

[assembly: MelonInfo(typeof(M1A1AbramsMod), "M1A1 Abrams", "1.3.8", "ATLAS, WPSX")]
[assembly: MelonGame("Radian Simulations LLC", "GHPC")]

namespace M1A1Abrams
{
    public class M1A1AbramsMod : MelonMod {
        private ModuleManager module_manager;
        public static Vehicle[] vics;
        public static GameObject game_manager;
        private int valid_scene_count = 0;

        public IEnumerator OnGameReady(GameState _)
        {
            vics = GameObject.FindObjectsByType<Vehicle>(FindObjectsSortMode.None);
            game_manager = GameObject.Find("_APP_GHPC_");

            module_manager.LoadAllDynamicAssets();

            yield break;
        }

        public override void OnInitializeMelon()
        {
            // MelonLoader automatically applies this assembly's Harmony patches.
            // Patching again can overwrite the M1 -> M1IP prefix state before its postfix runs.

            module_manager = new ModuleManager();
            MelonPreferences_Category cfg = MelonPreferences.CreateCategory("M1A1-CROWSFIX");
            M1A1.Config(cfg);

            module_manager.Add("AMMO_120MM", new Ammo_120mm());
            module_manager.Add("DUArmour", new DUArmour());
            module_manager.Add("Assets", new Assets());
            module_manager.Add("GAS", new GAS());

            PresetManager.LoadAllPresets();
        }

        public override void OnSceneWasLoaded(int idx, string scene_name) {
            if (Util.MenuScreens.Contains(scene_name))
            {
                ResetMissionAssets();
                valid_scene_count = 0;
                module_manager.LoadAllStaticAssets();
                AssetUtil.ReleaseVanillaAssets();
            }

            if (Util.MenuScreensWithLoader.Contains(scene_name)) return;

            valid_scene_count++;

            // Unload once at the first mission stage, rather than on every scene callback.
            if (valid_scene_count == 1)
            {
                ResetMissionAssets();
            }

            if (valid_scene_count == 2)
            {       
                StateController.RunOrDefer(GameState.PlayerReady, new GameStateEventHandler(AssetUtil.ReleaseTempVanillaAssetsDeferred), GameStatePriority.Medium);
                StateController.RunOrDefer(GameState.PlayerReady, new GameStateEventHandler(OnGameReady), GameStatePriority.Medium);

                CITVManager.Init();
                M1A1.Init();
                valid_scene_count = 0;
            }
        }

        private void ResetMissionAssets()
        {
            module_manager.UnloadAllDynamicAssets();
            M1A1.ResetConversionState();
            vics = null;
            game_manager = null;
        }
    }
}
