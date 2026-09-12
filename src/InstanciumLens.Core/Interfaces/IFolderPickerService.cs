using System;
using System.Collections.Generic;
using System.Text;

namespace InstanciumLens.Core.Interfaces
{
    public interface IFolderPickerService
    {
        /// <summary>
        /// Opens the native OS folder picker dialogue.
        /// </summary>
        /// <returns>The absolute path to the selected folder, or null if canceled.</returns>
        Task<string?> PickFolderAsync();
    }
}
