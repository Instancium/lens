// File: src/InstanciumLens.Infrastructure/Data/SqLiteGraphRepository.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using InstanciumLens.Core.Interfaces;
using InstanciumLens.Core.Models;
using Microsoft.Data.Sqlite;

namespace InstanciumLens.Infrastructure.Data
{
    public class SqLiteGraphRepository : IGraphRepository
    {
        private const string EntityColumns =
            "Id, Name, Type, Description, NodeKind, RuleExpression, Standard, Severity, SourceText, ConfidenceScore, SourceDocumentNodeIds";

        private readonly string _dbConnectionString;
        private readonly string _hiddenFolderPath;

        public SqLiteGraphRepository(string targetFolderPath, string profileName = "default")
            : this(targetFolderPath, profileName, null)
        {
        }
        public SqLiteGraphRepository(string targetFolderPath, string profileName, string? branchId)
        {
            if (string.IsNullOrWhiteSpace(targetFolderPath))
            {
                throw new ArgumentNullException(nameof(targetFolderPath));
            }

            _hiddenFolderPath = Path.Combine(targetFolderPath, ".instancium");
            EnsureHiddenDirectory(_hiddenFolderPath);

            string safeProfileName = SanitizeName(profileName);
            string dbPath;

            if (string.IsNullOrWhiteSpace(branchId))
            {
                dbPath = Path.Combine(_hiddenFolderPath, $"knowledge_graph_{safeProfileName}.db");
            }
            else
            {
                string branchFolder = Path.Combine(_hiddenFolderPath, "branches", safeProfileName);
                Directory.CreateDirectory(branchFolder);
                dbPath = Path.Combine(branchFolder, $"{SanitizeName(branchId)}.db");
            }

            _dbConnectionString = $"Data Source={dbPath}";
            InitializeDatabase();
        }

        private void InitializeDatabase()
        {
            using var connection = new SqliteConnection(_dbConnectionString);
            connection.Open();

            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
CREATE TABLE IF NOT EXISTS Entities (
    Id TEXT PRIMARY KEY,
    Name TEXT NOT NULL,
    Type TEXT NOT NULL,
    Description TEXT,
    NodeKind TEXT NOT NULL DEFAULT 'Entity',
    RuleExpression TEXT,
    Standard TEXT,
    Severity TEXT,
    SourceText TEXT,
    ConfidenceScore REAL NOT NULL DEFAULT 1.0,
    SourceDocumentNodeIds TEXT NOT NULL DEFAULT '[]'
);

CREATE TABLE IF NOT EXISTS Relationships (
    SourceEntityId TEXT NOT NULL,
    TargetEntityId TEXT NOT NULL,
    RelationType TEXT NOT NULL,
    Description TEXT,
    Weight REAL,
    PRIMARY KEY (SourceEntityId, TargetEntityId, RelationType)
);

CREATE INDEX IF NOT EXISTS IX_Relationships_SourceEntityId ON Relationships(SourceEntityId);
CREATE INDEX IF NOT EXISTS IX_Relationships_TargetEntityId ON Relationships(TargetEntityId);
CREATE INDEX IF NOT EXISTS IX_Relationships_RelationType ON Relationships(RelationType);
CREATE INDEX IF NOT EXISTS IX_Entities_Type ON Entities(Type);
CREATE INDEX IF NOT EXISTS IX_Entities_NodeKind ON Entities(NodeKind);
";

                command.ExecuteNonQuery();
            }
            EnsureEntityColumn(connection, "NodeKind", "TEXT NOT NULL DEFAULT 'Entity'");
            EnsureEntityColumn(connection, "RuleExpression", "TEXT");
            EnsureEntityColumn(connection, "Standard", "TEXT");
            EnsureEntityColumn(connection, "Severity", "TEXT");
            EnsureEntityColumn(connection, "SourceText", "TEXT");
            EnsureEntityColumn(connection, "ConfidenceScore", "REAL NOT NULL DEFAULT 1.0");
            EnsureEntityColumn(connection, "SourceDocumentNodeIds", "TEXT NOT NULL DEFAULT '[]'");
        }

