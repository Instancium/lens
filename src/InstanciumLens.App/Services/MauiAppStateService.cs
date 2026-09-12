using InstanciumLens.Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Text;

namespace InstanciumLens.App.Services
{
    public class MauiAppStateService : IAppStateService
    {
        private const string LastFolderKey = "Instancium_LastOpenedFolder";

        public string? CurrentFolderPath { get; private set; }

        public MauiAppStateService()
        {
            // Restore the last used folder automatically upon app startup
            CurrentFolderPath = Preferences.Default.Get(LastFolderKey, (string?)null);
        }

        public void SetCurrentFolder(string path)
        {
            CurrentFolderPath = path;
            Preferences.Default.Set(LastFolderKey, path); // Persist to OS storage
        }

        public void ClearCurrentFolder()
        {
            CurrentFolderPath = null;
            Preferences.Default.Remove(LastFolderKey);
        }
    }
}
