// File: src/InstanciumLens.Core/Services/GraphValidationPipeline.cs
// [AI DIFF]: added for WBS stage 5 validation pipeline
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using InstanciumLens.Core.Interfaces;
using InstanciumLens.Core.Models;

namespace InstanciumLens.Core.Services
{
    public sealed class GraphValidationPipeline : IGraphValidationPipeline
    {
        private const char RuleSeparator = ';';
        private const int GraphWalkDepth = 2;

        private const string RequiredRelationshipPrefix = "required_relationship:";
        private const string ForbiddenRelationshipPrefix = "forbidden_relationship:";
        private const string MaxRelationshipsPrefix = "max_relationships:";
        private const string MinConfidencePrefix = "min_confidence:";
        private const string RequiresNodeTypePrefix = "requires_node_type:";

        private const string RequiredRelationshipCode = "required_relationship";
        private const string ForbiddenRelationshipCode = "forbidden_relationship";
        private const string MaxRelationshipsCode = "max_relationships";
        private const string MinConfidenceCode = "min_confidence";
        private const string RequiresNodeTypeCode = "requires_node_type";
        private const string ManualVerificationCode = "manual_verification";
        private const string InvalidRuleCode = "invalid_rule";
        private const string UnsupportedRuleCode = "unsupported_rule";

        public ValidationReport Validate(KnowledgeGraph graph, GraphChange change)
        {
            ArgumentNullException.ThrowIfNull(graph);
            ArgumentNullException.ThrowIfNull(change);

            HashSet<string> changedNodeIds = CollectChangedNodeIds(change);

            Dictionary<string, GraphEntity> entityLookup = graph.Entities
                .Where(entity => !string.IsNullOrWhiteSpace(entity.Id))
                .GroupBy(entity => entity.Id, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

            Dictionary<string, List<GraphRelationship>> relationshipsByNode = BuildRelationshipsByNode(graph.Relationships);
            Dictionary<string, HashSet<string>> neighbors = BuildNeighbors(graph.Relationships);

            List<GraphEntity> ruleNodes = graph.Entities
                .Where(entity => entity.NodeKind != GraphNodeKind.Entity)
                .Where(entity =>
                    changedNodeIds.Contains(entity.Id) ||
                    IsWithinChangedNeighborhood(entity.Id, changedNodeIds, neighbors, GraphWalkDepth))
                .ToList();

            var violations = new List<ValidationViolation>();
            var seenViolations = new HashSet<string>(StringComparer.Ordinal);

            foreach (GraphEntity ruleNode in ruleNodes)
            {
                List<string> affectedEntityIds = GetAffectedEntityIds(
                    ruleNode,
                    changedNodeIds,
                    relationshipsByNode,
                    entityLookup);

                if (affectedEntityIds.Count == 0)
                {
                    continue;
                }

                List<string> rules = ParseRules(ruleNode.RuleExpression);

                if (rules.Count == 0)
                {
                    if (ruleNode.NodeKind == GraphNodeKind.Requirement)
                    {
                        AddManualVerificationViolations(
                            ruleNode,
                            affectedEntityIds,
                            entityLookup,
                            violations,
                            seenViolations);
                    }

                    continue;
                }

                EvaluateRules(
                    ruleNode,
                    rules,
                    affectedEntityIds,
                    relationshipsByNode,
                    entityLookup,
                    violations,
                    seenViolations);
            }

            return new ValidationReport(violations);
        }

        private static HashSet<string> CollectChangedNodeIds(GraphChange change)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);

            foreach (GraphEntity node in change.AddedOrUpdatedNodes)
            {
                AddIfNotEmpty(result, node.Id);
            }

            foreach (string nodeId in change.RemovedNodeIds)
            {
                AddIfNotEmpty(result, nodeId);
            }

            foreach (GraphRelationship relationship in change.AddedOrUpdatedRelationships)
            {
                AddIfNotEmpty(result, relationship.SourceEntityId);
                AddIfNotEmpty(result, relationship.TargetEntityId);
            }

            foreach (GraphRelationshipKey key in change.RemovedRelationships)
            {
                AddIfNotEmpty(result, key.SourceEntityId);
                AddIfNotEmpty(result, key.TargetEntityId);
            }

            return result;
        }

