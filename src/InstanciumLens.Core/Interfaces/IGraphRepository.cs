using InstanciumLens.Core.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace InstanciumLens.Core.Interfaces
{
    public interface IGraphRepository
    {
        Task SaveGraphSegmentAsync(KnowledgeGraph graphSegment, CancellationToken cancellationToken = default);
        Task<KnowledgeGraph> LoadFullGraphAsync(CancellationToken cancellationToken = default);
        Task<KnowledgeGraph> GetRelevantSubGraphAsync(IEnumerable<string> keywords, CancellationToken cancellationToken = default);
        Task SaveChangesAsync(GraphChange change, CancellationToken cancellationToken = default);

        /// <summary>
        /// Loads all per-page checkpoint rows stored for this graph database.
        /// </summary>
        Task<PipelineCheckpointSnapshot> LoadCheckpointSnapshotAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Persists the Pass 1 result (OCR text) of one page and invalidates its Pass 2 state.
        /// </summary>
        Task SavePagePass1Async(PageCheckpoint page, CancellationToken cancellationToken = default);

        /// <summary>
        /// Replaces the graph segment of one page and marks the page Pass 2 complete in a single transaction.
        /// </summary>
        Task SavePageSegmentAsync(KnowledgeGraph pageSegment, PageCheckpoint page, CancellationToken cancellationToken = default);

        /// <summary>
        /// Drops pipeline checkpoints and optionally the whole graph content for a full rebuild.
        /// </summary>
        Task ClearPipelineAsync(bool clearGraph, CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns the total number of entity rows currently stored in the graph database.
        /// </summary>
        Task<int> CountEntitiesAsync(CancellationToken cancellationToken = default);
    }
}