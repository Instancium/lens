using System;

namespace InstanciumLens.AI.Models
{
    /// <summary>
    /// Represents the real-time stream state of a model weight download.
    /// Used by ModelProvisioning to report progress to the Blazor UI.
    /// </summary>
    /// <param name="FileName">The name of the GGUF file being downloaded.</param>
    /// <param name="TotalBytes">The total size of the file in bytes (or -1 if unknown).</param>
    /// <param name="BytesDownloaded">The amount of bytes successfully written to disk.</param>
    /// <param name="Percent">The completion percentage (0.0 to 100.0).</param>
    /// <param name="IsComplete">Flag indicating whether the download has finished successfully.</param>
    public record DownloadProgress(
        string FileName,
        long TotalBytes,
        long BytesDownloaded,
        double Percent,
        bool IsComplete
    );
}