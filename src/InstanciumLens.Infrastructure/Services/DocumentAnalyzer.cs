using InstanciumLens.Core.Interfaces;
using InstanciumLens.Core.Models;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace InstanciumLens.Infrastructure.Services
{
    public class DocumentAnalyzer
    {
        private readonly IDocumentVision _documentVision;
        private readonly ILocalModelService _localModelService;
        private readonly IGraphRepository _graphRepository;

        // Constructor for Dependency Injection
        public DocumentAnalyzer(
            IDocumentVision documentVision,
            ILocalModelService localModelService,
            IGraphRepository graphRepository)
        {
            _documentVision = documentVision;
            _localModelService = localModelService;
            _graphRepository = graphRepository;
        }

        /// <summary>
        /// Orchestrates the 3-pass document analysis process and updates the UI state.
        /// </summary>
        public async Task ProcessDocumentAsync(
            string filePath,
            string ontologySchemaJson,
            Action<ProcessingState> progressCallback,
            CancellationToken cancellationToken = default)
        {
            var state = new ProcessingState
            {
                TotalDocuments = 1,
                ProcessedDocuments = 0,
                ProgressPercentage = 0
            };

            try
            {
                // ==========================================
                // PASS 1: Text Extraction (Vision)
                // ==========================================
                state.CurrentPhase = ProcessingPhase.Pass1_TextExtraction;
                state.StatusMessage = $"Reading PDF pages: {filePath}";
                progressCallback?.Invoke(state);

                // Calls native Windows.Data.Pdf and Windows.Media.Ocr via Infrastructure layer
                var documentNodes = await _documentVision.ExtractTextAsync(filePath, cancellationToken);
                var nodesList = documentNodes.ToList();

                state.ProgressPercentage = 33;
                progressCallback?.Invoke(state);

                // ==========================================
                // PASS 2: Entity Resolution (LLM)
                // ==========================================
                state.CurrentPhase = ProcessingPhase.Pass2_EntityResolution;
                state.StatusMessage = "Extracting facts and entities via Qwen 8B...";
                progressCallback?.Invoke(state);

                var aggregatedGraph = new KnowledgeGraph();
                int processedPages = 0;

                foreach (var node in nodesList)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Calls local LLM using the provided ontology (e.g., Legal, Engineering)
                    var segmentGraph = await _localModelService.ExtractEntitiesAsync(node, ontologySchemaJson, cancellationToken);

                    aggregatedGraph.Entities.AddRange(segmentGraph.Entities);
                    aggregatedGraph.Relationships.AddRange(segmentGraph.Relationships);

                    processedPages++;
                    state.ProgressPercentage = 33 + (int)((double)processedPages / nodesList.Count * 33);
                    progressCallback?.Invoke(state);
                }

                // ==========================================
                // PASS 3: Graph Building (Database)
                // ==========================================
                state.CurrentPhase = ProcessingPhase.Pass3_GraphBuilding;
                state.StatusMessage = "Building knowledge graph and saving to SQLite...";
                progressCallback?.Invoke(state);

                // Saves the connections to SQLite repository
                await _graphRepository.SaveGraphSegmentAsync(aggregatedGraph, cancellationToken);

                state.CurrentPhase = ProcessingPhase.Completed;
                state.StatusMessage = "Analysis complete. Ready for questions.";
                state.ProgressPercentage = 100;
                state.ProcessedDocuments = 1;
                progressCallback?.Invoke(state);
            }
            catch (OperationCanceledException)
            {
                state.CurrentPhase = ProcessingPhase.Error;
                state.StatusMessage = "Processing was canceled by the user.";
                progressCallback?.Invoke(state);
                throw;
            }
            catch (Exception ex)
            {
                state.CurrentPhase = ProcessingPhase.Error;
                state.StatusMessage = $"An error occurred: {ex.Message}";
                progressCallback?.Invoke(state);
                throw;
            }
        }
    }
}