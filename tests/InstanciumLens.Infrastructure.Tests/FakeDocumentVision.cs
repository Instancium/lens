using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using InstanciumLens.Core.Interfaces;
using InstanciumLens.Core.Models;

namespace InstanciumLens.Infrastructure.Tests
{
    public sealed class FakeDocumentVision : IDocumentVision
    {
        private readonly IReadOnlyList<string> _pageTexts;

        public FakeDocumentVision(IReadOnlyList<string> pageTexts)
        {
            _pageTexts = pageTexts ?? throw new ArgumentNullException(nameof(pageTexts));
        }

        public List<int> ExtractedPages { get; } = new();

        public Task<int> GetPageCountAsync(string filePath, CancellationToken cancellationToken = default) =>
            Task.FromResult(_pageTexts.Count);

        public Task<IEnumerable<DocumentNode>> ExtractTextAsync(
            string filePath,
            CancellationToken cancellationToken = default,
            IProgress<DocumentPageProgress>? pageProgress = null)
        {
            string fileName = Path.GetFileName(filePath);
            var nodes = new List<DocumentNode>();
            for (int i = 0; i < _pageTexts.Count; i++)
            {
                nodes.Add(new DocumentNode
                {
                    FilePath = filePath,
                    FileName = fileName,
                    PageNumber = i + 1,
                    RawText = _pageTexts[i]
                });
                pageProgress?.Report(new DocumentPageProgress(fileName, i + 1, _pageTexts.Count));
            }
            return Task.FromResult<IEnumerable<DocumentNode>>(nodes);
        }

        public Task<DocumentNode> ExtractPageAsync(string filePath, int pageNumber, CancellationToken cancellationToken = default)
        {
            ExtractedPages.Add(pageNumber);
            return Task.FromResult(new DocumentNode
            {
                FilePath = filePath,
                FileName = Path.GetFileName(filePath),
                PageNumber = pageNumber,
                RawText = _pageTexts[pageNumber - 1]
            });
        }
    }

    public sealed class FakeAiService : ILocalModelService
    {
        private const string FakeAnswerToken = "fake";

        private readonly IReadOnlyCollection<int> _failingPages;
        private readonly int _entitiesPerPage;

        public FakeAiService(IReadOnlyCollection<int>? failingPages = null, int entitiesPerPage = 1)
        {
            _failingPages = failingPages ?? Array.Empty<int>();
            _entitiesPerPage = entitiesPerPage;
        }

        public List<int> ExtractedPages { get; } = new();

        public Task<KnowledgeGraph> ExtractEntitiesAsync(
            DocumentNode document,
            string ontologySchemaJson,
            CancellationToken cancellationToken = default)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            ExtractedPages.Add(document.PageNumber);
            if (_failingPages.Contains(document.PageNumber))
            {
                throw new InvalidOperationException($"Simulated gateway failure on page {document.PageNumber}.");
            }

            var graph = new KnowledgeGraph();
            for (int i = 0; i < _entitiesPerPage; i++)
            {
                var entity = new GraphEntity
                {
                    Id = Guid.NewGuid().ToString(),
                    Type = "TestEntity",
                    Name = $"Entity page {document.PageNumber} item {i + 1}"
                };
                entity.SourceDocumentNodeIds.Add(document.Id);
                graph.Entities.Add(entity);
            }
            return Task.FromResult(graph);
        }

        public Task<string> AnswerQueryAsync(
            string userQuery,
            KnowledgeGraph contextGraph,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(FakeAnswerToken);

        public async IAsyncEnumerable<string> AnswerQueryStreamAsync(
            string userQuery,
            CitationContext citationContext,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield return FakeAnswerToken;
        }

        public Task<(GraphProfile Profile, string OntologySchemaJson)> GenerateOntologyProfileAsync(
            string userIntent,
            CancellationToken cancellationToken = default) =>
            Task.FromResult((
                new GraphProfile
                {
                    Id = "fake_profile",
                    DisplayName = "Fake Profile",
                    Description = "Fake description",
                    Icon = "📊",
                    CreatedAt = DateTime.UtcNow,
                    DatabaseFile = "knowledge_graph_fake_profile.db"
                },
                "{}"));
    }

    public sealed class FakeGraphRepository : IGraphRepository
    {
        private readonly Dictionary<string, PageCheckpoint> _checkpoints = new(StringComparer.Ordinal);
        private readonly List<GraphEntity> _entities = new();
        private readonly List<GraphRelationship> _relationships = new();

        public IReadOnlyList<GraphEntity> Entities => _entities;
        public int SavedSegmentCount { get; private set; }
        public bool ClearPipelineCalled { get; private set; }
        public bool ClearedGraph { get; private set; }

        public Task SaveGraphSegmentAsync(KnowledgeGraph graphSegment, CancellationToken cancellationToken = default)
        {
            if (graphSegment == null) throw new ArgumentNullException(nameof(graphSegment));
            _entities.AddRange(graphSegment.Entities);
            _relationships.AddRange(graphSegment.Relationships);
            SavedSegmentCount++;
            return Task.CompletedTask;
        }

