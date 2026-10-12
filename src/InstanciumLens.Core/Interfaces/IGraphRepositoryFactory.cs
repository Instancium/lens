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

        // [AI DIFF]: creates or opens isolated what-if branch repository
        IGraphRepository CreateBranchRepository(string targetFolderPath, string profileId, string branchId);

        // [AI DIFF]: deletes isolated what-if branch database file
        void DeleteBranch(string targetFolderPath, string profileId, string branchId);

        // [AI DIFF]: exposes physical master database path for diagnostics and branch lifecycle
        string GetMasterDatabasePath(string targetFolderPath, string profileId);

        // [AI DIFF]: exposes physical branch database path for diagnostics and branch lifecycle
        string GetBranchDatabasePath(string targetFolderPath, string profileId, string branchId);
    }
}
