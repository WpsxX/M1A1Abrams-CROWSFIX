using System;

namespace ModUtil
{
    public class Module
    {
        private bool static_assets_loaded = false;
        private bool dynamic_assets_loaded = false;

        /// <summary>
        /// Modules may only apply to some scenes. A module can leave this false from
        /// LoadDynamicAssets() so the next scene gets another load attempt.
        /// </summary>
        protected bool DynamicAssetLoadSucceeded { get; set; } = true;

        public bool TryLoadStaticAssets()
        {
            if (static_assets_loaded) return false;

            try
            {
                LoadStaticAssets();
                static_assets_loaded = true;
            }
            catch (Exception error)
            {
                MelonLoader.MelonLogger.Error("[M1A1] " + GetType().Name + " static assets failed: " + error);
                return false;
            }

            return true;
        }

        public bool TryLoadDynamicAssets()
        {
            if (dynamic_assets_loaded) return false;

            DynamicAssetLoadSucceeded = true;

            try
            {
                LoadDynamicAssets();
            }
            catch (Exception error)
            {
                MelonLoader.MelonLogger.Error("[M1A1] " + GetType().Name + " dynamic assets failed: " + error);
                DynamicAssetLoadSucceeded = false;

                // A module can create several objects before a later lookup fails.  The
                // module is still marked as not loaded, so without an explicit rollback
                // those partial objects (and any Addressables handles they own) survive
                // into the next scene and are loaded a second time.  Unload is deliberately
                // best-effort here: a cleanup failure must not hide the original load
                // failure or leave the module permanently marked as loaded.
                try
                {
                    UnloadDynamicAssets();
                }
                catch (Exception cleanupError)
                {
                    MelonLoader.MelonLogger.Warning("[M1A1] " + GetType().Name + " rollback failed: " + cleanupError);
                }
            }

            dynamic_assets_loaded = DynamicAssetLoadSucceeded;

            return dynamic_assets_loaded;
        }

        public bool TryUnloadDynamicAssets()
        {
            if (!dynamic_assets_loaded) return false;

            try
            {
                UnloadDynamicAssets();
            }
            catch (Exception error)
            {
                MelonLoader.MelonLogger.Warning("[M1A1] " + GetType().Name + " unload failed: " + error);
            }
            finally
            {
                dynamic_assets_loaded = false;
            }

            return true;
        }

        public virtual void LoadStaticAssets() { }
        public virtual void LoadDynamicAssets() { }

        // ASSETS THAT ARE INSTANTIATED DYNAMICALLY 
        // I.E. USING Object.Instantiate
        // MUST BE DESTROYED!
        public virtual void UnloadDynamicAssets() { }
    }
}
