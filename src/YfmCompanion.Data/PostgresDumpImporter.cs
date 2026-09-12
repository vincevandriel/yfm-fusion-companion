using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace YfmCompanion.Data;

public static class PostgresDumpImporter
{
    public const string ImporterVersion = "1";

    private static readonly HashSet<string> IncludedTables = new(StringComparer.OrdinalIgnoreCase)
    {
        "sources", "cards", "categories", "card_categories", "fusion_rules",
        "general_fusion_rules", "exact_fusion_rules", "rule_index", "rule_operand_cards",
        "fusion_rule_conflicts", "fusion_pairs", "fusion_pair_rule_refs",
        "fusion_conflict_pairs", "attack_rule_exceptions", "equip_rules",
        "equip_compatibility", "metadata_differences", "known_disputed_checks"
    };

    private static readonly IReadOnlyDictionary<string, long> RequiredCounts =
        new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
        {
            ["cards"] = 722,
            ["categories"] = 44,
            ["card_categories"] = 1_338,
            ["fusion_pairs"] = 25_146,
            ["intended_fusion_pairs"] = 25_131,
            ["glitch_fusion_pairs"] = 15,
            ["fusion_pair_rule_refs"] = 25_151,
            ["general_fusion_rules"] = 104,
            ["exact_fusion_rules"] = 149,
            ["rule_index"] = 491,
            ["fusion_conflict_pairs"] = 1_777,
            ["attack_rule_exceptions"] = 179
        };

    public static DataBuildReport Import(string sourceSqlPath, string outputDatabasePath)
    {
        var sourceFullPath = Path.GetFullPath(sourceSqlPath);
        var outputFullPath = Path.GetFullPath(outputDatabasePath);
        if (!File.Exists(sourceFullPath))
        {
            throw new FileNotFoundException("The PostgreSQL source file was not found.", sourceFullPath);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputFullPath)!);
        var tempPath = outputFullPath + ".building";
        if (File.Exists(tempPath))
        {
            File.Delete(tempPath);
        }

        var sourceBytes = File.ReadAllBytes(sourceFullPath);
        var sourceHash = Convert.ToHexString(SHA256.HashData(sourceBytes)).ToLowerInvariant();
        var sql = System.Text.Encoding.UTF8.GetString(sourceBytes);
        var imported = IncludedTables.ToDictionary(table => table, _ => 0L, StringComparer.OrdinalIgnoreCase);

