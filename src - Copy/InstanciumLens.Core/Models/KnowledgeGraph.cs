using System;
using System.Collections.Generic;
using System.Text;

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
        public string Type { get; set; } = string.Empty; // e.g., "Person", "Organization", "Contract"
        public string Description { get; set; } = string.Empty;

        // References to document nodes where this entity was found (for UI proofing)
        public List<string> SourceDocumentNodeIds { get; set; } = new();
    }

    public class GraphRelationship
    {
        public string SourceEntityId { get; set; } = string.Empty;
        public string TargetEntityId { get; set; } = string.Empty;
        public string RelationType { get; set; } = string.Empty; // e.g., "OWNS", "SIGNATORY_OF", "WORKS_AT"
        public string Description { get; set; } = string.Empty;
        public double Weight { get; set; } = 1.0;
    }
}
