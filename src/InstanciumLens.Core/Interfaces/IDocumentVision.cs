using InstanciumLens.Core.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace InstanciumLens.Core.Interfaces
{
    public interface IDocumentVision
    {
        Task<int> GetPageCountAsync(string filePath, CancellationToken cancellationToken = default);

        Task<IEnumerable<DocumentNode>> ExtractTextAsync(
            string filePath,
            CancellationToken cancellationToken = default,
            IProgress<DocumentPageProgress>? pageProgress = null);

        Task<DocumentNode> ExtractPageAsync(string filePath, int pageNumber, CancellationToken cancellationToken = default);
    }
}