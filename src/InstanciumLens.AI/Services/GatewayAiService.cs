using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using InstanciumLens.AI.Abstractions;
using InstanciumLens.AI.Configuration;
using InstanciumLens.AI.Contracts;
using InstanciumLens.Core.Interfaces;
using InstanciumLens.Core.Models;
using Microsoft.Extensions.Logging;

namespace InstanciumLens.AI.Services
{
    public sealed class GatewayAiService : ILocalModelService
    {
        private readonly IGatewayHubClient _gateway;
        private readonly GatewaySettings _settings;
        private readonly ILogger<GatewayAiService> _logger;

        public GatewayAiService(
            IGatewayHubClient gateway,
            GatewaySettings settings,
            ILogger<GatewayAiService> logger)
        {
            _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<KnowledgeGraph> ExtractEntitiesAsync(
            DocumentNode document,
            string ontologySchemaJson,
            CancellationToken cancellationToken = default)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            var pageStopwatch = Stopwatch.StartNew();
            _logger.LogInformation(
                "Extraction started for {FileName} page {PageNumber}",
                document.FileName,
                document.PageNumber);
            string response = await CompleteAsync(
                BuildExtractionSystemPrompt(ontologySchemaJson),
                $"Text to analyze: {document.RawText}",
                cancellationToken);
            KnowledgeGraph segment = ParseGraphSegment(response, document);
            _logger.LogInformation(
                "Extraction completed for {FileName} page {PageNumber} in {ElapsedSeconds:F1} s ({EntityCount} entities, {RelationshipCount} relationships)",
                document.FileName,
                document.PageNumber,
                pageStopwatch.Elapsed.TotalSeconds,
                segment.Entities.Count,
                segment.Relationships.Count);
            return segment;
        }

        public Task<string> AnswerQueryAsync(
            string userQuery,
            KnowledgeGraph contextGraph,
            CancellationToken cancellationToken = default)
        {
            if (contextGraph == null) throw new ArgumentNullException(nameof(contextGraph));
            return CompleteAsync(BuildGraphAnswerSystemPrompt(contextGraph), userQuery, cancellationToken);
        }

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

        public async Task<(GraphProfile Profile, string OntologySchemaJson)> GenerateOntologyProfileAsync(
            string userIntent,
            CancellationToken cancellationToken = default)
        {
            string response = await CompleteAsync(BuildOntologySystemPrompt(), userIntent, cancellationToken);
            string rawJson = SanitizeJsonKeys(CleanLlmJsonOutput(response));
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
                        DisplayName = GraphProfilePresentation.ResolveDisplayName(
                            llmResponse.DisplayName,
                            GraphProfilePresentation.TitleFromId(safeId)),
                        Description = GraphProfilePresentation.ResolveDescription(
                            llmResponse.Description,
                            $"Knowledge graph profile for domain '{safeId}'."),
                        Icon = GraphProfilePresentation.ResolveIcon(llmResponse.Icon),
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
            }
            var fallbackProfile = new GraphProfile
            {
                Id = "default_audit",
                DisplayName = "General Audit",
                Description = "Fallback profile created due to generation error.",
                DatabaseFile = "knowledge_graph_default.db",
                Icon = GraphProfilePresentation.FallbackIcon,
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
            string cleaned = SanitizeJsonKeys(CleanLlmJsonOutput(response));
            cleaned = RepairTruncatedJson(cleaned);

            KnowledgeGraph? graph = TryDeserializeStrict(cleaned, document);

            if (graph == null || (graph.Entities.Count == 0 && graph.Relationships.Count == 0))
            {
                KnowledgeGraph? rescued = TryRescueViaJsonDocument(cleaned);
                if (rescued != null && (rescued.Entities.Count > 0 || rescued.Relationships.Count > 0))
                {
                    _logger.LogInformation(
                        "Strict parse yielded nothing for {FileName} page {PageNumber}; rescued {EntityCount} entities and {RelationshipCount} relationships from an alternative JSON shape.",
                        document.FileName,
                        document.PageNumber,
                        rescued.Entities.Count,
                        rescued.Relationships.Count);
                    graph = rescued;
                }
            }

            if (graph != null && (graph.Entities.Count > 0 || graph.Relationships.Count > 0))
            {
                foreach (GraphEntity entity in graph.Entities)
                {
                    entity.SourceDocumentNodeIds.Add(document.Id);
                }
                return graph;
            }

            string head = cleaned.Length > 400 ? cleaned[..400] : cleaned;
            _logger.LogWarning(
                "Empty graph for {FileName} page {PageNumber}. Cleaned JSON length {Length}. Head: {Head}",
                document.FileName,
                document.PageNumber,
                cleaned.Length,
                head);
            return graph ?? new KnowledgeGraph();
        }

