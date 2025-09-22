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

        }

        private void HandleGameLoaded(object sender, EventArgs e)
        {

        }

        private void HandleGameBeforeNewGame(object sender, EventArgs e)
        {

        }

        private void HandleGameBeforeLoad(object sender, EventArgs e)
        {

        }

        private void HandleGameBeforeDelete(object sender, EventArgs e)
        {

        }

        private void HandleGameAfterDelete(object sender, EventArgs e)
        {

        }
    }
}