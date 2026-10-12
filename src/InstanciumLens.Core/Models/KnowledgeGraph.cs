// File: src/InstanciumLens.Core/Models/KnowledgeGraph.cs
#nullable enable
using System;
using System.Collections.Generic;

namespace InstanciumLens.Core.Models
{
    public class KnowledgeGraph
    {
        public List<GraphEntity> Entities { get; set; } = new();
        public List<GraphRelationship> Relationships { get; set; } = new();
    }

    public class GraphEntity
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public List<string> SourceDocumentNodeIds { get; set; } = new();
        public double ConfidenceScore { get; set; } = 1.0;
        public GraphNodeKind NodeKind { get; set; } = GraphNodeKind.Entity;
        // Supported examples:
        // required_relationship:APPROVED_BY
        // forbidden_relationship:OWNS
        // max_relationships:OWNS:1
        // min_confidence:0.8
        // requires_node_type:Contract
        public string? RuleExpression { get; set; }
        public string? Standard { get; set; }
        public ValidationSeverity? Severity { get; set; }
        public string? SourceText { get; set; }
    }

    public class GraphRelationship
    {
        public string SourceEntityId { get; set; } = string.Empty;
        public string TargetEntityId { get; set; } = string.Empty;
        public string RelationType { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public double Weight { get; set; } = 1.0;
    }
}