        private static void AddIfNotEmpty(HashSet<string> target, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                target.Add(value);
            }
        }

        private static Dictionary<string, List<GraphRelationship>> BuildRelationshipsByNode(
            IEnumerable<GraphRelationship> relationships)
        {
            var result = new Dictionary<string, List<GraphRelationship>>(StringComparer.Ordinal);

            foreach (GraphRelationship relationship in relationships)
            {
                AddRelationship(result, relationship.SourceEntityId, relationship);

                if (!string.Equals(relationship.SourceEntityId, relationship.TargetEntityId, StringComparison.Ordinal))
                {
                    AddRelationship(result, relationship.TargetEntityId, relationship);
                }
            }

            return result;
        }

        private static void AddRelationship(
            Dictionary<string, List<GraphRelationship>> map,
            string nodeId,
            GraphRelationship relationship)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                return;
            }

            if (!map.TryGetValue(nodeId, out List<GraphRelationship>? relationships))
            {
                relationships = new List<GraphRelationship>();
                map[nodeId] = relationships;
            }

            relationships.Add(relationship);
        }

        private static Dictionary<string, HashSet<string>> BuildNeighbors(
            IEnumerable<GraphRelationship> relationships)
        {
            var result = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

            foreach (GraphRelationship relationship in relationships)
            {
                AddNeighbor(result, relationship.SourceEntityId, relationship.TargetEntityId);
                AddNeighbor(result, relationship.TargetEntityId, relationship.SourceEntityId);
            }

            return result;
        }

        private static void AddNeighbor(
            Dictionary<string, HashSet<string>> map,
            string nodeId,
            string neighborId)
        {
            if (string.IsNullOrWhiteSpace(nodeId) || string.IsNullOrWhiteSpace(neighborId))
            {
                return;
            }

            if (!map.TryGetValue(nodeId, out HashSet<string>? neighbors))
            {
                neighbors = new HashSet<string>(StringComparer.Ordinal);
                map[nodeId] = neighbors;
            }

            neighbors.Add(neighborId);
        }

        private static bool IsWithinChangedNeighborhood(
            string startNodeId,
            HashSet<string> changedNodeIds,
            Dictionary<string, HashSet<string>> neighbors,
            int maxDepth)
        {
            if (changedNodeIds.Contains(startNodeId))
            {
                return true;
            }

            var visited = new HashSet<string>(StringComparer.Ordinal)
            {
                startNodeId
            };

            var queue = new Queue<(string NodeId, int Depth)>();
            queue.Enqueue((startNodeId, 0));

            while (queue.Count > 0)
            {
                (string nodeId, int depth) = queue.Dequeue();

                if (depth >= maxDepth)
                {
                    continue;
                }

                if (!neighbors.TryGetValue(nodeId, out HashSet<string>? nextNodes))
                {
                    continue;
                }

                foreach (string neighborId in nextNodes)
                {
                    if (!visited.Add(neighborId))
                    {
                        continue;
                    }

                    if (changedNodeIds.Contains(neighborId))
                    {
                        return true;
                    }

                    queue.Enqueue((neighborId, depth + 1));
                }
            }

            return false;
        }

        private static List<string> GetAffectedEntityIds(
            GraphEntity ruleNode,
            HashSet<string> changedNodeIds,
            Dictionary<string, List<GraphRelationship>> relationshipsByNode,
            Dictionary<string, GraphEntity> entityLookup)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);

            if (relationshipsByNode.TryGetValue(ruleNode.Id, out List<GraphRelationship>? relationships))
            {
                foreach (GraphRelationship relationship in relationships)
                {
                    string otherNodeId = relationship.SourceEntityId == ruleNode.Id
                        ? relationship.TargetEntityId
                        : relationship.SourceEntityId;

                    if (entityLookup.TryGetValue(otherNodeId, out GraphEntity? otherNode) &&
                        otherNode.NodeKind == GraphNodeKind.Entity)
                    {
                        result.Add(otherNodeId);
                    }
                }
            }

            if (result.Count > 0)
            {
                return result.ToList();
            }

            foreach (string changedNodeId in changedNodeIds)
            {
                if (entityLookup.TryGetValue(changedNodeId, out GraphEntity? entity) &&
                    entity.NodeKind == GraphNodeKind.Entity)
                {
                    result.Add(changedNodeId);
                }
            }

            return result.ToList();
        }

        private static List<string> ParseRules(string? ruleExpression)
        {
            if (string.IsNullOrWhiteSpace(ruleExpression))
            {
                return new List<string>();
            }

            return ruleExpression
                .Split(RuleSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
        }

        private void EvaluateRules(
            GraphEntity ruleNode,
            List<string> rules,
            List<string> affectedEntityIds,
            Dictionary<string, List<GraphRelationship>> relationshipsByNode,
            Dictionary<string, GraphEntity> entityLookup,
            List<ValidationViolation> violations,
            HashSet<string> seenViolations)
        {
            foreach (string rule in rules)
            {
                foreach (string affectedEntityId in affectedEntityIds)
                {
                    string entityName = GetEntityName(entityLookup, affectedEntityId);
                    ValidationSeverity severity = GetSeverity(ruleNode);

                    if (rule.StartsWith(RequiredRelationshipPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        string relationType = GetArgument(rule, RequiredRelationshipPrefix);

                        if (!HasRelationship(affectedEntityId, relationType, relationshipsByNode))
                        {
                            AddViolation(
                                violations,
                                seenViolations,
                                ruleNode,
                                affectedEntityId,
                                severity,
                                RequiredRelationshipCode,
                                $"Node '{entityName}' must have at least one relationship of type '{relationType}'.");
                        }

                        continue;
                    }

                    if (rule.StartsWith(ForbiddenRelationshipPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        string relationType = GetArgument(rule, ForbiddenRelationshipPrefix);

                        if (HasRelationship(affectedEntityId, relationType, relationshipsByNode))
                        {
                            AddViolation(
                                violations,
                                seenViolations,
                                ruleNode,
                                affectedEntityId,
                                severity,
                                ForbiddenRelationshipCode,
                                $"Node '{entityName}' must not have any relationship of type '{relationType}'.");
                        }

                        continue;
                    }

                    if (rule.StartsWith(MaxRelationshipsPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        string[] parts = rule.Split(':', StringSplitOptions.TrimEntries);

                        if (parts.Length == 3 &&
                            int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int maxCount))
                        {
                            string relationType = parts[1];
                            int count = CountRelationships(affectedEntityId, relationType, relationshipsByNode);

                            if (count > maxCount)
                            {
                                AddViolation(
                                    violations,
                                    seenViolations,
                                    ruleNode,
                                    affectedEntityId,
                                    severity,
                                    MaxRelationshipsCode,
                                    $"Node '{entityName}' has {count} relationships of type '{relationType}', but the maximum allowed is {maxCount}.");
                            }
                        }
                        else
                        {
                            AddViolation(
                                violations,
                                seenViolations,
                                ruleNode,
                                affectedEntityId,
                                ValidationSeverity.Warning,
                                InvalidRuleCode,
                                $"Rule '{rule}' is invalid. Expected format: max_relationships:<RELATION_TYPE>:<integer>.");
                        }

                        continue;
                    }

                    if (rule.StartsWith(MinConfidencePrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        string thresholdText = GetArgument(rule, MinConfidencePrefix);

                        if (double.TryParse(thresholdText, NumberStyles.Float, CultureInfo.InvariantCulture, out double threshold))
                        {
                            if (entityLookup.TryGetValue(affectedEntityId, out GraphEntity? entity) &&
                                entity.ConfidenceScore < threshold)
                            {
                                AddViolation(
                                    violations,
                                    seenViolations,
                                    ruleNode,
                                    affectedEntityId,
                                    severity,
                                    MinConfidenceCode,
                                    $"Node '{entityName}' confidence {entity.ConfidenceScore.ToString(CultureInfo.InvariantCulture)} is below required threshold {threshold.ToString(CultureInfo.InvariantCulture)}.");
                            }
                        }
                        else
                        {
                            AddViolation(
                                violations,
                                seenViolations,
                                ruleNode,
                                affectedEntityId,
                                ValidationSeverity.Warning,
                                InvalidRuleCode,
                                $"Rule '{rule}' is invalid. Expected format: min_confidence:<double>.");
                        }

                        continue;
                    }

                    if (rule.StartsWith(RequiresNodeTypePrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        string requiredNodeType = GetArgument(rule, RequiresNodeTypePrefix);

                        if (!HasRelatedNodeType(affectedEntityId, requiredNodeType, relationshipsByNode, entityLookup))
                        {
                            AddViolation(
                                violations,
                                seenViolations,
                                ruleNode,
                                affectedEntityId,
                                severity,
                                RequiresNodeTypeCode,
                                $"Node '{entityName}' must be related to at least one node of type '{requiredNodeType}'.");
                        }

                        continue;
                    }

                    AddViolation(
                        violations,
                        seenViolations,
                        ruleNode,
                        affectedEntityId,
                        ValidationSeverity.Warning,
                        UnsupportedRuleCode,
                        $"Rule '{rule}' is not supported.");
                }
            }
        }

        private void AddManualVerificationViolations(
            GraphEntity ruleNode,
            List<string> affectedEntityIds,
            Dictionary<string, GraphEntity> entityLookup,
            List<ValidationViolation> violations,
            HashSet<string> seenViolations)
        {
            foreach (string affectedEntityId in affectedEntityIds)
            {
                string entityName = GetEntityName(entityLookup, affectedEntityId);

                AddViolation(
                    violations,
                    seenViolations,
                    ruleNode,
                    affectedEntityId,
                    GetSeverity(ruleNode),
                    ManualVerificationCode,
                    $"Requirement '{ruleNode.Name}' must be verified manually for node '{entityName}'.");
            }
        }

        private static string GetArgument(string rule, string prefix) =>
            rule[prefix.Length..].Trim();

        private static string GetEntityName(Dictionary<string, GraphEntity> entityLookup, string entityId) =>
            entityLookup.TryGetValue(entityId, out GraphEntity? entity)
                ? entity.Name
                : entityId;

        private static bool HasRelationship(
            string entityId,
            string relationType,
            Dictionary<string, List<GraphRelationship>> relationshipsByNode)
        {
            if (!relationshipsByNode.TryGetValue(entityId, out List<GraphRelationship>? relationships))
            {
                return false;
            }

            return relationships.Any(relationship =>
                string.Equals(relationship.RelationType, relationType, StringComparison.OrdinalIgnoreCase));
        }

        private static int CountRelationships(
            string entityId,
            string relationType,
            Dictionary<string, List<GraphRelationship>> relationshipsByNode)
        {
            if (!relationshipsByNode.TryGetValue(entityId, out List<GraphRelationship>? relationships))
            {
                return 0;
            }

            return relationships.Count(relationship =>
                string.Equals(relationship.RelationType, relationType, StringComparison.OrdinalIgnoreCase));
        }

        private static bool HasRelatedNodeType(
            string entityId,
            string requiredNodeType,
            Dictionary<string, List<GraphRelationship>> relationshipsByNode,
            Dictionary<string, GraphEntity> entityLookup)
        {
            if (!relationshipsByNode.TryGetValue(entityId, out List<GraphRelationship>? relationships))
            {
                return false;
            }

            foreach (GraphRelationship relationship in relationships)
            {
                string otherNodeId = relationship.SourceEntityId == entityId
                    ? relationship.TargetEntityId
                    : relationship.SourceEntityId;

                if (entityLookup.TryGetValue(otherNodeId, out GraphEntity? otherNode) &&
                    string.Equals(otherNode.Type, requiredNodeType, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static ValidationSeverity GetSeverity(GraphEntity ruleNode) =>
            ruleNode.Severity ??
            (ruleNode.NodeKind == GraphNodeKind.Constraint
                ? ValidationSeverity.Error
                : ValidationSeverity.Warning);

        private static void AddViolation(
            List<ValidationViolation> violations,
            HashSet<string> seenViolations,
            GraphEntity ruleNode,
            string? affectedNodeId,
            ValidationSeverity severity,
            string code,
            string message)
        {
            string key = string.Join(
                "|",
                ruleNode.Id,
                affectedNodeId ?? string.Empty,
                code,
                message);

            if (!seenViolations.Add(key))
            {
                return;
            }

            violations.Add(new ValidationViolation
            {
                RuleNodeId = ruleNode.Id,
                AffectedNodeId = affectedNodeId,
                Severity = severity,
                Code = code,
                Message = message,
                SourceDocumentNodeIds = ruleNode.SourceDocumentNodeIds?.ToList() ?? new List<string>()
            });
        }
    }
}