// File: src/InstanciumLens.Core/Models/DecisionModels.cs
using System;
using System.Collections.Generic;
using System.Linq;

namespace InstanciumLens.Core.Models
{
    public enum ValidationSeverity
    {
        Info = 0,
        Warning = 1,
        Error = 2
    }

    public sealed record GraphRelationshipKey(
        string SourceEntityId,
        string TargetEntityId,
        string RelationType);

    public sealed class GraphChange
    {
        public IReadOnlyList<GraphEntity> AddedOrUpdatedNodes { get; init; } = Array.Empty<GraphEntity>();
        public IReadOnlyList<GraphRelationship> AddedOrUpdatedRelationships { get; init; } = Array.Empty<GraphRelationship>();
        public IReadOnlyList<string> RemovedNodeIds { get; init; } = Array.Empty<string>();
        public IReadOnlyList<GraphRelationshipKey> RemovedRelationships { get; init; } = Array.Empty<GraphRelationshipKey>();
    }

    public sealed class DecisionBranch
    {
        public string BranchId { get; init; } = string.Empty;
        public string ProfileId { get; init; } = string.Empty;
        public string FolderPath { get; init; } = string.Empty;
        public string DatabaseFilePath { get; init; } = string.Empty;
        public DateTime CreatedAtUtc { get; init; }
    }

    public sealed class ValidationViolation
    {
        public string RuleNodeId { get; init; } = string.Empty;
        public string? AffectedNodeId { get; init; }
        public ValidationSeverity Severity { get; init; }
        public string Code { get; init; } = string.Empty;
        public string Message { get; init; } = string.Empty;
        public IReadOnlyList<string> SourceDocumentNodeIds { get; init; } = Array.Empty<string>();
    }

    public sealed class ValidationReport
    {
        public ValidationReport(IReadOnlyList<ValidationViolation> violations)
        {
            Violations = violations ?? throw new ArgumentNullException(nameof(violations));
            UtcTimestamp = DateTime.UtcNow;
        }

        public IReadOnlyList<ValidationViolation> Violations { get; }
        public DateTime UtcTimestamp { get; }
        public bool HasErrors => Violations.Any(v => v.Severity == ValidationSeverity.Error);
        public bool IsValid => !HasErrors;
    }
}