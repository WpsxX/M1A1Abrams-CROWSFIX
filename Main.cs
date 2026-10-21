using System.Linq;
using MelonLoader;
using M1A1Abrams;      // the assembly attributes below name the mod type
using GHPC.State;
using System.Collections;
using UnityEngine;
using GHPC.Vehicle;
using ModUtil;
using Presets;
using VUtil = VanillaAssetUtil.ModUtil.Util;

[assembly: MelonInfo(typeof(M1A1AbramsMod), "M1A1 Abrams", "1.4.5", "ATLAS, WPSX")]
[assembly: MelonGame("Radian Simulations LLC", "GHPC")]

namespace M1A1Abrams
{
    public class M1A1AbramsMod : MelonMod {
        private ModuleManager module_manager;
        public static Vehicle[] vics;
        public static GameObject game_manager;
        private int valid_scene_count = 0;
        private object mission_watcher;

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

            bool menu_scene = VUtil.MenuScreens.Contains(scene_name);

            if (menu_scene)
            {
                ResetMissionAssets();
                valid_scene_count = 0;
                module_manager.LoadAllStaticAssets();

                // Do NOT call AssetUtil.ReleaseVanillaAssets() here. It releases every vanilla
                // asset handle in VanillaAssetUtil's shared list, including the ones other mods
                // loaded. A later LoadVanillaVehicle from another mod (Fulda 1989's Leopard 2A4
                // module loads the vanilla LEO2A3 prefab when a mission spawns) then hits a
                // released Addressables handle and throws "Unable to load asset of type
                // UnityEngine.GameObject from location ...", which leaves the mission without a
                // player platoon and freezes the loading screen under a per-frame NRE flood.
                // Our own vanilla assets are few and are re-used across missions, so keeping
                // them resident is the cheaper price.
            }

            if (VUtil.MenuScreensWithLoader.Contains(scene_name)) return;

            valid_scene_count++;

            // Unload once at the first mission stage, rather than on every scene callback.
            if (valid_scene_count == 1)
            {
                ResetMissionAssets();
            }

            if (valid_scene_count == 2)
            {       
                // Nothing in this mod uses the temp vanilla pool any more (the M1 to M1IP transmog
                // loads the M1 prefab permanently), so the PlayerReady temp release that used to be
                // registered here has no work left to do and was removed.
                StateController.RunOrDefer(GameState.PlayerReady, new GameStateEventHandler(OnGameReady), GameStatePriority.Medium);

                CITVManager.Init();
                M1A1.Init();
                valid_scene_count = 0;
            }

            // The registration above is the documented path, but it can fail without a word: RunOrDefer
            // returns Fail while the state machine does not exist yet, and a handler deferred before the
            // StateController is torn down is dropped by its OnDestroy. Either one leaves the station
            // unbuilt with the vanilla cupola gun still on the roof, and neither logs anything. This
            // watcher never touches the state machine: it waits for the mission's vehicles and for
            // PlayerReady, then converts once.
            if (!menu_scene && mission_watcher == null)
            {
                mission_watcher = MelonCoroutines.Start(MissionWatcher());
            }
        }

        private IEnumerator MissionWatcher()
        {
            const int MAX_FRAMES = 3600; // about a minute at 60 fps

            Vehicle[] found = null;
            bool player_ready = false;

            for (int frame = 0; frame < MAX_FRAMES; frame++)
            {
                found = GameObject.FindObjectsByType<Vehicle>(FindObjectsSortMode.None);
                player_ready = StateController.IsGameStateTriggered(GameState.PlayerReady)
                    || StateController.IsGameStateComplete(GameState.PlayerReady);

                if (found != null && found.Length > 0 && player_ready)
                {
                    break;
                }

                yield return null;
            }

            mission_watcher = null;

            if (found == null || found.Length == 0 || !player_ready || M1A1.converted)
            {
                yield break;
            }

            vics = found;

            if (game_manager == null)
            {
                game_manager = GameObject.Find("_APP_GHPC_");
            }

            module_manager.LoadAllDynamicAssets();
            yield return M1A1.Convert(GameState.PlayerReady);
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