        public Task<KnowledgeGraph> LoadFullGraphAsync(CancellationToken cancellationToken = default)
        {
            var graph = new KnowledgeGraph();
            graph.Entities.AddRange(_entities);
            graph.Relationships.AddRange(_relationships);
            return Task.FromResult(graph);
        }

        public Task<KnowledgeGraph> GetRelevantSubGraphAsync(IEnumerable<string> keywords, CancellationToken cancellationToken = default) =>
            LoadFullGraphAsync(cancellationToken);

        public Task SaveChangesAsync(GraphChange change, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<PipelineCheckpointSnapshot> LoadCheckpointSnapshotAsync(CancellationToken cancellationToken = default)
        {
            var snapshot = new PipelineCheckpointSnapshot();
            foreach (PageCheckpoint page in _checkpoints.Values)
            {
                snapshot.Add(page);
            }
            return Task.FromResult(snapshot);
        }

        public Task SavePagePass1Async(PageCheckpoint page, CancellationToken cancellationToken = default)
        {
            if (page == null) throw new ArgumentNullException(nameof(page));
            _checkpoints[page.PageKey] = new PageCheckpoint
            {
                PageKey = page.PageKey,
                FilePath = page.FilePath,
                PageNumber = page.PageNumber,
                FileFingerprint = page.FileFingerprint,
                SchemaHash = page.SchemaHash,
                Pass1Completed = true,
                Pass2Completed = false,
                RawText = page.RawText
            };
            return Task.CompletedTask;
        }

        public Task SavePageSegmentAsync(KnowledgeGraph pageSegment, PageCheckpoint page, CancellationToken cancellationToken = default)
        {
            if (pageSegment == null) throw new ArgumentNullException(nameof(pageSegment));
            if (page == null) throw new ArgumentNullException(nameof(page));
            List<string> removedIds = _entities
                .Where(entity => entity.SourceDocumentNodeIds.Contains(page.PageKey))
                .Select(entity => entity.Id)
                .ToList();
            _entities.RemoveAll(entity => removedIds.Contains(entity.Id));
            _relationships.RemoveAll(relationship =>
                removedIds.Contains(relationship.SourceEntityId) || removedIds.Contains(relationship.TargetEntityId));
            _entities.AddRange(pageSegment.Entities);
            _relationships.AddRange(pageSegment.Relationships);
            _checkpoints[page.PageKey] = new PageCheckpoint
            {
                PageKey = page.PageKey,
                FilePath = page.FilePath,
                PageNumber = page.PageNumber,
                FileFingerprint = page.FileFingerprint,
                SchemaHash = page.SchemaHash,
                Pass1Completed = true,
                Pass2Completed = true,
                RawText = page.RawText
            };
            return Task.CompletedTask;
        }

        public Task ClearPipelineAsync(bool clearGraph, CancellationToken cancellationToken = default)
        {
            ClearPipelineCalled = true;
            _checkpoints.Clear();
            if (clearGraph)
            {
                ClearedGraph = true;
                _entities.Clear();
                _relationships.Clear();
            }
            return Task.CompletedTask;
        }

        public Task<int> CountEntitiesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_entities.Count);
    }

    public sealed class FakeGraphRepositoryFactory : IGraphRepositoryFactory
    {
        private readonly Dictionary<string, FakeGraphRepository> _repositories = new(StringComparer.Ordinal);

        public FakeGraphRepository CreateRepository(string folderPath, string profileId)
        {
            string key = $"{folderPath}|{profileId}";
            if (!_repositories.TryGetValue(key, out FakeGraphRepository? repository))
            {
                repository = new FakeGraphRepository();
                _repositories[key] = repository;
            }
            return repository;
        }

        IGraphRepository IGraphRepositoryFactory.CreateRepository(string targetFolderPath, string profileId) =>
            CreateRepository(targetFolderPath, profileId);

        public IGraphRepository CreateBranchRepository(string targetFolderPath, string profileId, string branchId) =>
            throw new NotImplementedException("Branch repository is out of scope for pipeline tests.");

        public void DeleteBranch(string targetFolderPath, string profileId, string branchId)
        {
        }

        public string GetMasterDatabasePath(string targetFolderPath, string profileId) =>
            Path.Combine(targetFolderPath, ".instancium", $"knowledge_graph_{profileId}.db");

        public string GetBranchDatabasePath(string targetFolderPath, string profileId, string branchId) =>
            Path.Combine(targetFolderPath, ".instancium", "branches", profileId, $"{branchId}.db");

        public FakeGraphRepository GetCreatedRepository(string folderPath, string profileId)
        {
            string key = $"{folderPath}|{profileId}";
            if (!_repositories.TryGetValue(key, out FakeGraphRepository? repository))
            {
                throw new InvalidOperationException($"Repository was never created for {key}.");
            }
            return repository;
        }

        public void DeleteMaster(string folderPath, string profileId)
        {
            string key = $"{folderPath}|{profileId}";
            _repositories.Remove(key);
        }
    }
}