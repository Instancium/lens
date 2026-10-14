using System;

namespace InstanciumLens.Core.Models
{
    /// <summary>
    /// Per-page Pass 1 progress reported by the vision layer while a PDF is being rendered and OCR-processed.
    /// </summary>
    public sealed record DocumentPageProgress(string FileName, int PageNumber, int TotalPages);
}