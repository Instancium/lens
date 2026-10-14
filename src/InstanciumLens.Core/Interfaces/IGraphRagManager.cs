using InstanciumLens.Core.Models;

namespace InstanciumLens.Core.Interfaces
{
    public interface IGraphRagManager
    {
        Task BuildGraphForFolderAsync(
            string folderPath,
            GraphProfile profile,
            Action<ProcessingState> progressCallback,
            bool rebuildFromScratch = false,
            CancellationToken cancellationToken = default);
    }
}