        private static void EnsureEntityColumn(SqliteConnection connection, string columnName, string columnDefinition)
        {
            bool exists = false;

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA table_info(Entities);";

                using var reader = command.ExecuteReader();

                while (reader.Read())
                {
                    if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
                    {
                        exists = true;
                        break;
                    }
                }
            }

            if (exists)
            {
                return;
            }

            using var alterCommand = connection.CreateCommand();
            alterCommand.CommandText = $"ALTER TABLE Entities ADD COLUMN {columnName} {columnDefinition};";
            alterCommand.ExecuteNonQuery();
        }

        public async Task SaveGraphSegmentAsync(KnowledgeGraph graphSegment, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(graphSegment);

            using var connection = new SqliteConnection(_dbConnectionString);
            await connection.OpenAsync(cancellationToken);

            using var transaction = connection.BeginTransaction();

            try
            {
                foreach (GraphEntity entity in graphSegment.Entities)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    using var command = CreateEntityCommand(connection, transaction, entity);
                    await command.ExecuteNonQueryAsync(cancellationToken);
                }

                foreach (GraphRelationship relationship in graphSegment.Relationships)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    using var command = CreateRelationshipCommand(connection, transaction, relationship);
                    await command.ExecuteNonQueryAsync(cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }
        public async Task SaveChangesAsync(GraphChange change, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(change);

            using var connection = new SqliteConnection(_dbConnectionString);
            await connection.OpenAsync(cancellationToken);

            using var transaction = connection.BeginTransaction();

            try
            {
                foreach (GraphEntity entity in change.AddedOrUpdatedNodes)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    using var command = CreateEntityCommand(connection, transaction, entity);
                    await command.ExecuteNonQueryAsync(cancellationToken);
                }

                foreach (GraphRelationship relationship in change.AddedOrUpdatedRelationships)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    using var command = CreateRelationshipCommand(connection, transaction, relationship);
                    await command.ExecuteNonQueryAsync(cancellationToken);
                }

                foreach (string nodeId in change.RemovedNodeIds)
                {
                    if (string.IsNullOrWhiteSpace(nodeId))
                    {
                        continue;
                    }

                    cancellationToken.ThrowIfCancellationRequested();

                    using (var entityCommand = connection.CreateCommand())
                    {
                        entityCommand.Transaction = transaction;
                        entityCommand.CommandText = "DELETE FROM Entities WHERE Id = $id";
                        entityCommand.Parameters.AddWithValue("$id", nodeId);
                        await entityCommand.ExecuteNonQueryAsync(cancellationToken);
                    }

                    using (var relationshipCommand = connection.CreateCommand())
                    {
                        relationshipCommand.Transaction = transaction;
                        relationshipCommand.CommandText =
                            "DELETE FROM Relationships WHERE SourceEntityId = $id OR TargetEntityId = $id";
                        relationshipCommand.Parameters.AddWithValue("$id", nodeId);
                        await relationshipCommand.ExecuteNonQueryAsync(cancellationToken);
                    }
                }

                foreach (GraphRelationshipKey? key in change.RemovedRelationships)
                {
                    if (key == null)
                    {
                        continue;
                    }

                    cancellationToken.ThrowIfCancellationRequested();

                    using var command = connection.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText = @"
DELETE FROM Relationships
WHERE SourceEntityId = $source
  AND TargetEntityId = $target
  AND RelationType = $relationType";

                    command.Parameters.AddWithValue("$source", key.SourceEntityId);
                    command.Parameters.AddWithValue("$target", key.TargetEntityId);
                    command.Parameters.AddWithValue("$relationType", key.RelationType);

                    await command.ExecuteNonQueryAsync(cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }

        public async Task<KnowledgeGraph> LoadFullGraphAsync(CancellationToken cancellationToken = default)
        {
            var graph = new KnowledgeGraph();

            using var connection = new SqliteConnection(_dbConnectionString);
            await connection.OpenAsync(cancellationToken);

            using (var entityCommand = connection.CreateCommand())
            {
                entityCommand.CommandText = $"SELECT {EntityColumns} FROM Entities";

                using var reader = await entityCommand.ExecuteReaderAsync(cancellationToken);

                while (await reader.ReadAsync(cancellationToken))
                {
                    graph.Entities.Add(ReadEntity(reader));
                }
            }

            using (var relationshipCommand = connection.CreateCommand())
            {
                relationshipCommand.CommandText =
                    "SELECT SourceEntityId, TargetEntityId, RelationType, Description, Weight FROM Relationships";

                using var reader = await relationshipCommand.ExecuteReaderAsync(cancellationToken);

                while (await reader.ReadAsync(cancellationToken))
                {
                    graph.Relationships.Add(ReadRelationship(reader));
                }
            }

            return graph;
        }

        public async Task<KnowledgeGraph> GetRelevantSubGraphAsync(
            IEnumerable<string> keywords,
            CancellationToken cancellationToken = default)
        {
            var subGraph = new KnowledgeGraph();

            List<string>? keywordList = keywords?
                .Where(keyword => !string.IsNullOrWhiteSpace(keyword))
                .ToList();

            if (keywordList == null || keywordList.Count == 0)
            {
                return subGraph;
            }

            using var connection = new SqliteConnection(_dbConnectionString);
            await connection.OpenAsync(cancellationToken);

            using var relationshipCommand = connection.CreateCommand();
            var likeConditions = new List<string>();

            for (int i = 0; i < keywordList.Count; i++)
            {
                string parameterName = $"@p{i}";
                likeConditions.Add($"(e.Name LIKE {parameterName} OR e.Description LIKE {parameterName})");
                relationshipCommand.Parameters.AddWithValue(parameterName, $"%{keywordList[i]}%");
            }

            string likeClause = string.Join(" OR ", likeConditions);

            relationshipCommand.CommandText = $@"
SELECT r.SourceEntityId, r.TargetEntityId, r.RelationType, r.Description, r.Weight
FROM Relationships r
JOIN Entities e ON r.SourceEntityId = e.Id OR r.TargetEntityId = e.Id
WHERE {likeClause}";

            var connectedEntityIds = new HashSet<string>(StringComparer.Ordinal);

            using (var reader = await relationshipCommand.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    GraphRelationship relationship = ReadRelationship(reader);
                    subGraph.Relationships.Add(relationship);

                    connectedEntityIds.Add(relationship.SourceEntityId);
                    connectedEntityIds.Add(relationship.TargetEntityId);
                }
            }

            if (connectedEntityIds.Count > 0)
            {
                using var entityCommand = connection.CreateCommand();
                var idParameters = new List<string>();
                int index = 0;

                foreach (string id in connectedEntityIds)
                {
                    string parameterName = $"@id{index}";
                    idParameters.Add(parameterName);
                    entityCommand.Parameters.AddWithValue(parameterName, id);
                    index++;
                }

                string inClause = string.Join(",", idParameters);
                entityCommand.CommandText = $"SELECT {EntityColumns} FROM Entities WHERE Id IN ({inClause})";

                using var entityReader = await entityCommand.ExecuteReaderAsync(cancellationToken);

                while (await entityReader.ReadAsync(cancellationToken))
                {
                    subGraph.Entities.Add(ReadEntity(entityReader));
                }
            }

            return subGraph;
        }

        private static SqliteCommand CreateEntityCommand(
            SqliteConnection connection,
            SqliteTransaction transaction,
            GraphEntity entity)
        {
            var command = connection.CreateCommand();
            command.Transaction = transaction;

            command.CommandText = @"
INSERT OR REPLACE INTO Entities (
    Id,
    Name,
    Type,
    Description,
    NodeKind,
    RuleExpression,
    Standard,
    Severity,
    SourceText,
    ConfidenceScore,
    SourceDocumentNodeIds
)
VALUES (
    $id,
    $name,
    $type,
    $description,
    $nodeKind,
    $ruleExpression,
    $standard,
    $severity,
    $sourceText,
    $confidenceScore,
    $sourceDocumentNodeIds
)";

            command.Parameters.AddWithValue("$id", entity.Id);
            command.Parameters.AddWithValue("$name", entity.Name);
            command.Parameters.AddWithValue("$type", entity.Type);
            command.Parameters.AddWithValue("$description", ToDbValue(entity.Description));
            command.Parameters.AddWithValue("$nodeKind", entity.NodeKind.ToString());
            command.Parameters.AddWithValue("$ruleExpression", ToDbValue(entity.RuleExpression));
            command.Parameters.AddWithValue("$standard", ToDbValue(entity.Standard));
            command.Parameters.AddWithValue("$severity", ToDbValue(entity.Severity?.ToString()));
            command.Parameters.AddWithValue("$sourceText", ToDbValue(entity.SourceText));
            command.Parameters.AddWithValue("$confidenceScore", entity.ConfidenceScore);
            command.Parameters.AddWithValue(
                "$sourceDocumentNodeIds",
                JsonSerializer.Serialize(entity.SourceDocumentNodeIds ?? new List<string>()));

            return command;
        }

        private static SqliteCommand CreateRelationshipCommand(
            SqliteConnection connection,
            SqliteTransaction transaction,
            GraphRelationship relationship)
        {
            var command = connection.CreateCommand();
            command.Transaction = transaction;

            command.CommandText = @"
INSERT OR REPLACE INTO Relationships (
    SourceEntityId,
    TargetEntityId,
    RelationType,
    Description,
    Weight
)
VALUES (
    $source,
    $target,
    $relationType,
    $description,
    $weight
)";

            command.Parameters.AddWithValue("$source", relationship.SourceEntityId);
            command.Parameters.AddWithValue("$target", relationship.TargetEntityId);
            command.Parameters.AddWithValue("$relationType", relationship.RelationType);
            command.Parameters.AddWithValue("$description", ToDbValue(relationship.Description));
            command.Parameters.AddWithValue("$weight", relationship.Weight);

            return command;
        }

        private static GraphEntity ReadEntity(SqliteDataReader reader)
        {
            return new GraphEntity
            {
                Id = reader.GetString(0),
                Name = reader.GetString(1),
                Type = reader.GetString(2),
                Description = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                NodeKind = ParseNodeKind(reader.IsDBNull(4) ? null : reader.GetString(4)),
                RuleExpression = reader.IsDBNull(5) ? null : reader.GetString(5),
                Standard = reader.IsDBNull(6) ? null : reader.GetString(6),
                Severity = ParseSeverity(reader.IsDBNull(7) ? null : reader.GetString(7)),
                SourceText = reader.IsDBNull(8) ? null : reader.GetString(8),
                ConfidenceScore = reader.IsDBNull(9) ? 1.0 : reader.GetDouble(9),
                SourceDocumentNodeIds = ParseSourceDocumentNodeIds(reader.IsDBNull(10) ? null : reader.GetString(10))
            };
        }

        private static GraphRelationship ReadRelationship(SqliteDataReader reader)
        {
            return new GraphRelationship
            {
                SourceEntityId = reader.GetString(0),
                TargetEntityId = reader.GetString(1),
                RelationType = reader.GetString(2),
                Description = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                Weight = reader.IsDBNull(4) ? 1.0 : reader.GetDouble(4)
            };
        }

        private static GraphNodeKind ParseNodeKind(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return GraphNodeKind.Entity;
            }

            return Enum.TryParse<GraphNodeKind>(value, ignoreCase: true, out GraphNodeKind result)
                ? result
                : GraphNodeKind.Entity;
        }

        private static ValidationSeverity? ParseSeverity(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return Enum.TryParse<ValidationSeverity>(value, ignoreCase: true, out ValidationSeverity result)
                ? result
                : null;
        }

        private static List<string> ParseSourceDocumentNodeIds(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new List<string>();
            }

            try
            {
                return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
            }
            catch (JsonException)
            {
                return new List<string>();
            }
        }

        private static object ToDbValue(string? value) =>
            value is null ? DBNull.Value : value;

        private static void EnsureHiddenDirectory(string path)
        {
            var directory = new DirectoryInfo(path);

            if (!directory.Exists)
            {
                directory.Create();
            }

            if ((directory.Attributes & FileAttributes.Hidden) != FileAttributes.Hidden)
            {
                directory.Attributes |= FileAttributes.Hidden;
            }
        }

        private static string SanitizeName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "default";
            }

            char[] invalidChars = Path.GetInvalidFileNameChars();

            string result = new string(value
                .Select(ch => invalidChars.Contains(ch) || char.IsWhiteSpace(ch)
                    ? '_'
                    : char.ToLowerInvariant(ch))
                .ToArray());

            return string.IsNullOrWhiteSpace(result) ? "default" : result;
        }
    }
}