        try
        {
            using (var connection = new SqliteConnection($"Data Source={tempPath};Pooling=False"))
            {
                connection.Open();
                using (var schema = connection.CreateCommand())
                {
                    schema.CommandText = SqliteSchema.Create;
                    schema.ExecuteNonQuery();
                }

                using var transaction = connection.BeginTransaction();
                foreach (var batch in PostgresDumpParser.Parse(sql, IncludedTables))
                {
                    InsertBatch(connection, transaction, batch);
                    imported[batch.Table] += batch.Rows.LongLength;
                }

                using (var manifest = connection.CreateCommand())
                {
                    manifest.Transaction = transaction;
                    manifest.CommandText = """
                        INSERT INTO source_manifest
                          (manifest_id, source_file_name, source_sha256, source_length, importer_version)
                        VALUES (1, $name, $sha256, $length, $version);
                        """;
                    manifest.Parameters.AddWithValue("$name", Path.GetFileName(sourceFullPath));
                    manifest.Parameters.AddWithValue("$sha256", sourceHash);
                    manifest.Parameters.AddWithValue("$length", sourceBytes.LongLength);
                    manifest.Parameters.AddWithValue("$version", ImporterVersion);
                    manifest.ExecuteNonQuery();
                }

                transaction.Commit();
                Validate(connection);

                using var vacuum = connection.CreateCommand();
                vacuum.CommandText = "VACUUM;";
                vacuum.ExecuteNonQuery();
            }

            File.Move(tempPath, outputFullPath, true);
        }
        catch
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            throw;
        }

        return new DataBuildReport(
            sourceFullPath,
            outputFullPath,
            sourceHash,
            sourceBytes.LongLength,
            imported,
            ReadIntegrityCounts(outputFullPath));
    }

    public static IReadOnlyDictionary<string, long> ReadIntegrityCounts(string databasePath)
    {
        using var connection = new SqliteConnection($"Data Source={Path.GetFullPath(databasePath)};Mode=ReadOnly;Pooling=False");
        connection.Open();
        var counts = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in IntegrityQueries)
        {
            using var command = connection.CreateCommand();
            command.CommandText = entry.Value;
            counts[entry.Key] = (long)(command.ExecuteScalar() ?? throw new InvalidDataException(entry.Key));
        }

        return counts;
    }

    private static readonly Dictionary<string, string> IntegrityQueries =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["cards"] = "SELECT count(*) FROM cards",
            ["categories"] = "SELECT count(*) FROM categories",
            ["card_categories"] = "SELECT count(*) FROM card_categories",
            ["fusion_pairs"] = "SELECT count(*) FROM fusion_pairs",
            ["intended_fusion_pairs"] = "SELECT count(*) FROM fusion_pairs WHERE is_intended = 1",
            ["glitch_fusion_pairs"] = "SELECT count(*) FROM fusion_pairs WHERE is_glitch = 1",
            ["fusion_pair_rule_refs"] = "SELECT count(*) FROM fusion_pair_rule_refs",
            ["general_fusion_rules"] = "SELECT count(*) FROM general_fusion_rules",
            ["exact_fusion_rules"] = "SELECT count(*) FROM exact_fusion_rules",
            ["rule_index"] = "SELECT count(*) FROM rule_index",
            ["fusion_conflict_pairs"] = "SELECT count(*) FROM fusion_conflict_pairs",
            ["attack_rule_exceptions"] = "SELECT count(*) FROM attack_rule_exceptions"
        };

    private static void InsertBatch(SqliteConnection connection, SqliteTransaction transaction, InsertBatch batch)
    {
        var columnSql = string.Join(",", batch.Columns.Select(QuoteIdentifier));
        var parameterSql = string.Join(",", batch.Columns.Select((_, index) => $"$p{index}"));
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"INSERT INTO {QuoteIdentifier(batch.Table)} ({columnSql}) VALUES ({parameterSql});";
        for (var index = 0; index < batch.Columns.Count; index++)
        {
            command.Parameters.Add(new SqliteParameter($"$p{index}", DBNull.Value));
        }

        foreach (var row in batch.Rows)
        {
            if (row.Count != batch.Columns.Count)
            {
                throw new InvalidDataException($"{batch.Table}: expected {batch.Columns.Count} values, found {row.Count}.");
            }

            for (var index = 0; index < row.Count; index++)
            {
                command.Parameters[index].Value = row[index] switch
                {
                    null => DBNull.Value,
                    bool boolean => boolean ? 1L : 0L,
                    _ => row[index]!
                };
            }

            command.ExecuteNonQuery();
        }
    }

    private static void Validate(SqliteConnection connection)
    {
        foreach (var expectation in RequiredCounts)
        {
            var actual = ExecuteCount(connection, IntegrityQueries[expectation.Key]);
            if (actual != expectation.Value)
            {
                throw new InvalidDataException(
                    $"Integrity check '{expectation.Key}' expected {expectation.Value:N0}, found {actual:N0}.");
            }
        }

        using var foreignKeys = connection.CreateCommand();
        foreignKeys.CommandText = "PRAGMA foreign_key_check;";
        using var reader = foreignKeys.ExecuteReader();
        if (reader.Read())
        {
            throw new InvalidDataException(
                $"Foreign-key failure in table '{reader.GetString(0)}', rowid {reader.GetValue(1)}.");
        }
    }

    private static long ExecuteCount(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (long)(command.ExecuteScalar() ?? throw new InvalidDataException(sql));
    }

    private static string QuoteIdentifier(string identifier)
    {
        if (identifier.Length == 0 || identifier.Any(character => !(char.IsAsciiLetterOrDigit(character) || character == '_')))
        {
            throw new InvalidDataException($"Invalid SQL identifier '{identifier}'.");
        }

        return $"\"{identifier}\"";
    }
}
