using InstanciumLens.Core.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace InstanciumLens.Core.Interfaces
{
    public interface ILocalModelService
    {
  
        /// <summary>
        /// Smart Interview: Analyzes user intent and generates a custom GraphProfile and JSON Ontology schema.
        /// </summary>
        /// <param name="userIntent">The natural language intent provided by the user.</param>
        /// <param name="cancellationToken">Token to cancel the generation.</param>
        /// <returns>A tuple containing the UI profile and the raw JSON schema for the extraction phase.</returns>
        Task<(GraphProfile Profile, string OntologySchemaJson)> GenerateOntologyProfileAsync(string userIntent, CancellationToken cancellationToken = default);

        /// <summary>
        /// Pass 2: Uses the local LLM to extract entities and relationships based on a dynamic ontology schema.
        /// </summary>
        /// <param name="document">The document node containing raw text.</param>
        /// <param name="ontologySchemaJson">JSON structure defining what to search for (e.g., Legal, Engineering).</param>
        /// <param name="cancellationToken">Token to cancel the offline processing.</param>
        /// <returns>A KnowledgeGraph segment containing found entities and relationships.</returns>
        Task<KnowledgeGraph> ExtractEntitiesAsync(DocumentNode document, string ontologySchemaJson, CancellationToken cancellationToken = default);

        /// <summary>
        /// Answers the user query using the pre-built knowledge graph context.
        /// </summary>
        /// <param name="userQuery">The question asked in the chat UI.</param>
        /// <param name="contextGraph">The relevant subgraph pulled from RAM/SQLite.</param>
        /// <param name="cancellationToken">Token to cancel the generation.</param>
        /// <returns>The generated response text.</returns>
        Task<string> AnswerQueryAsync(string userQuery, KnowledgeGraph contextGraph, CancellationToken cancellationToken = default);
    }
}
