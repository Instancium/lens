// File: src/InstanciumLens.Infrastructure/Data/SqLiteGraphRepositoryFactory.cs
using System;
using System.IO;
using System.Linq;
using InstanciumLens.Core.Interfaces;
using Microsoft.Data.Sqlite;

namespace InstanciumLens.Infrastructure.Data
{
    public class SqLiteGraphRepositoryFactory : IGraphRepositoryFactory
    {
        public IGraphRepository CreateRepository(string targetFolderPath, string profileId)
        {
            ValidateFolderPath(targetFolderPath);

            return new SqLiteGraphRepository(targetFolderPath, profileId);
        }
        public IGraphRepository CreateBranchRepository(string targetFolderPath, string profileId, string branchId)
        {
            ValidateFolderPath(targetFolderPath);

            string branchPath = GetBranchDatabasePath(targetFolderPath, profileId, branchId);
            string? branchFolder = Path.GetDirectoryName(branchPath);

            if (!string.IsNullOrEmpty(branchFolder))
            {
                Directory.CreateDirectory(branchFolder);
            }

            string masterPath = GetMasterDatabasePath(targetFolderPath, profileId);

            if (!File.Exists(branchPath) && File.Exists(masterPath))
            {
                File.Copy(masterPath, branchPath, overwrite: true);
            }

            return new SqLiteGraphRepository(targetFolderPath, profileId, branchId);
        }
        public void DeleteBranch(string targetFolderPath, string profileId, string branchId)
        {
            ValidateFolderPath(targetFolderPath);

            string branchPath = GetBranchDatabasePath(targetFolderPath, profileId, branchId);
            ClearConnectionPool(branchPath);

            if (File.Exists(branchPath))
            {
                File.Delete(branchPath);
            }
        }
        private static void ClearConnectionPool(string databasePath)
        {
            using var connection = new SqliteConnection($"Data Source={databasePath}");
            SqliteConnection.ClearPool(connection);
        }

        public string GetMasterDatabasePath(string targetFolderPath, string profileId)
        {
            ValidateFolderPath(targetFolderPath);

            string safeProfileId = SanitizeName(profileId);
            return Path.Combine(targetFolderPath, ".instancium", $"knowledge_graph_{safeProfileId}.db");
        }

        public string GetBranchDatabasePath(string targetFolderPath, string profileId, string branchId)
        {
            ValidateFolderPath(targetFolderPath);

            string safeProfileId = SanitizeName(profileId);
            string safeBranchId = SanitizeName(branchId);

            return Path.Combine(
                targetFolderPath,
                ".instancium",
                "branches",
                safeProfileId,
                $"{safeBranchId}.db");
        }

        private static void ValidateFolderPath(string targetFolderPath)
        {
            if (string.IsNullOrWhiteSpace(targetFolderPath))
            {
                throw new ArgumentNullException(nameof(targetFolderPath));
            }
        }

        private static string SanitizeName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "default";
            }

            char[] invalidChars = Path.GetInvalidFileNameChars();

            string result = new string(value
                .Select(ch => invalidChars.Contains(ch) || char.IsWhiteSpace(ch)
                    ? '_'
                    : char.ToLowerInvariant(ch))
                .ToArray());

            return string.IsNullOrWhiteSpace(result) ? "default" : result;
        }
    }
}