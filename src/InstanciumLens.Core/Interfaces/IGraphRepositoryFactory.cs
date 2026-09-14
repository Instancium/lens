using System;
using System.Collections.Generic;
using System.Text;

namespace InstanciumLens.Core.Interfaces
{
    public interface IGraphRepositoryFactory
    {
        /// <summary>
        /// Creates a dynamically named graph repository based on the active profile.
        /// </summary>
        IGraphRepository CreateRepository(string targetFolderPath, string profileId);
    }
}
