using System.Collections.Generic;
using System.IO;
using MelonLoader.Utils;
using Tomlet.Models;
using Tomlet;
using System;
using MelonLoader;

namespace Presets
{
    internal class PresetManager {
        internal static readonly List<PresetManager> PresetManagers = new List<PresetManager>();

        public virtual void LoadPresets() { }

        public static void LoadAllPresets()
        {
            foreach (PresetManager preset_manager in PresetManagers)
            {
                preset_manager.LoadPresets();
            }
        }

        internal static void ClearAll()
        {
            PresetManagers.Clear();
        }
    }

    internal class PresetManager<T> : PresetManager
    {
        public T PlayerReservedPreset { get; set; }
        public bool HasPlayerReservedPreset { get; set; } = false;

        private List<T> loaded_presets = new List<T>();
        private List<T> preset_choice_pool = new List<T>();
        private string preset_bundle_path;

        public PresetManager(string preset_bundle_path)
        {
            this.preset_bundle_path = preset_bundle_path;
            PresetManagers.Add(this);
        }

        public override void LoadPresets()
        {
            string full_path = Path.Combine(MelonEnvironment.ModsDirectory, preset_bundle_path);

            loaded_presets.Clear();
            preset_choice_pool.Clear();
            PlayerReservedPreset = default(T);
            HasPlayerReservedPreset = false;

            try 
            {

                string[] preset_paths = Directory.GetFiles(full_path, "*.cfg");

                foreach (string preset_path in preset_paths)
                {
                    TomlDocument toml_doc = TomlParser.ParseFile(preset_path);
                    T preset = TomletMain.To<T>(toml_doc);
                    loaded_presets.Add(preset);

                    PresetTemplate t = preset as PresetTemplate;

                    int weight = t.Weight;
                    for (int i = 0; i < weight; i++)
                    {
                        preset_choice_pool.Add(preset);
                    }

                    if (t.GuaranteeForStartingUnit)
                    {
                        HasPlayerReservedPreset = true;
                        PlayerReservedPreset = preset;
                    }
                }

            }
            catch (Exception error)
            {
                MelonLogger.Error("[M1A1] Could not load preset bundle '" + full_path + "': " + error);
            }
        }

        public T ChoosePreset()
        {
            if (preset_choice_pool.Count == 0)
            {
                return default(T);
            }

            return preset_choice_pool[UnityEngine.Random.Range(0, preset_choice_pool.Count)];
        }
    }
}
