using Microsoft.Data.Sqlite;
using InstanciumLens.Core.Interfaces;
using InstanciumLens.Core.Models;

namespace InstanciumLens.Infrastructure.Data
{
    public class SqLiteGraphRepository : IGraphRepository
    {
        private readonly string _dbConnectionString;
        private readonly string _hiddenFolderPath;

        /// <summary>
        /// Constructor takes the path of the folder the user selected in the UI
        /// and an optional profileName to support multiple isolated graphs.
        /// </summary>
        public SqLiteGraphRepository(string targetFolderPath, string profileName = "default")
        {
            if (string.IsNullOrWhiteSpace(targetFolderPath))
                throw new ArgumentNullException(nameof(targetFolderPath));

            // Define the hidden folder structure
            _hiddenFolderPath = Path.Combine(targetFolderPath, ".instancium");

            // Create the hidden folder if it doesn't exist
            if (!Directory.Exists(_hiddenFolderPath))
            {
                var directoryInfo = Directory.CreateDirectory(_hiddenFolderPath);
                // Make the directory hidden using Windows file attributes
                directoryInfo.Attributes |= FileAttributes.Hidden;
            }

            // Sanitize the profile name to ensure it's a valid file name
            string safeProfileName = string.IsNullOrWhiteSpace(profileName)
                ? "default"
                : string.Join("_", profileName.Split(Path.GetInvalidFileNameChars()));

            // Creates dynamically named DB: e.g., knowledge_graph_legal.db or knowledge_graph_kip.db
            string dbPath = Path.Combine(_hiddenFolderPath, $"knowledge_graph_{safeProfileName}.db");
            _dbConnectionString = $"Data Source={dbPath}";

            InitializeDatabase();
        }

        private void InitializeDatabase()
        {
            using var connection = new SqliteConnection(_dbConnectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = @"
                CREATE TABLE IF NOT EXISTS Entities (
                    Id TEXT PRIMARY KEY,
                    Name TEXT NOT NULL,
                    Type TEXT NOT NULL,
                    Description TEXT
                );

                CREATE TABLE IF NOT EXISTS Relationships (
                    SourceEntityId TEXT NOT NULL,
                    TargetEntityId TEXT NOT NULL,
                    RelationType TEXT NOT NULL,
                    Description TEXT,
                    Weight REAL,
                    PRIMARY KEY (SourceEntityId, TargetEntityId, RelationType)
                );
            ";
            command.ExecuteNonQuery();
        }

        public async Task SaveGraphSegmentAsync(KnowledgeGraph graphSegment, CancellationToken cancellationToken = default)
        {
            using var connection = new SqliteConnection(_dbConnectionString);
            await connection.OpenAsync(cancellationToken);
            using var transaction = connection.BeginTransaction();

            try
            {
                // Save Entities
                foreach (var entity in graphSegment.Entities)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var cmd = connection.CreateCommand();
                    cmd.Transaction = transaction;
                    cmd.CommandText = @"
                        INSERT OR REPLACE INTO Entities (Id, Name, Type, Description)
                        VALUES ($id, $name, $type, $desc)";
                    cmd.Parameters.AddWithValue("$id", entity.Id);
                    cmd.Parameters.AddWithValue("$name", entity.Name);
                    cmd.Parameters.AddWithValue("$type", entity.Type);
                    cmd.Parameters.AddWithValue("$desc", entity.Description);
                    await cmd.ExecuteNonQueryAsync(cancellationToken);
                }

                // Save Relationships
                foreach (var rel in graphSegment.Relationships)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var cmd = connection.CreateCommand();
                    cmd.Transaction = transaction;
                    cmd.CommandText = @"
                        INSERT OR REPLACE INTO Relationships (SourceEntityId, TargetEntityId, RelationType, Description, Weight)
                        VALUES ($source, $target, $relType, $desc, $weight)";
                    cmd.Parameters.AddWithValue("$source", rel.SourceEntityId);
                    cmd.Parameters.AddWithValue("$target", rel.TargetEntityId);
                    cmd.Parameters.AddWithValue("$relType", rel.RelationType);
                    cmd.Parameters.AddWithValue("$desc", rel.Description);
                    cmd.Parameters.AddWithValue("$weight", rel.Weight);
                    await cmd.ExecuteNonQueryAsync(cancellationToken);
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

            // Load Entities
            var entityCmd = connection.CreateCommand();
            entityCmd.CommandText = "SELECT Id, Name, Type, Description FROM Entities";
            using (var reader = await entityCmd.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    graph.Entities.Add(new GraphEntity
                    {
                        Id = reader.GetString(0),
                        Name = reader.GetString(1),
                        Type = reader.GetString(2),
                        Description = reader.IsDBNull(3) ? string.Empty : reader.GetString(3)
                    });
                }
            }

            // Load Relationships
            var relCmd = connection.CreateCommand();
            relCmd.CommandText = "SELECT SourceEntityId, TargetEntityId, RelationType, Description, Weight FROM Relationships";
            using (var reader = await relCmd.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    graph.Relationships.Add(new GraphRelationship
                    {
                        SourceEntityId = reader.GetString(0),
                        TargetEntityId = reader.GetString(1),
                        RelationType = reader.GetString(2),
                        Description = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                        Weight = reader.GetDouble(4)
                    });
                }
            }

