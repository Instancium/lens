using InstanciumLens.Core.Models;


namespace InstanciumLens.Core.Interfaces
{
    public interface IGraphRagManager
    {
        /// <summary>
        /// Orchestrates the end-to-end processing of all documents in a folder.
        /// </summary>
        Task BuildGraphForFolderAsync(
            string folderPath,
            GraphProfile profile,
            Action<ProcessingState> progressCallback,
            CancellationToken cancellationToken = default);
    }
}

