using System;

namespace InstanciumLens.Core.Interfaces
{
    /// <summary>
    /// Creates graph repositories bound to a workspace folder and manages branch database files.
    /// </summary>
    public interface IGraphRepositoryFactory
    {
        /// <summary>
        /// Creates a repository over the master graph database of the profile.
        /// </summary>
        IGraphRepository CreateRepository(string targetFolderPath, string profileId);

        /// <summary>
        /// Creates a repository over an isolated branch database, seeding it from the master on first access.
        /// </summary>
        IGraphRepository CreateBranchRepository(string targetFolderPath, string profileId, string branchId);

        /// <summary>
        /// Deletes the branch database file of the profile.
        /// </summary>
        void DeleteBranch(string targetFolderPath, string profileId, string branchId);

        void DeleteMaster(string targetFolderPath, string profileId);

        /// <summary>
        /// Returns the absolute path of the master graph database file.
        /// </summary>
        string GetMasterDatabasePath(string targetFolderPath, string profileId);

        /// <summary>
        /// Returns the absolute path of the branch graph database file.
        /// </summary>
        string GetBranchDatabasePath(string targetFolderPath, string profileId, string branchId);
    }
}