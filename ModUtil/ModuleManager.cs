using System.Collections.Generic;

namespace ModUtil
{
    internal class ModuleManager
    {
        private readonly Dictionary<string, Module> modules = new Dictionary<string, Module>();

        public void Add(string id, Module module)
        {
            modules.Add(id, module);
        }

        public void UnloadAllDynamicAssets()
        {
            foreach (Module module in modules.Values)
            {
                module.TryUnloadDynamicAssets();
            }
        }

        public void LoadAllDynamicAssets()
        {
            foreach (Module module in modules.Values)
            {
                module.TryLoadDynamicAssets();
            }
        }

        public void LoadAllStaticAssets()
        {
            foreach (Module module in modules.Values)
            {
                module.TryLoadStaticAssets();
            }
        }
    }
}

