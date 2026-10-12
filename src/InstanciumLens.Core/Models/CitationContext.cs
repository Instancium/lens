using System;
using System.Collections.Generic;
using System.Linq;

namespace InstanciumLens.Core.Models
{
    /// <summary>Single numbered fact that can be cited by the model via [n] markers.</summary>
    public class CitationFact
    {
        /// <summary>One-based footnote index used in prompt and answer markers.</summary>
        public int Index { get; set; }

        /// <summary>Identifier of the backing entity node in the knowledge graph.</summary>
        public string EntityId { get; set; } = string.Empty;

        /// <summary>Human readable fact label shown in footnotes and drawer.</summary>
        public string Label { get; set; } = string.Empty;

        /// <summary>Extraction reliability in range [0..1].</summary>
        public double ConfidenceScore { get; set; } = 1.0;

        /// <summary>Document node identifiers that back this fact.</summary>
        public List<string> SourceDocumentNodeIds { get; set; } = new();
    }

    /// <summary>Numbered citation context shared between prompt builder and chat footnote UI.</summary>
    public class CitationContext
    {
        /// <summary>Ordered facts available for citation.</summary>
        public List<CitationFact> Facts { get; set; } = new();

        /// <summary>Numbered fact list injected into the system prompt for [n] marker grounding.</summary>
        public string PromptText =>
            string.Join("\n", Facts.Select(f => $"[{f.Index}] {f.Label} (confidence {f.ConfidenceScore:P0})"));

    }

    /// <summary>Pure builder that converts a subgraph into a numbered citation context.</summary>
    public static class CitationContextBuilder
    {
        /// <summary>Selects the most confident entities and assigns stable footnote indexes.</summary>
        public static CitationContext Build(KnowledgeGraph graph, int maxFacts = 10)
        {
            var context = new CitationContext();
            if (graph?.Entities == null)
            {
                return context;
            }

            int index = 1;
            foreach (var entity in graph.Entities
                .OrderByDescending(e => e.ConfidenceScore)
                .Take(maxFacts))
            {
                context.Facts.Add(new CitationFact
                {
                    Index = index++,
                    EntityId = entity.Id,
                    Label = $"{entity.Type}: {entity.Name}",
                    ConfidenceScore = entity.ConfidenceScore,
                    SourceDocumentNodeIds = entity.SourceDocumentNodeIds?.ToList() ?? new List<string>()
                });
            }

            return context;
        }
    }
}