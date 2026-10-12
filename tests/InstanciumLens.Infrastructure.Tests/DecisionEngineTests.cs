// File: tests/InstanciumLens.Infrastructure.Tests/DecisionEngineTests.cs
// [AI DIFF]: added tests for branch isolation, conflict detection, and constraint rules
using InstanciumLens.Core.Interfaces;
using InstanciumLens.Core.Models;
using InstanciumLens.Core.Services;
using InstanciumLens.Infrastructure.Data;
using Microsoft.Data.Sqlite; // [AI DIFF]: required to release pooled handles before temp folder cleanup
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace InstanciumLens.Infrastructure.Tests
{
    public sealed class DecisionEngineTests : IDisposable
    {
        private const string ProfileId = "decision_test";
        private const string MasterEntityId = "entity-master";
        private const string BranchEntityId = "entity-branch";
        private const string EntityAId = "entity-a";
        private const string EntityBId = "entity-b";
        private const string ConstraintId = "constraint-node";
        private const string RequirementId = "requirement-node";

        private const string ForbiddenRelationType = "OWNS";
        private const string RequiredRelationType = "APPROVED_BY";
        private const string ConstraintLinkType = "CONSTRAINS";

        private const string ForbiddenRuleCode = "forbidden_relationship";
        private const string RequiredRuleCode = "required_relationship";
        private const string ManualVerificationCode = "manual_verification";

        private const double FullConfidence = 1.0;

        private readonly string _rootFolder;
        private readonly SqLiteGraphRepositoryFactory _factory;
        private readonly DecisionEngine _engine;

        public DecisionEngineTests()
        {
            _rootFolder = Path.Combine(
                Path.GetTempPath(),
                "InstanciumLensTests",
                Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(_rootFolder);

            _factory = new SqLiteGraphRepositoryFactory();
            _engine = new DecisionEngine(_factory, new GraphValidationPipeline());
        }

        public void Dispose()
        {
            // [AI DIFF]: pooled SQLite handles lock .db files on Windows; release them before recursive delete
            SqliteConnection.ClearAllPools();

            if (Directory.Exists(_rootFolder))
            {
                Directory.Delete(_rootFolder, recursive: true);
            }
        }

        [Fact]
        public async Task ApplyChangeToBranch_AddsNode_MasterRemainsUnchanged()
        {
            GraphProfile profile = CreateProfile();

            IGraphRepository masterRepository = _factory.CreateRepository(_rootFolder, profile.Id);
            await masterRepository.SaveGraphSegmentAsync(new KnowledgeGraph
            {
                Entities = new List<GraphEntity>
                {
                    CreateEntity(MasterEntityId, "Master Component", "Component")
                }
            });

            DecisionBranch branch = await _engine.CreateBranchAsync(_rootFolder, profile, "what-if");

            var change = new GraphChange
            {
                AddedOrUpdatedNodes = new[]
                {
                    CreateEntity(BranchEntityId, "Branch Component", "Component")
                }
            };

            ValidationReport report = await _engine.ApplyChangeToBranchAsync(branch, change);

            Assert.True(report.IsValid);

            IGraphRepository branchRepository = _factory.CreateBranchRepository(
                _rootFolder,
                profile.Id,
                branch.BranchId);

            KnowledgeGraph branchGraph = await branchRepository.LoadFullGraphAsync();
            KnowledgeGraph masterGraph = await masterRepository.LoadFullGraphAsync();

            Assert.Contains(branchGraph.Entities, entity => entity.Id == BranchEntityId);
            Assert.DoesNotContain(masterGraph.Entities, entity => entity.Id == BranchEntityId);
        }

        [Fact]
        public async Task ApplyChangeToBranch_ForbiddenRelationship_ProducesError()
        {
            GraphProfile profile = CreateProfile();

            IGraphRepository masterRepository = _factory.CreateRepository(_rootFolder, profile.Id);

            GraphEntity entityA = CreateEntity(EntityAId, "Component A", "Component");
            GraphEntity entityB = CreateEntity(EntityBId, "Component B", "Component");

            GraphEntity constraint = CreateEntity(
                ConstraintId,
                "No ownership",
                "Policy",
                GraphNodeKind.Constraint,
                $"forbidden_relationship:{ForbiddenRelationType}");

            await masterRepository.SaveGraphSegmentAsync(new KnowledgeGraph
            {
                Entities = new List<GraphEntity>
                {
                    entityA,
                    entityB,
                    constraint
                },
                Relationships = new List<GraphRelationship>
                {
                    new()
                    {
                        SourceEntityId = constraint.Id,
                        TargetEntityId = entityA.Id,
                        RelationType = ConstraintLinkType
                    }
                }
            });

            DecisionBranch branch = await _engine.CreateBranchAsync(_rootFolder, profile, "forbidden");

            var change = new GraphChange
            {
                AddedOrUpdatedRelationships = new[]
                {
                    new GraphRelationship
                    {
                        SourceEntityId = entityA.Id,
                        TargetEntityId = entityB.Id,
                        RelationType = ForbiddenRelationType
                    }
                }
            };

            ValidationReport report = await _engine.ApplyChangeToBranchAsync(branch, change);

            Assert.True(report.HasErrors);
            Assert.Contains(
                report.Violations,
                violation => violation.Code == ForbiddenRuleCode && violation.AffectedNodeId == EntityAId);
        }

        [Fact]
        public async Task ApplyChangeToBranch_RequiredRelationshipMissing_ProducesError()
        {
            GraphProfile profile = CreateProfile();

            IGraphRepository masterRepository = _factory.CreateRepository(_rootFolder, profile.Id);

            GraphEntity entityA = CreateEntity(EntityAId, "Component A", "Component");

            GraphEntity constraint = CreateEntity(
                ConstraintId,
                "Approval required",
                "Policy",
                GraphNodeKind.Constraint,
                $"required_relationship:{RequiredRelationType}");

            await masterRepository.SaveGraphSegmentAsync(new KnowledgeGraph
            {
                Entities = new List<GraphEntity>
                {
                    entityA,
                    constraint
                },
                Relationships = new List<GraphRelationship>
                {
                    new()
                    {
                        SourceEntityId = constraint.Id,
                        TargetEntityId = entityA.Id,
                        RelationType = ConstraintLinkType
                    }
                }
            });

            DecisionBranch branch = await _engine.CreateBranchAsync(_rootFolder, profile, "required");

            var change = new GraphChange
            {
                AddedOrUpdatedNodes = new[]
                {
                    entityA
                }
            };

            ValidationReport report = await _engine.ApplyChangeToBranchAsync(branch, change);

            Assert.True(report.HasErrors);
            Assert.Contains(
                report.Violations,
                violation => violation.Code == RequiredRuleCode && violation.AffectedNodeId == EntityAId);
        }

        [Fact]
        public async Task ApplyChangeToBranch_RequirementWithoutRule_ProducesManualVerificationWarning()
        {
            GraphProfile profile = CreateProfile();

            IGraphRepository masterRepository = _factory.CreateRepository(_rootFolder, profile.Id);

            GraphEntity entityA = CreateEntity(EntityAId, "Component A", "Component");

            GraphEntity requirement = CreateEntity(
                RequirementId,
                "GOST requirement",
                "GOST",
                GraphNodeKind.Requirement);

            await masterRepository.SaveGraphSegmentAsync(new KnowledgeGraph
            {
                Entities = new List<GraphEntity>
                {
                    entityA,
                    requirement
                },
                Relationships = new List<GraphRelationship>
                {
                    new()
                    {
                        SourceEntityId = requirement.Id,
                        TargetEntityId = entityA.Id,
                        RelationType = ConstraintLinkType
                    }
                }
            });

            DecisionBranch branch = await _engine.CreateBranchAsync(_rootFolder, profile, "manual");

            var change = new GraphChange
            {
                AddedOrUpdatedNodes = new[]
                {
                    entityA
                }
            };

            ValidationReport report = await _engine.ApplyChangeToBranchAsync(branch, change);

            Assert.False(report.HasErrors);
            Assert.Contains(
                report.Violations,
                violation =>
                    violation.Code == ManualVerificationCode &&
                    violation.AffectedNodeId == EntityAId &&
                    violation.Severity == ValidationSeverity.Warning);
        }

        [Fact]
        public async Task DeleteBranch_RemovesBranchDatabaseFile()
        {
            GraphProfile profile = CreateProfile();

            DecisionBranch branch = await _engine.CreateBranchAsync(_rootFolder, profile, "delete");

            Assert.True(File.Exists(branch.DatabaseFilePath));

            await _engine.DeleteBranchAsync(branch);

            Assert.False(File.Exists(branch.DatabaseFilePath));
        }

        private static GraphProfile CreateProfile()
        {
            return new GraphProfile
            {
                Id = ProfileId,
                DisplayName = "Decision Test",
                Description = "Profile used by decision engine tests.",
                DatabaseFile = $"knowledge_graph_{ProfileId}.db",
                CreatedAt = DateTime.UtcNow,
                Icon = "🧪"
            };
        }

        private static GraphEntity CreateEntity(
            string id,
            string name,
            string type,
            GraphNodeKind kind = GraphNodeKind.Entity,
            string? ruleExpression = null)
        {
            return new GraphEntity
            {
                Id = id,
                Name = name,
                Type = type,
                Description = $"{name} description",
                NodeKind = kind,
                RuleExpression = ruleExpression,
                ConfidenceScore = FullConfidence,
                SourceDocumentNodeIds = new List<string>
                {
                    "document-1"
                }
            };
        }
    }
}