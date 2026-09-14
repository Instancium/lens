using System;
using System.Text;
using System.Text.Json;
using InstanciumLens.Core.Interfaces;
using InstanciumLens.Core.Models;
using InstanciumLens.AI.Abstractions;

namespace InstanciumLens.AI.Services
{
    /// <summary>
    /// Implements the core AI logic: mapping domain concepts to LLM prompts and parsing results.
    /// This service orchestrates Pass 2 (Entity Extraction) and query answering.
    /// </summary>
    public class LocalAiService : ILocalModelService
    {
        private readonly IInferenceProvider _inferenceProvider;
        private bool _isModelLoaded = false;


        public LocalAiService(IInferenceProvider inferenceProvider)
        {
            _inferenceProvider = inferenceProvider ?? throw new ArgumentNullException(nameof(inferenceProvider));
        }


        public async Task<KnowledgeGraph> ExtractEntitiesAsync(
            DocumentNode document,
            string ontologySchemaJson,
            CancellationToken cancellationToken = default)
        {
            // 1. Build a strict prompt forcing the model to output JSON
            string prompt = BuildExtractionPrompt(document.RawText, ontologySchemaJson);

            // 2. Consume the streaming response from the inference provider
            var sb = new StringBuilder();
            await foreach (var token in _inferenceProvider.GenerateTextStreamAsync(prompt, cancellationToken))
            {
                sb.Append(token);
            }

            // 3. Clean up the output (models often wrap JSON in markdown block quotes)
            string rawJson = CleanLlmJsonOutput(sb.ToString());

            try
            {
                // 4. Parse the LLM's JSON string back into our C# domain models
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var graphSegment = JsonSerializer.Deserialize<KnowledgeGraph>(rawJson, options);

                if (graphSegment != null)
                {
                    // Automatically link extracted entities back to the source document 
                    // so the UI can provide clickable "proofs" to the user
                    foreach (var entity in graphSegment.Entities)
                    {
                        entity.SourceDocumentNodeIds.Add(document.Id);
                    }
                    return graphSegment;
                }
            }
            catch (JsonException)
            {
                // In production, we would log this. For now, return an empty segment on hallucination/bad JSON.
            }

            return new KnowledgeGraph();
        }

        public async Task<string> AnswerQueryAsync(
            string userQuery,
            KnowledgeGraph contextGraph,
            CancellationToken cancellationToken = default)
        {
            string prompt = BuildAnswerPrompt(userQuery, contextGraph);
            var sb = new StringBuilder();

            // For Pass 3 (Answering), we aggregate the stream. 
            // In a future UI update, we could expose the IAsyncEnumerable directly to the UI for typing effects.
            await foreach (var token in _inferenceProvider.GenerateTextStreamAsync(prompt, cancellationToken))
            {
                sb.Append(token);
            }

            return sb.ToString();
        }

        #region Prompt Engineering

        private string BuildExtractionPrompt(string text, string ontology)
        {
            // Using Qwen/ChatML prompt format
            return $"<|im_start|>system\n" +
                   $"You are a precise data extraction AI. Extract entities and relationships from the text based on this JSON ontology schema:\n{ontology}\n" +
                   $"Return strictly valid JSON matching the schema with 'Entities' and 'Relationships' arrays. Do not add markdown formatting, conversational filler, or explanations.\n" +
                   $"<|im_end|>\n" +
                   $"<|im_start|>user\nText to analyze: {text}<|im_end|>\n" +
                   $"<|im_start|>assistant\n";
        }

        private string BuildAnswerPrompt(string query, KnowledgeGraph context)
        {
            string contextJson = JsonSerializer.Serialize(context);
            return $"<|im_start|>system\n" +
                   $"You are an expert analytical assistant. Answer the user's question using ONLY the provided Knowledge Graph context below.\n" +
                   $"If the answer is not contained in the context, explicitly state that you do not have enough information.\n" +
                   $"Context: {contextJson}\n" +
                   $"<|im_end|>\n" +
                   $"<|im_start|>user\n{query}<|im_end|>\n" +
                   $"<|im_start|>assistant\n";
        }

        #endregion

        #region Helpers

        private string CleanLlmJsonOutput(string text)
        {
            // Local LLMs frequently output ```json ... ``` despite instructions.
            // This safely extracts just the JSON payload.
            var start = text.IndexOf('{');
            var end = text.LastIndexOf('}');
            if (start >= 0 && end > start)
            {
                return text.Substring(start, end - start + 1);
            }
            return text;
        }

