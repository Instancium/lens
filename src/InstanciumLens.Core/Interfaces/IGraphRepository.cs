using InstanciumLens.Core.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace InstanciumLens.Core.Interfaces
{
    public interface IGraphRepository
    {
        /// <summary>
        /// Pass 3: Saves the extracted graph segment into the local database.
        /// </summary>
        /// <param name="graphSegment">The graph segment containing new entities and relationships.</param>
        /// <param name="cancellationToken">Token to cancel the database operation.</param>
        Task SaveGraphSegmentAsync(KnowledgeGraph graphSegment, CancellationToken cancellationToken = default);

        /// <summary>
        /// Loads the entire knowledge graph into RAM for ultra-fast query resolution.
        /// </summary>
        /// <param name="cancellationToken">Token to cancel the database operation.</param>
        /// <returns>The complete KnowledgeGraph.</returns>
        Task<KnowledgeGraph> LoadFullGraphAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Searches the graph for specific entities related to the user's intent.
        /// </summary>
        /// <param name="keywords">Extracted keywords or intent tags from the user's query.</param>
        /// <param name="cancellationToken">Token to cancel the search operation.</param>
        /// <returns>A focused sub-graph containing only relevant nodes and edges.</returns>
        Task<KnowledgeGraph> GetRelevantSubGraphAsync(IEnumerable<string> keywords, CancellationToken cancellationToken = default);

        // [AI DIFF]: applies explicit graph mutations for master or sandbox branch
        Task SaveChangesAsync(GraphChange change, CancellationToken cancellationToken = default);
    }
}
