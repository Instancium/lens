using InstanciumLens.Core.Models;
using InstanciumLens.Core.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace InstanciumLens.Infrastructure.Tests
{
    public class GraphRagManagerTests
    {
        private const string ProfileId = "test_profile";
        private const string DocumentName = "sample.pdf";
        private const string SchemaJson =
            "{ \"Entities\": [{ \"Type\": \"TestEntity\", \"Description\": \"Unit test entity\" }], \"Relationships\": [] }";
        private const string PlaceholderSchemaJson =
            "{ \"Entities\": [{ \"Type\": \"TargetEntity\", \"Description\": \"What it represents\" }], \"Relationships\": [] }";
        private const int PageCount = 3;
        private const int FailingPage = 2;
        private const int ExpectedDocumentCount = 1;
        private const int ExpectedEntitiesAfterResume = 2;
        private const string ZeroEntitiesMarker = "Graph is empty after build";

        [Fact]
        public async Task BuildGraphForFolderAsync_ValidInputs_SavesAllPagesAndUpdatesManifest()
        {
            string workspace = CreateWorkspace(SchemaJson);
            try
            {
                var vision = new FakeDocumentVision(BuildPageTexts());
                var ai = new FakeAiService();
                var factory = new FakeGraphRepositoryFactory();
                var manager = new GraphRagManager(vision, ai, factory);
                ProcessingState finalState = new();

                await manager.BuildGraphForFolderAsync(workspace, BuildProfile(), state => finalState = state);

                FakeGraphRepository repository = factory.GetCreatedRepository(workspace, ProfileId);
                Assert.Equal(ProcessingPhase.Completed, finalState.CurrentPhase);
                Assert.Equal(100, finalState.ProgressPercentage);
                Assert.Equal(PageCount, finalState.ExtractedEntities);
                Assert.Equal(PageCount, repository.Entities.Count);
                Assert.Equal(ExpectedDocumentCount, ReadManifestDocumentCount(workspace));
            }
            finally
            {
                Directory.Delete(workspace, recursive: true);
            }
        }

        [Fact]
        public async Task BuildGraphForFolderAsync_CrashDuringPass2_ResumesOnlyMissingPages()
        {
            string workspace = CreateWorkspace(SchemaJson);
            try
            {
                var factory = new FakeGraphRepositoryFactory();
                var crashedManager = new GraphRagManager(
                    new FakeDocumentVision(BuildPageTexts()),
                    new FakeAiService(failingPages: new[] { FailingPage }),
                    factory);
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    crashedManager.BuildGraphForFolderAsync(workspace, BuildProfile(), _ => { }));

                var secondVision = new FakeDocumentVision(BuildPageTexts());
                var secondAi = new FakeAiService();
                var resumeManager = new GraphRagManager(secondVision, secondAi, factory);
                ProcessingState finalState = new();

                await resumeManager.BuildGraphForFolderAsync(workspace, BuildProfile(), state => finalState = state);

                Assert.Equal(ProcessingPhase.Completed, finalState.CurrentPhase);
                Assert.Equal(PageCount, finalState.ResumedPass1Pages);
                Assert.Equal(1, finalState.ResumedPass2Pages);
                Assert.Equal(new[] { FailingPage, PageCount }, secondAi.ExtractedPages);
                Assert.Empty(secondVision.ExtractedPages);
                Assert.Equal(ExpectedEntitiesAfterResume, finalState.ExtractedEntities);
                Assert.Equal(PageCount, factory.GetCreatedRepository(workspace, ProfileId).Entities.Count);
            }
            finally
            {
                Directory.Delete(workspace, recursive: true);
            }
        }

        
        [Fact]
        public async Task BuildGraphForFolderAsync_RebuildRequested_RecreatesMasterAndReprocessesAllPages()
        {
            string workspace = CreateWorkspace(SchemaJson);
            try
            {
                var factory = new FakeGraphRepositoryFactory();
                var firstManager = new GraphRagManager(
                    new FakeDocumentVision(BuildPageTexts()),
                    new FakeAiService(),
                    factory);
                await firstManager.BuildGraphForFolderAsync(workspace, BuildProfile(), _ => { });
                FakeGraphRepository firstRepository = factory.GetCreatedRepository(workspace, ProfileId);

                var secondAi = new FakeAiService();
                var rebuildManager = new GraphRagManager(new FakeDocumentVision(BuildPageTexts()), secondAi, factory);
                ProcessingState finalState = new();

                await rebuildManager.BuildGraphForFolderAsync(
                    workspace,
                    BuildProfile(),
                    state => finalState = state,
                    rebuildFromScratch: true);

                FakeGraphRepository rebuiltRepository = factory.GetCreatedRepository(workspace, ProfileId);
                Assert.NotSame(firstRepository, rebuiltRepository);
                Assert.Equal(BuildPageNumbers(), secondAi.ExtractedPages);
                Assert.Equal(PageCount, rebuiltRepository.Entities.Count);
                Assert.Equal(ProcessingPhase.Completed, finalState.CurrentPhase);
                Assert.Equal(0, finalState.ResumedPass2Pages);
            }
            finally
            {
                Directory.Delete(workspace, recursive: true);
            }
        }

        [Fact]
        public async Task BuildGraphForFolderAsync_EmptyLlmSegments_CompletesWithEmptyGraphWarning()
        {
            string workspace = CreateWorkspace(SchemaJson);
            try
            {
                var factory = new FakeGraphRepositoryFactory();
                var manager = new GraphRagManager(
                    new FakeDocumentVision(BuildPageTexts()),
                    new FakeAiService(entitiesPerPage: 0),
                    factory);
                ProcessingState finalState = new();

                await manager.BuildGraphForFolderAsync(workspace, BuildProfile(), state => finalState = state);

                Assert.Equal(ProcessingPhase.Completed, finalState.CurrentPhase);
                Assert.Contains(ZeroEntitiesMarker, finalState.StatusMessage);
                Assert.Equal(0, factory.GetCreatedRepository(workspace, ProfileId).Entities.Count);
            }
            finally
            {
                Directory.Delete(workspace, recursive: true);
            }
        }

        [Fact]
        public async Task BuildGraphForFolderAsync_PlaceholderOntologySchema_FailsFast()
        {
            string workspace = CreateWorkspace(PlaceholderSchemaJson);
            try
            {
                var manager = new GraphRagManager(
                    new FakeDocumentVision(BuildPageTexts()),
                    new FakeAiService(),
                    new FakeGraphRepositoryFactory());

                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    manager.BuildGraphForFolderAsync(workspace, BuildProfile(), _ => { }));
            }
            finally
            {
                Directory.Delete(workspace, recursive: true);
            }
        }

        private static List<string> BuildPageTexts()
        {
            var texts = new List<string>(PageCount);
            for (int i = 1; i <= PageCount; i++)
            {
                texts.Add($"page {i} text");
            }
            return texts;
        }

        private static List<int> BuildPageNumbers()
        {
            var numbers = new List<int>(PageCount);
            for (int i = 1; i <= PageCount; i++)
            {
                numbers.Add(i);
            }
            return numbers;
        }

        private static GraphProfile BuildProfile() => new()
        {
            Id = ProfileId,
            DisplayName = "Test Profile",
            Description = "Pipeline contract profile",
            Icon = "📊",
            CreatedAt = DateTime.UtcNow,
            DatabaseFile = $"knowledge_graph_{ProfileId}.db",
            DocumentCount = 0
        };

        private static string CreateWorkspace(string schemaJson)
        {
            string workspace = Path.Combine(Path.GetTempPath(), "lens-core-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(workspace, ".instancium"));
            File.WriteAllText(Path.Combine(workspace, ".instancium", $"{ProfileId}_schema.json"), schemaJson);
            File.WriteAllText(
                Path.Combine(workspace, ".instancium", "manifest.json"),
                JsonSerializer.Serialize(new FolderManifest { Profiles = new List<GraphProfile> { BuildProfile() } }));
            File.WriteAllText(Path.Combine(workspace, DocumentName), "placeholder pdf bytes");
            return workspace;
        }

        private static int ReadManifestDocumentCount(string workspace)
        {
            string json = File.ReadAllText(Path.Combine(workspace, ".instancium", "manifest.json"));
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            FolderManifest? manifest = JsonSerializer.Deserialize<FolderManifest>(json, options);
            return manifest?.Profiles.Find(profile => profile.Id == ProfileId)?.DocumentCount ?? 0;
        }
    }
}