        // DTO for parsing the specific LLM JSON structure
        private class LlmOntologyResponse
        {
            public string ProfileName { get; set; } = string.Empty;
            public string DisplayName { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public string Icon { get; set; } = "📊";
            public JsonElement OntologySchema { get; set; }
        }
        public async Task<(GraphProfile Profile, string OntologySchemaJson)> GenerateOntologyProfileAsync(
    string userIntent,
    CancellationToken cancellationToken = default)
        {
            if (!_isModelLoaded)
            {
                string modelDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "InstanciumLens", "Models");

                if (!Directory.Exists(modelDir))
                {
                    throw new DirectoryNotFoundException($"Model directory not found: {modelDir}");
                }

                var ggufFiles = Directory.GetFiles(modelDir, "*.gguf");

                if (ggufFiles.Length == 0)
                {
                    throw new FileNotFoundException($"No .gguf model found in {modelDir}. Please wait for the initial download to complete.");
                }

                string modelPath = ggufFiles[0];

                await _inferenceProvider.InitializeAsync(modelPath, cancellationToken);
                _isModelLoaded = true;
            }

            string prompt = BuildOntologyPrompt(userIntent);
            var sb = new StringBuilder();

            await foreach (var token in _inferenceProvider.GenerateTextStreamAsync(prompt, cancellationToken))
            {
                sb.Append(token);
            }

            string rawJson = CleanLlmJsonOutput(sb.ToString());

            try
            {
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var llmResponse = JsonSerializer.Deserialize<LlmOntologyResponse>(rawJson, options);

                if (llmResponse != null)
                {
                    string safeId = string.IsNullOrWhiteSpace(llmResponse.ProfileName)
                        ? Guid.NewGuid().ToString().Substring(0, 8)
                        : llmResponse.ProfileName.ToLower().Replace(" ", "_");

                    var profile = new GraphProfile
                    {
                        Id = safeId,
                        DisplayName = llmResponse.DisplayName,
                        Description = llmResponse.Description,
                        Icon = string.IsNullOrWhiteSpace(llmResponse.Icon) ? "📊" : llmResponse.Icon,
                        CreatedAt = DateTime.UtcNow,
                        DatabaseFile = $"knowledge_graph_{safeId}.db"
                    };

                    string schemaJson = JsonSerializer.Serialize(llmResponse.OntologySchema, new JsonSerializerOptions { WriteIndented = true });

                    return (profile, schemaJson);
                }
            }
            catch (JsonException)
            {
            }

            var fallbackProfile = new GraphProfile
            {
                Id = "default_audit",
                DisplayName = "General Audit",
                Description = "Fallback profile created due to generation error.",
                DatabaseFile = "knowledge_graph_default.db",
                Icon = "📑",
                CreatedAt = DateTime.UtcNow
            };
            return (fallbackProfile, "{}");
        }


        private string BuildOntologyPrompt(string intent)
        {
            // Strict ChatML formatting to force Qwen 8B into generating valid JSON
            return $"<|im_start|>system\n" +
                   $"You are an expert data architect and ontology engineer. Your task is to analyze the user's intent and design a strict Knowledge Graph ontology schema.\n" +
                   $"You must output ONLY a valid JSON object. Do not add conversational text, markdown formatting, or explanations.\n" +
                   $"The JSON must strictly follow this structure:\n" +
                   $"{{\n" +
                   $"  \"ProfileName\": \"short_english_name\",\n" +
                   $"  \"DisplayName\": \"Human readable title in the user's language\",\n" +
                   $"  \"Description\": \"Short description of what this graph extracts\",\n" +
                   $"  \"Icon\": \"A single appropriate emoji\",\n" +
                   $"  \"OntologySchema\": {{\n" +
                   $"    \"Entities\": [{{ \"Type\": \"TargetEntity\", \"Description\": \"What it represents\" }}],\n" +
                   $"    \"Relationships\": [{{ \"RelationType\": \"RELATION_NAME\", \"Description\": \"How entities connect\" }}]\n" +
                   $"  }}\n" +
                   $"}}\n" +
                   $"<|im_end|>\n" +
                   $"<|im_start|>user\n{intent}<|im_end|>\n" +
                   $"<|im_start|>assistant\n";
        }

        #endregion
    }
}