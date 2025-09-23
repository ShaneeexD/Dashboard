using SOD.Common;
using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityStandardAssets.Characters.FirstPerson;
using HarmonyLib;

namespace Dashboard
{
    public class SaveGameHandlers : MonoBehaviour
    {
        public SaveGameHandlers()
        {       
            Lib.SaveGame.OnAfterLoad += HandleGameLoaded;
            Lib.SaveGame.OnAfterNewGame += HandleNewGameStarted;
            Lib.SaveGame.OnBeforeNewGame += HandleGameBeforeNewGame;
            Lib.SaveGame.OnBeforeLoad += HandleGameBeforeLoad;
            Lib.SaveGame.OnBeforeDelete += HandleGameBeforeDelete;
            Lib.SaveGame.OnAfterDelete += HandleGameAfterDelete;
        }

        private void HandleNewGameStarted(object sender, EventArgs e)
        {
            try
            {
                NpcCache.ClearPhotoCache();
                NpcCache.RebuildFromGame();
            }
            catch (Exception ex)
            {
                ModLogger.Error($"Error during HandleNewGameStarted NPC build: {ex}");
            }
        }

        private void HandleGameLoaded(object sender, EventArgs e)
        {
            try
            {
                NpcCache.ClearPhotoCache();
                NpcCache.RebuildFromGame();
            }
            catch (Exception ex)
            {
                ModLogger.Error($"Error during HandleGameLoaded NPC build: {ex}");
            }
        }

        private void HandleGameBeforeNewGame(object sender, EventArgs e)
        {
            // Flag cache as not ready; it will be rebuilt after the new game starts
            // Clear photo cache to avoid stale images
            NpcCache.ClearPhotoCache();
        }

        private void HandleGameBeforeLoad(object sender, EventArgs e)
        {
            // Flag cache as not ready while loading
            // Clear photo cache to avoid stale images
            NpcCache.ClearPhotoCache();
        }

        private void HandleGameBeforeDelete(object sender, EventArgs e)
        {
            // No-op for now
        }

        private void HandleGameAfterDelete(object sender, EventArgs e)
        {
            // No-op for now
        }
    }
}