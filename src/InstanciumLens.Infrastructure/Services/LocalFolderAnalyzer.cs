using System;
using System.IO;
using System.Threading.Tasks;
using InstanciumLens.Core.Interfaces;
using InstanciumLens.Core.Models;

namespace InstanciumLens.Infrastructure.Services
{
    public class LocalFolderAnalyzer : IFolderAnalyzer
    {
        public Task<FolderStatistics> AnalyzeAsync(string folderPath)
        {
            return Task.Run(() =>
            {
                var stats = new FolderStatistics();
                var rootDirectory = new DirectoryInfo(folderPath);

                AnalyzeDirectorySafe(rootDirectory, stats);

                return stats;
            });
        }

        private void AnalyzeDirectorySafe(DirectoryInfo directory, FolderStatistics stats)
        {
            try
            {
                // 1. Count PDFs and size in the current specific directory
                var files = directory.GetFiles("*.pdf");
                stats.FileCount += files.Length;

                foreach (var file in files)
                {
                    stats.TotalSizeBytes += file.Length;
                }

                // 2. Get subdirectories
                var subDirs = directory.GetDirectories();
                stats.SubfolderCount += subDirs.Length;

                // 3. Recursively dive into subdirectories safely
                foreach (var subDir in subDirs)
                {
                    // Skip hidden or system directories to avoid access errors and save time
                    if ((subDir.Attributes & FileAttributes.Hidden) == FileAttributes.Hidden ||
                        (subDir.Attributes & FileAttributes.System) == FileAttributes.System)
                    {
                        continue;
                    }

                    AnalyzeDirectorySafe(subDir, stats);
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Silently ignore folders the OS won't let us read (e.g., protected system links)
            }
            catch (Exception)
            {
                // Silently ignore other IO anomalies (e.g., path too long)
            }
        }
    }
}