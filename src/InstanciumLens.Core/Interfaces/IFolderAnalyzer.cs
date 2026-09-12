using InstanciumLens.Core.Models;

namespace InstanciumLens.Core.Interfaces
{
    public interface IFolderAnalyzer
    {
        Task<FolderStatistics> AnalyzeAsync(string folderPath);
    }
}
