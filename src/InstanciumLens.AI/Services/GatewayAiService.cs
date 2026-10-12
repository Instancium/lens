using System.Runtime.CompilerServices;
using System.Text.Json;
using InstanciumLens.AI.Abstractions;
using InstanciumLens.AI.Configuration;
using InstanciumLens.AI.Contracts;
using InstanciumLens.Core.Interfaces;
using InstanciumLens.Core.Models;

namespace InstanciumLens.AI.Services
{
    /// <summary>
    /// ILocalModelService implementation backed by InstantAIGate sessions.
    /// Every call owns an ephemeral session; destruction is guaranteed by the hub client core
    /// in all exit paths, so no double-destroy happens here.
    /// </summary>
    public sealed class GatewayAiService : ILocalModelService
    {
        private readonly IGatewayHubClient _gateway;
        private readonly GatewaySettings _settings;

        /// <summary>Initializes the service with the gateway facade and endpoint settings.</summary>
        public GatewayAiService(IGatewayHubClient gateway, GatewaySettings settings)
        {
            _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        /// <inheritdoc/>
        public async Task<KnowledgeGraph> ExtractEntitiesAsync(
            DocumentNode document,
            string ontologySchemaJson,
            CancellationToken cancellationToken = default)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));

            string response = await CompleteAsync(
                BuildExtractionSystemPrompt(ontologySchemaJson),
                $"Text to analyze: {document.RawText}",
                cancellationToken);

            return ParseGraphSegment(response, document);
        }

        /// <inheritdoc/>
        public Task<string> AnswerQueryAsync(
            string userQuery,
            KnowledgeGraph contextGraph,
            CancellationToken cancellationToken = default)
        {
            if (contextGraph == null) throw new ArgumentNullException(nameof(contextGraph));

            return CompleteAsync(BuildGraphAnswerSystemPrompt(contextGraph), userQuery, cancellationToken);
        }

        /// <inheritdoc/>
        public async IAsyncEnumerable<string> AnswerQueryStreamAsync(
            string userQuery,
            CitationContext citationContext,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (citationContext == null) throw new ArgumentNullException(nameof(citationContext));

            string sessionId = "lens-chat-" + Guid.NewGuid().ToString("N");
            var messages = new List<GatewayChatMessage>
            {
                GatewayChatMessage.System(BuildCitationAnswerSystemPrompt(citationContext)),
                GatewayChatMessage.User(userQuery)
            };

            await foreach (var token in _gateway.RunCompletionStreamAsync(
                sessionId, _settings.ActiveRepoId, messages, ephemeral: true, cancellationToken))
            {
                yield return token;
            }
        }

        /// <inheritdoc/>
        public async Task<(GraphProfile Profile, string OntologySchemaJson)> GenerateOntologyProfileAsync(
            string userIntent,
            CancellationToken cancellationToken = default)
        {
            string response = await CompleteAsync(BuildOntologySystemPrompt(), userIntent, cancellationToken);

            string rawJson = CleanLlmJsonOutput(response);
            try
            {
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var llmResponse = JsonSerializer.Deserialize<LlmOntologyResponse>(rawJson, options);
                if (llmResponse != null)
                {
                    string safeId = string.IsNullOrWhiteSpace(llmResponse.ProfileName)
                        ? Guid.NewGuid().ToString("N")[..8]
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

                    string schemaJson = JsonSerializer.Serialize(
                        llmResponse.OntologySchema,
                        new JsonSerializerOptions { WriteIndented = true });
                    return (profile, schemaJson);
                }
            }
            catch (JsonException)
            {
                // Fall through to the fallback profile below.
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

        private Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken)
        {
            string sessionId = "lens-" + Guid.NewGuid().ToString("N");
            var messages = new List<GatewayChatMessage>
            {
                GatewayChatMessage.System(systemPrompt),
                GatewayChatMessage.User(userPrompt)
            };

            return _gateway.RunCompletionAsync(sessionId, _settings.ActiveRepoId, messages, ephemeral: true, cancellationToken);
        }

        private KnowledgeGraph ParseGraphSegment(string response, DocumentNode document)
        {
            string rawJson = CleanLlmJsonOutput(response);
            try
            {
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var graphSegment = JsonSerializer.Deserialize<KnowledgeGraph>(rawJson, options);
                if (graphSegment != null)
                {
                    foreach (var entity in graphSegment.Entities)
                    {
                        entity.SourceDocumentNodeIds.Add(document.Id);
                    }

                    return graphSegment;
                }
            }
            catch (JsonException)
            {
                // Malformed model output degrades to an empty segment, not to a crash.
            }

            return new KnowledgeGraph();
        }

        private static string BuildExtractionSystemPrompt(string ontologySchemaJson) =>
            "You are a precise data extraction AI. Extract entities and relationships from the text " +
            $"based on this JSON ontology schema:\n{ontologySchemaJson}\n" +
            "Return strictly valid JSON matching the schema with 'Entities' and 'Relationships' arrays. " +
            "Every entity and every relationship MUST include a numeric 'ConfidenceScore' field in range 0.0..1.0 " +
            "expressing how certain you are that the fact is present in the text. " +
            "Do not add markdown formatting, conversational filler, or explanations.";

        private static string BuildGraphAnswerSystemPrompt(KnowledgeGraph context)
        {
            string contextJson = JsonSerializer.Serialize(context);
            return "You are an expert analytical assistant. Answer the user's question using ONLY the provided " +
                   "Knowledge Graph context below. If the answer is not contained in the context, explicitly state " +
                   $"that you do not have enough information.\nContext: {contextJson}";
        }

        private static string BuildCitationAnswerSystemPrompt(CitationContext context) =>
            "You are an expert analytical assistant. Answer using ONLY the numbered facts below. " +
            "Immediately after every claim derived from fact [n], place the marker [n] inline with no space. " +
            "If the facts are insufficient, explicitly state that you do not have enough information. " +
            $"Numbered facts:\n{context.PromptText}";

        private static string BuildOntologySystemPrompt() =>
            "You are an expert data architect and ontology engineer. Your task is to analyze the user's intent " +
            "and design a strict Knowledge Graph ontology schema. You must output ONLY a valid JSON object. " +
            "Do not add conversational text, markdown formatting, or explanations. The JSON must strictly follow this structure:\n" +
            "{\n" +
            "  \"ProfileName\": \"short_english_name\",\n" +
            "  \"DisplayName\": \"Human readable title in the user's language\",\n" +
            "  \"Description\": \"Short description of what this graph extracts\",\n" +
            "  \"Icon\": \"A single appropriate emoji\",\n" +
            "  \"OntologySchema\": {\n" +
            "    \"Entities\": [{ \"Type\": \"TargetEntity\", \"Description\": \"What it represents\" }],\n" +
            "    \"Relationships\": [{ \"RelationType\": \"RELATION_NAME\", \"Description\": \"How entities connect\" }]\n" +
            "  }\n" +
            "}";

        private static string CleanLlmJsonOutput(string text)
        {
            var start = text.IndexOf('{');
            var end = text.LastIndexOf('}');
            if (start >= 0 && end > start)
            {
                return text.Substring(start, end - start + 1);
            }

            return text;
        }

        private sealed class LlmOntologyResponse
        {
            public string ProfileName { get; set; } = string.Empty;
            public string DisplayName { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public string Icon { get; set; } = "📊";
            public JsonElement OntologySchema { get; set; }
        }
    }
}