using System;
using System.Collections.Generic;
using System.Text;

namespace InstanciumLens.Core.Interfaces
{
    public interface IAppStateService
    {
        string? CurrentFolderPath { get; }
        void SetCurrentFolder(string path);
        void ClearCurrentFolder();
    }
}
