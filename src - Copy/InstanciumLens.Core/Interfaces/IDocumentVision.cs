using InstanciumLens.Core.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace InstanciumLens.Core.Interfaces
{
    public interface IDocumentVision
    {
        /// <summary>
        /// Pass 1: Renders PDF pages and extracts raw text using native Windows APIs.
        /// </summary>
        /// <param name="filePath">Absolute path to the local PDF file.</param>
        /// <param name="cancellationToken">Token to cancel the offline processing.</param>
        /// <returns>A collection of DocumentNode representing each page.</returns>
        Task<IEnumerable<DocumentNode>> ExtractTextAsync(string filePath, CancellationToken cancellationToken = default);
    }
}
