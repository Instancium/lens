using InstanciumLens.Core.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace InstanciumLens.Core.Interfaces
{
    /// <summary>Abstraction over the AI completion backend used by Core orchestration.</summary>
    public interface ILocalModelService
    {
        /// <summary>Generates an ontology profile and schema JSON from a free-form user intent.</summary>
        Task<(GraphProfile Profile, string OntologySchemaJson)> GenerateOntologyProfileAsync(string userIntent, CancellationToken cancellationToken = default);

        /// <summary>Extracts entities and relationships from a single document node.</summary>
        Task<KnowledgeGraph> ExtractEntitiesAsync(DocumentNode document, string ontologySchemaJson, CancellationToken cancellationToken = default);

        /// <summary>Answers a user query grounded in the provided knowledge graph context.</summary>
        Task<string> AnswerQueryAsync(string userQuery, KnowledgeGraph contextGraph, CancellationToken cancellationToken = default);

        /// <summary>Streams a grounded answer with numbered [n] citation markers driven by the citation context.</summary>
        IAsyncEnumerable<string> AnswerQueryStreamAsync(string userQuery, CitationContext citationContext, CancellationToken cancellationToken = default);
    }
}