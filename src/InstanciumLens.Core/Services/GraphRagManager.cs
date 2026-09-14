using InstanciumLens.Core.Interfaces;
using InstanciumLens.Core.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace InstanciumLens.Core.Services
{
    public class GraphRagManager : IGraphRagManager
    {
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
            CancellationToken cancellationToken = default)
        {
            var state = new ProcessingState { CurrentPhase = ProcessingPhase.Pass1_TextExtraction };

            try
            {
                // 1. Load the customized AI Ontology Schema from the hidden folder
                string schemaPath = Path.Combine(folderPath, ".instancium", $"{profile.Id}_schema.json");
                if (!File.Exists(schemaPath))
                {
                    throw new FileNotFoundException("Ontology schema not found. Please recreate the profile.");
                }

                string ontologySchemaJson = await File.ReadAllTextAsync(schemaPath, cancellationToken);

                // 2. Locate all target documents
                var pdfFiles = Directory.GetFiles(folderPath, "*.pdf", SearchOption.AllDirectories);
                state.TotalDocuments = pdfFiles.Length;

                if (state.TotalDocuments == 0)
                {
                    state.CurrentPhase = ProcessingPhase.Completed;
                    state.StatusMessage = "No PDF documents found in the selected folder.";
                    progressCallback?.Invoke(state);
                    return;
                }

                // 3. Initialize the specific SQLite database for this user's profile
                var repository = _repositoryFactory.CreateRepository(folderPath, profile.Id);

                // 4. Execute the 3-Pass Analysis Pipeline for each document
                for (int i = 0; i < pdfFiles.Length; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string file = pdfFiles[i];
                    string fileName = Path.GetFileName(file);

                    // --- PASS 1: Native Windows Vision ---
                    state.CurrentPhase = ProcessingPhase.Pass1_TextExtraction;
                    state.StatusMessage = $"Reading document {i + 1} of {pdfFiles.Length}: {fileName}";
                    state.ProgressPercentage = (int)(((double)i / pdfFiles.Length) * 100);
                    progressCallback?.Invoke(state);

                    var pages = await _documentVision.ExtractTextAsync(file, cancellationToken);

                    // --- PASS 2: Local AI Entity Resolution ---
                    state.CurrentPhase = ProcessingPhase.Pass2_EntityResolution;
                    state.StatusMessage = $"Extracting facts from {fileName} using AI...";
                    progressCallback?.Invoke(state);

                    var aggregatedGraph = new KnowledgeGraph();
                    foreach (var page in pages)
                    {
                        var subGraph = await _aiService.ExtractEntitiesAsync(page, ontologySchemaJson, cancellationToken);
                        aggregatedGraph.Entities.AddRange(subGraph.Entities);
                        aggregatedGraph.Relationships.AddRange(subGraph.Relationships);
                    }

                    // --- PASS 3: Graph Building (Database Storage) ---
                    state.CurrentPhase = ProcessingPhase.Pass3_GraphBuilding;
                    state.StatusMessage = $"Saving relationships for {fileName}...";
                    progressCallback?.Invoke(state);

                    await repository.SaveGraphSegmentAsync(aggregatedGraph, cancellationToken);

                    state.ProcessedDocuments = i + 1;
                }

                state.CurrentPhase = ProcessingPhase.Completed;
                state.StatusMessage = "Graph database successfully built! Ready for analysis.";
                state.ProgressPercentage = 100;
                progressCallback?.Invoke(state);
            }
            catch (Exception ex)
            {
                state.CurrentPhase = ProcessingPhase.Error;
                state.StatusMessage = $"Process interrupted: {ex.Message}";
                progressCallback?.Invoke(state);
                throw;
            }
        }
    }
}