        private KnowledgeGraph? TryDeserializeStrict(string json, DocumentNode document)
        {
            try
            {
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                return JsonSerializer.Deserialize<KnowledgeGraph>(json, options);
            }
            catch (JsonException ex)
            {
                string tail = json.Length > 150 ? json[^150..] : json;
                _logger.LogError(ex, "JSON Parse Error. Length: {Length}, Tail: ...{Tail}", json.Length, tail);
                return null;
            }
        }

        private static KnowledgeGraph? TryRescueViaJsonDocument(string json)
        {
            try
            {
                using var document = JsonDocument.Parse(json);
                JsonElement root = document.RootElement;

                var entityElements = new List<JsonElement>();
                var relationElements = new List<JsonElement>();

                if (root.ValueKind == JsonValueKind.Array)
                {
                    entityElements.AddRange(root.EnumerateArray());
                }
                else if (root.ValueKind == JsonValueKind.Object)
                {
                    foreach (JsonProperty property in root.EnumerateObject())
                    {
                        if (property.Value.ValueKind != JsonValueKind.Array)
                        {
                            continue;
                        }

                        string normalized = property.Name
                            .Replace("_", "", StringComparison.Ordinal)
                            .Replace("-", "", StringComparison.Ordinal)
                            .ToLowerInvariant();

                        bool isEntityKey = normalized.Contains("entity") || normalized.Contains("node")
                            || normalized == "items" || normalized == "objects";
                        bool isRelationKey = normalized.Contains("relation") || normalized.Contains("edge")
                            || normalized.Contains("link") || normalized.Contains("connection");

                        if (isEntityKey)
                        {
                            entityElements.AddRange(property.Value.EnumerateArray());
                        }
                        else if (isRelationKey)
                        {
                            relationElements.AddRange(property.Value.EnumerateArray());
                        }
                        else if (entityElements.Count == 0 && LooksLikeEntityArray(property.Value))
                        {
                            entityElements.AddRange(property.Value.EnumerateArray());
                        }
                    }
                }
                else
                {
                    return null;
                }

                var graph = new KnowledgeGraph();
                var idMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                foreach (JsonElement element in entityElements)
                {
                    if (element.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    string name = GetString(element, "Name", "name", "title", "label") ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    var entity = new GraphEntity
                    {
                        Id = Guid.NewGuid().ToString(),
                        Name = name,
                        Type = GetString(element, "Type", "type", "category", "label") ?? "Entity",
                        Description = GetString(element, "Description", "description", "summary") ?? string.Empty,
                        ConfidenceScore = GetDouble(element, 0.5, "ConfidenceScore", "confidence", "score", "confidence_score")
                    };
                    graph.Entities.Add(entity);

                    string? originalId = GetString(element, "Id", "id", "uid", "entity_id");
                    if (!string.IsNullOrWhiteSpace(originalId))
                    {
                        idMap[originalId] = entity.Id;
                    }
                    idMap[name] = entity.Id;
                }

                foreach (JsonElement element in relationElements)
                {
                    if (element.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    string sourceRef = GetString(element, "SourceEntityId", "SourceEntityName", "source_id", "source", "from", "sourceEntity", "source_name", "Source") ?? string.Empty;
                    string targetRef = GetString(element, "TargetEntityId", "TargetEntityName", "target_id", "target", "to", "targetEntity", "target_name", "Target") ?? string.Empty;
                    string relationType = GetString(element, "RelationType", "relation_type", "relation", "predicate", "type", "label") ?? string.Empty;

                    if (!idMap.TryGetValue(sourceRef, out string? sourceId)
                        || !idMap.TryGetValue(targetRef, out string? targetId)
                        || string.IsNullOrWhiteSpace(relationType))
                    {
                        continue;
                    }

                    graph.Relationships.Add(new GraphRelationship
                    {
                        SourceEntityId = sourceId,
                        TargetEntityId = targetId,
                        RelationType = relationType,
                        Weight = GetDouble(element, 1.0, "Weight", "weight", "confidence")
                    });
                }

                return graph;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static bool LooksLikeEntityArray(JsonElement array)
        {
            foreach (JsonElement item in array.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    return false;
                }

                foreach (JsonProperty property in item.EnumerateObject())
                {
                    string normalized = property.Name.ToLowerInvariant();
                    if (normalized is "name" or "title" or "type" or "label" or "entity")
                    {
                        return true;
                    }
                }

                return false;
            }

            return false;
        }

        private static string? GetString(JsonElement obj, params string[] propertyNames)
        {
            foreach (string propertyName in propertyNames)
            {
                if (obj.TryGetProperty(propertyName, out JsonElement value)
                    && value.ValueKind == JsonValueKind.String)
                {
                    string result = value.GetString();
                    if (!string.IsNullOrWhiteSpace(result))
                    {
                        return result;
                    }
                }
            }

            return null;
        }

        private static double GetDouble(JsonElement obj, double fallback, params string[] propertyNames)
        {
            foreach (string propertyName in propertyNames)
            {
                if (obj.TryGetProperty(propertyName, out JsonElement value)
                    && value.ValueKind == JsonValueKind.Number
                    && value.TryGetDouble(out double result))
                {
                    return result;
                }
            }

            return fallback;
        }

        private static string BuildExtractionSystemPrompt(string ontologySchemaJson) =>
            "You are a precise data extraction AI. Your task is to extract entities and relationships from the user's text.\n\n" +
            "ALLOWED ONTOLOGY SCHEMA:\n" +
            $"{ontologySchemaJson}\n\n" +
            "OUTPUT FORMAT:\n" +
            "You MUST return a single valid JSON object with exactly two keys: \"Entities\" and \"Relationships\".\n" +
            "Every entity object MUST have these fields: \"Name\" (string, exact text from document), \"Type\" (string, must match one of the allowed Types from the schema), \"Description\" (string, 1-5 words), \"ConfidenceScore\" (number 0.0 to 1.0).\n" +
            "Every relationship object MUST have these fields: \"SourceEntityName\" (string, must match an extracted Entity Name), \"TargetEntityName\" (string, must match an extracted Entity Name), \"RelationType\" (string, must match one of the allowed RelationTypes from the schema), \"ConfidenceScore\" (number 0.0 to 1.0).\n\n" +
            "EXAMPLE OUTPUT:\n" +
            "{\n" +
            "  \"Entities\": [\n" +
            "    { \"Name\": \"Alessandro Volta\", \"Type\": \"Person\", \"Description\": \"Italian physicist\", \"ConfidenceScore\": 0.95 },\n" +
            "    { \"Name\": \"Volt\", \"Type\": \"Unit\", \"Description\": \"Unit of electric potential\", \"ConfidenceScore\": 0.99 }\n" +
            "  ],\n" +
            "  \"Relationships\": [\n" +
            "    { \"SourceEntityName\": \"Volt\", \"TargetEntityName\": \"Alessandro Volta\", \"RelationType\": \"NAMED_AFTER\", \"ConfidenceScore\": 0.9 }\n" +
            "  ]\n" +
            "}\n\n" +
            "RULES:\n" +
            "1. Return ONLY the raw JSON. NO markdown code blocks (```json), NO explanations, NO conversational text.\n" +
            "2. Extract ALL relevant entities and relationships present in the text. Do not be lazy.\n" +
            "3. If absolutely nothing relevant is found, return {\"Entities\":[], \"Relationships\":[]}.\n" +
            "4. Use the exact names/phrases as they appear in the text for entity Names.";

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
            "Do not add conversational text, markdown formatting, or explanations. " +
            "The string values inside the structure below are structural placeholders: never copy them verbatim, " +
            "always produce original values for the user's domain. The JSON must strictly follow this structure:\n" +
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
            if (string.IsNullOrWhiteSpace(text))
            {
                return text;
            }

            text = text.Trim();
            if (text.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
            {
                text = text["```json".Length..].TrimStart();
            }
            else if (text.StartsWith("```", StringComparison.OrdinalIgnoreCase))
            {
                text = text["```".Length..].TrimStart();
            }

            if (text.EndsWith("```"))
            {
                text = text[..^3].TrimEnd();
            }

            int start = text.IndexOf('{');
            int end = text.LastIndexOf('}');
            if (start >= 0 && end > start)
            {
                return text.Substring(start, end - start + 1);
            }

            if (start >= 0)
            {
                return text[start..];
            }

            return text;
        }

        private static string SanitizeJsonKeys(string json)
        {
            return Regex.Replace(json, @"(?<="")\s+(?=""\s*:)", "");
        }

        private static string RepairTruncatedJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return json;
            }

            int openBraces = 0;
            int openBrackets = 0;
            bool inString = false;
            bool escape = false;

            foreach (char symbol in json)
            {
                if (escape)
                {
                    escape = false;
                    continue;
                }
                if (symbol == '\\')
                {
                    escape = true;
                    continue;
                }
                if (symbol == '"')
                {
                    inString = !inString;
                    continue;
                }

                if (!inString)
                {
                    if (symbol == '{') openBraces++;
                    else if (symbol == '}') openBraces--;
                    else if (symbol == '[') openBrackets++;
                    else if (symbol == ']') openBrackets--;
                }
            }

            string repaired = json.TrimEnd();
            while (openBrackets > 0)
            {
                repaired += "]";
                openBrackets--;
            }
            while (openBraces > 0)
            {
                repaired += "}";
                openBraces--;
            }

            return Regex.Replace(repaired, @",\s*([\]}])", "$1");
        }

        private sealed class LlmOntologyResponse
        {
            public string ProfileName { get; set; } = string.Empty;
            public string DisplayName { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public string Icon { get; set; } = GraphProfilePresentation.FallbackIcon;
            public JsonElement OntologySchema { get; set; }
        }
    }
}