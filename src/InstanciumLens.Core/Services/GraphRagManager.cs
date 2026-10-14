using InstanciumLens.Core.Interfaces;
using InstanciumLens.Core.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace InstanciumLens.Core.Services
{
    public class GraphRagManager : IGraphRagManager
    {
        private const int ThrottleMilliseconds = 250;
        private const int MaxPercentBeforeCompletion = 99;
        private const int MaxDocumentNameLength = 40;
        private const string PlaceholderEntityType = "TargetEntity";

        private readonly IDocumentVision _documentVision;
        private readonly ILocalModelService _aiService;
        private readonly IGraphRepositoryFactory _repositoryFactory;

        public GraphRagManager(
            IDocumentVision documentVision,
            ILocalModelService aiService,
            IGraphRepositoryFactory repositoryFactory)
        {
            _documentVision = documentVision ?? throw new ArgumentNullException(nameof(documentVision));
            _aiService = aiService ?? throw new ArgumentNullException(nameof(aiService));
            _repositoryFactory = repositoryFactory ?? throw new ArgumentNullException(nameof(repositoryFactory));
        }

        public async Task BuildGraphForFolderAsync(
            string folderPath,
            GraphProfile profile,
            Action<ProcessingState> progressCallback,
            bool rebuildFromScratch = false,
            CancellationToken cancellationToken = default)
        {
            var state = new ProcessingState { CurrentPhase = ProcessingPhase.Pass1_TextExtraction };
            var stopwatch = Stopwatch.StartNew();
            var estimator = new EtaEstimator();
            var reporter = new ProgressReporter(state, stopwatch, estimator, progressCallback);
            try
            {
                string schemaPath = Path.Combine(folderPath, ".instancium", $"{profile.Id}_schema.json");
                if (!File.Exists(schemaPath))
                {
                    throw new FileNotFoundException("Ontology schema not found. Please recreate the profile.");
                }

                string ontologySchemaJson = await File.ReadAllTextAsync(schemaPath, cancellationToken);
                ValidateOntologySchema(ontologySchemaJson, schemaPath);
                string schemaHash = BuildSchemaHash(ontologySchemaJson);
                string[] pdfFiles = Directory.GetFiles(folderPath, "*.pdf", SearchOption.AllDirectories);
                state.TotalDocuments = pdfFiles.Length;
                if (state.TotalDocuments == 0)
                {
                    state.CurrentPhase = ProcessingPhase.Completed;
                    state.StatusMessage = "No PDF documents found in the selected folder.";
                    state.ProgressPercentage = 100;
                    state.EstimatedRemaining = TimeSpan.Zero;
                    reporter.Report(force: true);
                    return;
                }

                // The master must be deleted before the repository is created:
                // the repository constructor initializes the schema on the target database file.
                if (rebuildFromScratch)
                {
                    reporter.SetMessage("Rebuild requested: clearing previous graph and checkpoints...");
                    reporter.Report(force: true);
                    _repositoryFactory.DeleteMaster(folderPath, profile.Id);
                }

                var repository = _repositoryFactory.CreateRepository(folderPath, profile.Id);
                PipelineCheckpointSnapshot snapshot = await repository.LoadCheckpointSnapshotAsync(cancellationToken);

                reporter.SetMessage("Counting pages in PDF documents...");
                reporter.Report(force: true);
                var documentPlans = new List<DocumentPlan>();
                foreach (string file in pdfFiles)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var fileInfo = new FileInfo(file);
                    int pageCount = await _documentVision.GetPageCountAsync(file, cancellationToken);
                    string relativePath = Path.GetRelativePath(folderPath, file);
                    string fingerprint = BuildFileFingerprint(fileInfo);
                    var plan = new DocumentPlan(file, relativePath, fingerprint);
                    for (int pageNumber = 1; pageNumber <= pageCount; pageNumber++)
                    {
                        string pageKey = BuildPageKey(relativePath, pageNumber);
                        PageCheckpoint checkpoint = snapshot.TryGet(pageKey, out PageCheckpoint? existing) &&
                            existing != null &&
                            existing.FileFingerprint == fingerprint
                            ? existing
                            : new PageCheckpoint
                            {
                                PageKey = pageKey,
                                FilePath = file,
                                PageNumber = pageNumber,
                                FileFingerprint = fingerprint,
                                SchemaHash = schemaHash
                            };
                        plan.Pages.Add(checkpoint);
                    }
                    documentPlans.Add(plan);
                    state.TotalPages += pageCount;
                }

                state.ResumedPass1Pages = documentPlans.Sum(plan =>
                    plan.Pages.Count(page => page.Pass1Completed && page.RawText != null));
                state.ResumedPass2Pages = documentPlans.Sum(plan =>
                    plan.Pages.Count(page => page.Pass2Completed && page.SchemaHash == schemaHash));
                state.Pass1ProcessedPages = state.ResumedPass1Pages;
                state.Pass2ProcessedPages = state.ResumedPass2Pages;
                if (state.ResumedPass1Pages > 0 || state.ResumedPass2Pages > 0)
                {
                    reporter.SetMessage(
                        $"Checkpoint loaded: {state.ResumedPass1Pages} pages cached for Pass 1, " +
                        $"{state.ResumedPass2Pages} pages cached for Pass 2. Resuming.");
                    reporter.Report(force: true);
                }

                int pass1Base = 0;
                int pass2Base = 0;
                for (int i = 0; i < documentPlans.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    DocumentPlan plan = documentPlans[i];
                    state.CurrentDocumentName = plan.FileName;

                    state.CurrentPhase = ProcessingPhase.Pass1_TextExtraction;
                    reporter.SetMessage($"Pass 1 · {plan.FileName} · reading pages");
                    reporter.Report(force: true);
                    int pass1Done = plan.Pages.Count(page => page.Pass1Completed && page.RawText != null);
                    TimeSpan lastPageStamp = stopwatch.Elapsed;
                    var pageNodes = new List<DocumentNode>(plan.Pages.Count);
                    foreach (PageCheckpoint page in plan.Pages)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (!page.Pass1Completed || page.RawText == null)
                        {
                            DocumentNode extracted = await _documentVision.ExtractPageAsync(plan.FilePath, page.PageNumber, cancellationToken);
                            estimator.RecordPass1Page((stopwatch.Elapsed - lastPageStamp).TotalSeconds);
                            page.RawText = extracted.RawText;
                            page.SchemaHash = schemaHash;
                            await repository.SavePagePass1Async(page, cancellationToken);
                            page.Pass1Completed = true;
                            pass1Done++;
                        }
                        lastPageStamp = stopwatch.Elapsed;
                        state.Pass1ProcessedPages = pass1Base + pass1Done;
                        pageNodes.Add(new DocumentNode
                        {
                            Id = page.PageKey,
                            FilePath = plan.FilePath,
                            FileName = plan.FileName,
                            PageNumber = page.PageNumber,
                            RawText = page.RawText ?? string.Empty
                        });
                        reporter.Report(force: false);
                    }

                    state.CurrentPhase = ProcessingPhase.Pass2_EntityResolution;
                    reporter.SetMessage($"Pass 2 · {plan.FileName} · extracting facts");
                    reporter.Report(force: true);
                    int pass2Done = plan.Pages.Count(page => page.Pass2Completed && page.SchemaHash == schemaHash);
                    for (int pageIndex = 0; pageIndex < plan.Pages.Count; pageIndex++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        PageCheckpoint page = plan.Pages[pageIndex];
                        if (page.Pass2Completed && page.SchemaHash == schemaHash)
                        {
                            state.Pass2ProcessedPages = pass2Base + pass2Done;
                            reporter.Report(force: false);
                            continue;
                        }

                        var pageStopwatch = Stopwatch.StartNew();
                        KnowledgeGraph segment = await _aiService.ExtractEntitiesAsync(pageNodes[pageIndex], ontologySchemaJson, cancellationToken);
                        estimator.RecordPass2Page(pageStopwatch.Elapsed.TotalSeconds);
                        page.SchemaHash = schemaHash;
                        await repository.SavePageSegmentAsync(segment, page, cancellationToken);
                        page.Pass2Completed = true;
                        pass2Done++;
                        state.ExtractedEntities += segment.Entities.Count;
                        state.ExtractedRelationships += segment.Relationships.Count;
                        state.Pass2ProcessedPages = pass2Base + pass2Done;
                        reporter.Report(force: false);
                    }

                    pass1Base += plan.Pages.Count;
                    pass2Base += plan.Pages.Count;

                    state.CurrentPhase = ProcessingPhase.Pass3_GraphBuilding;
                    reporter.SetMessage($"Pass 3 · {plan.FileName} · committing verified segments");
                    reporter.Report(force: true);
                    state.ProcessedDocuments = i + 1;
                }

                await UpdateManifestDocumentCountAsync(folderPath, profile, pdfFiles.Length, cancellationToken);

                int totalEntities = await repository.CountEntitiesAsync(cancellationToken);
                state.CurrentPhase = ProcessingPhase.Completed;
                state.StatusMessage = BuildCompletionMessage(state, totalEntities, schemaPath);
                state.ProgressPercentage = 100;
                state.EstimatedRemaining = TimeSpan.Zero;
                reporter.Report(force: true);
            }
            catch (Exception ex)
            {
                state.CurrentPhase = ProcessingPhase.Error;
                state.StatusMessage = $"Process interrupted: {ex.Message}";
                reporter.Report(force: true);
                throw;
            }
        }

        private static async Task UpdateManifestDocumentCountAsync(
            string folderPath,
            GraphProfile profile,
            int documentCount,
            CancellationToken cancellationToken)
        {
            string manifestPath = Path.Combine(folderPath, ".instancium", "manifest.json");
            if (!File.Exists(manifestPath))
            {
                return;
            }

            string json = await File.ReadAllTextAsync(manifestPath, cancellationToken);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            FolderManifest? manifest = JsonSerializer.Deserialize<FolderManifest>(json, options);
            GraphProfile? entry = manifest?.Profiles.FirstOrDefault(candidate => candidate.Id == profile.Id);
            if (entry == null)
            {
                return;
            }

            entry.DocumentCount = documentCount;
            string updated = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(manifestPath, updated, cancellationToken);
        }

        private static void ValidateOntologySchema(string schemaJson, string schemaPath)
        {
            List<string> entityTypes;
            try
            {
                using var document = JsonDocument.Parse(schemaJson);
                entityTypes = new List<string>();
                if (document.RootElement.ValueKind == JsonValueKind.Object &&
                    document.RootElement.TryGetProperty("Entities", out JsonElement entities) &&
                    entities.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement item in entities.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.Object &&
                            item.TryGetProperty("Type", out JsonElement type) &&
                            type.ValueKind == JsonValueKind.String &&
                            !string.IsNullOrWhiteSpace(type.GetString()))
                        {
                            entityTypes.Add(type.GetString()!);
                        }
                    }
                }
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException(
                    $"Ontology schema at {schemaPath} is not valid JSON: {ex.Message}", ex);
            }

            bool placeholderOnly = entityTypes.Count == 0 ||
                entityTypes.All(type => string.Equals(type, PlaceholderEntityType, StringComparison.OrdinalIgnoreCase));
            if (placeholderOnly)
            {
                throw new InvalidOperationException(
                    $"Ontology schema at {schemaPath} contains no real entity types (placeholder schema detected). " +
                    "Recreate the profile via Smart Interview before building the graph.");
            }
        }

        private static string BuildCompletionMessage(ProcessingState state, int totalEntities, string schemaPath)
        {
            if (totalEntities == 0)
            {
                return $"Graph is empty after build: 0 entities in the database. " +
                    $"Check ontology schema at {schemaPath} or recreate the profile via Smart Interview.";
            }

            int processedNow = Math.Max(0, state.TotalPages - state.ResumedPass2Pages);
            return $"Graph ready: {totalEntities} entities total, {state.ExtractedEntities} new in this run, " +
                $"{state.ResumedPass2Pages} pages resumed, {processedNow} pages processed now.";
        }

        private static string BuildPageKey(string relativePath, int pageNumber) =>
            $"{relativePath.Replace('\\', '/')}#{pageNumber}";

        private static string BuildFileFingerprint(FileInfo file) =>
            $"{file.Length}:{file.LastWriteTimeUtc.Ticks}";

        private static string BuildSchemaHash(string schemaJson) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(schemaJson)));

        private sealed class DocumentPlan
        {
            public DocumentPlan(string filePath, string relativePath, string fingerprint)
            {
                FilePath = filePath;
                RelativePath = relativePath;
                Fingerprint = fingerprint;
            }

            public string FilePath { get; }
            public string RelativePath { get; }
            public string Fingerprint { get; }
            public List<PageCheckpoint> Pages { get; } = new();
            public string FileName => Path.GetFileName(FilePath);
        }

        private sealed class ProgressReporter
        {
            private readonly ProcessingState _state;
            private readonly Stopwatch _stopwatch;
            private readonly EtaEstimator _estimator;
            private readonly Action<ProcessingState>? _callback;
            private DateTime _lastReportUtc = DateTime.MinValue;

            public ProgressReporter(
                ProcessingState state,
                Stopwatch stopwatch,
                EtaEstimator estimator,
                Action<ProcessingState>? callback)
            {
                _state = state;
                _stopwatch = stopwatch;
                _estimator = estimator;
                _callback = callback;
            }

            public void SetMessage(string message) => _state.StatusMessage = message;

            public void Report(bool force)
            {
                _state.Elapsed = _stopwatch.Elapsed;
                if (_state.CurrentPhase == ProcessingPhase.Pass1_TextExtraction ||
                    _state.CurrentPhase == ProcessingPhase.Pass2_EntityResolution)
                {
                    int pass1Remaining = Math.Max(0, _state.TotalPages - _state.Pass1ProcessedPages);
                    int pass2Remaining = Math.Max(0, _state.TotalPages - _state.Pass2ProcessedPages);
                    int documentsRemaining = Math.Max(0, _state.TotalDocuments - _state.ProcessedDocuments);
                    double seconds = _estimator.EstimateRemainingSeconds(pass1Remaining, pass2Remaining, documentsRemaining);
                    _state.EstimatedRemaining = TimeSpan.FromSeconds(seconds);
                    _state.ProgressPercentage = ComputePercent();
                    if (_state.TotalPages > 0)
                    {
                        _state.StatusMessage = BuildDetailedMessage();
                    }
                }

                if (!force && (DateTime.UtcNow - _lastReportUtc).TotalMilliseconds < ThrottleMilliseconds)
                {
                    return;
                }
                _lastReportUtc = DateTime.UtcNow;
                _callback?.Invoke(_state);
            }

            private int ComputePercent()
            {
                if (_state.TotalPages == 0)
                {
                    return 0;
                }
                int done = _state.Pass1ProcessedPages + _state.Pass2ProcessedPages;
                int percent = (int)(done * 100.0 / (2 * _state.TotalPages));
                return Math.Min(percent, MaxPercentBeforeCompletion);
            }

            private string BuildDetailedMessage()
            {
                string eta = FormatDuration(_state.EstimatedRemaining);
                string elapsed = FormatDuration(_state.Elapsed);
                string documentName = TruncateDocumentName(_state.CurrentDocumentName ?? string.Empty);
                return _state.CurrentPhase switch
                {
                    ProcessingPhase.Pass1_TextExtraction =>
                        $"Pass 1 · {documentName} · page {_state.Pass1ProcessedPages}/{_state.TotalPages} · elapsed {elapsed} · ETA {eta}",
                    ProcessingPhase.Pass2_EntityResolution =>
                        $"Pass 2 · {documentName} · page {_state.Pass2ProcessedPages}/{_state.TotalPages} · elapsed {elapsed} · ETA {eta}",
                    _ => _state.StatusMessage
                };
            }

            private static string TruncateDocumentName(string name)
            {
                if (name.Length <= MaxDocumentNameLength)
                {
                    return name;
                }
                int headLength = (MaxDocumentNameLength - 1) / 2;
                int tailLength = MaxDocumentNameLength - 1 - headLength;
                return string.Concat(
                    name.AsSpan(0, headLength),
                    "…",
                    name.AsSpan(name.Length - tailLength));
            }

            private static string FormatDuration(TimeSpan? duration)
            {
                if (duration == null)
                {
                    return "unknown";
                }
                TimeSpan value = duration.Value;
                if (value.TotalMinutes < 1)
                {
                    return "<1 min";
                }
                if (value.TotalHours < 1)
                {
                    return $"~{(int)Math.Ceiling(value.TotalMinutes)} min";
                }
                return $"~{(int)value.TotalHours} h {value.Minutes} min";
            }
        }

        private sealed class EtaEstimator
        {
            // Heuristic per-page costs keep the ETA meaningful before the first measured page completes on this machine.
            private const double DefaultPass1SecondsPerPage = 1.2;
            private const double DefaultPass2SecondsPerPage = 6.0;
            private const double Pass3SecondsPerDocument = 2.0;

            private double _pass1Seconds;
            private int _pass1Pages;
            private double _pass2Seconds;
            private int _pass2Pages;

            public void RecordPass1Page(double seconds)
            {
                if (seconds <= 0)
                {
                    return;
                }
                _pass1Seconds += seconds;
                _pass1Pages++;
            }

            public void RecordPass2Page(double seconds)
            {
                if (seconds <= 0)
                {
                    return;
                }
                _pass2Seconds += seconds;
                _pass2Pages++;
            }

            private double Pass1Average => _pass1Pages > 0 ? _pass1Seconds / _pass1Pages : DefaultPass1SecondsPerPage;

            private double Pass2Average => _pass2Pages > 0 ? _pass2Seconds / _pass2Pages : DefaultPass2SecondsPerPage;

            public double EstimateRemainingSeconds(int pass1PagesRemaining, int pass2PagesRemaining, int documentsRemaining) =>
                (pass1PagesRemaining * Pass1Average) +
                (pass2PagesRemaining * Pass2Average) +
                (documentsRemaining * Pass3SecondsPerDocument);
        }
    }
}