            return graph;
        }

        public async Task<KnowledgeGraph> GetRelevantSubGraphAsync(IEnumerable<string> keywords, CancellationToken cancellationToken = default)
        {
            var subGraph = new KnowledgeGraph();
            var keywordList = keywords?.Where(k => !string.IsNullOrWhiteSpace(k)).ToList();

            // If no keywords provided, return an empty graph
            if (keywordList == null || !keywordList.Any())
                return subGraph;

            using var connection = new SqliteConnection(_dbConnectionString);
            await connection.OpenAsync(cancellationToken);

            // ==========================================
            // STEP 1: Find matching edges (Relationships)
            // ==========================================
            var command = connection.CreateCommand();
            var likeConditions = new List<string>();

            // Build safe parameterized LIKE queries to prevent SQL injection
            for (int i = 0; i < keywordList.Count; i++)
            {
                string pName = $"@p{i}";
                likeConditions.Add($"(e.Name LIKE {pName} OR e.Description LIKE {pName})");
                command.Parameters.AddWithValue(pName, $"%{keywordList[i]}%");
            }

            string likeClause = string.Join(" OR ", likeConditions);

            // SQL: Join relationships with entities. If an entity matches our keywords, 
            // extract all relationships connected to it (both as Source and Target).
            command.CommandText = $@"
                SELECT r.SourceEntityId, r.TargetEntityId, r.RelationType, r.Description, r.Weight
                FROM Relationships r
                JOIN Entities e ON r.SourceEntityId = e.Id OR r.TargetEntityId = e.Id
                WHERE {likeClause}";

            var connectedEntityIds = new HashSet<string>();

            using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    var rel = new GraphRelationship
                    {
                        SourceEntityId = reader.GetString(0),
                        TargetEntityId = reader.GetString(1),
                        RelationType = reader.GetString(2),
                        Description = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                        Weight = reader.GetDouble(4)
                    };

                    subGraph.Relationships.Add(rel);

                    // Keep track of all unique node IDs involved in this sub-graph
                    connectedEntityIds.Add(rel.SourceEntityId);
                    connectedEntityIds.Add(rel.TargetEntityId);
                }
            }

            // ==========================================
            // STEP 2: Fetch full data for the connected nodes
            // ==========================================
            if (connectedEntityIds.Any())
            {
                var entityCmd = connection.CreateCommand();
                var idParameters = new List<string>();

                int index = 0;
                foreach (var id in connectedEntityIds)
                {
                    string pName = $"@id{index}";
                    idParameters.Add(pName);
                    entityCmd.Parameters.AddWithValue(pName, id);
                    index++;
                }

                string inClause = string.Join(",", idParameters);

                // Fetch the actual Entities using the collected IDs
                entityCmd.CommandText = $"SELECT Id, Name, Type, Description FROM Entities WHERE Id IN ({inClause})";

                using (var reader = await entityCmd.ExecuteReaderAsync(cancellationToken))
                {
                    while (await reader.ReadAsync(cancellationToken))
                    {
                        subGraph.Entities.Add(new GraphEntity
                        {
                            Id = reader.GetString(0),
                            Name = reader.GetString(1),
                            Type = reader.GetString(2),
                            Description = reader.IsDBNull(3) ? string.Empty : reader.GetString(3)
                        });
                    }
                }
            }

            return subGraph;
        }
    }
}