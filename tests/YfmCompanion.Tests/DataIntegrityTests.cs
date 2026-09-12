using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using YfmCompanion.Data;

namespace YfmCompanion.Tests;

[Collection(DatabaseCollection.Name)]
public sealed class DataIntegrityTests(DatabaseFixture fixture)
{
    [Fact]
    public void SourceQaCountsArePreserved()
    {
        var expected = new Dictionary<string, long>
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

        Assert.Equal(expected, fixture.Report.IntegrityCounts);
    }

    [Fact]
    public void SourceManifestMatchesActualSqlBytes()
    {
        var bytes = File.ReadAllBytes(fixture.SourcePath);
        var actualHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        Assert.Equal(actualHash, fixture.Report.SourceSha256);
        Assert.Equal(bytes.LongLength, fixture.Report.SourceLength);

        using var connection = new SqliteConnection($"Data Source={fixture.DatabasePath};Mode=ReadOnly;Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT source_sha256, source_length, importer_version FROM source_manifest WHERE manifest_id = 1";
        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal(actualHash, reader.GetString(0));
        Assert.Equal(bytes.LongLength, reader.GetInt64(1));
        Assert.Equal(PostgresDumpImporter.ImporterVersion, reader.GetString(2));
    }

    [Fact]
    public void SQLiteForeignKeysAreValid()
    {
        using var connection = new SqliteConnection($"Data Source={fixture.DatabasePath};Mode=ReadOnly;Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_key_check;";
        using var reader = command.ExecuteReader();
        Assert.False(reader.Read());
    }

    [Fact]
    public void RegenerationIsByteForByteDeterministic()
    {
        var secondPath = Path.Combine(fixture.DirectoryPath, "second.db");
        PostgresDumpImporter.Import(fixture.SourcePath, secondPath);

        var firstHash = SHA256.HashData(File.ReadAllBytes(fixture.DatabasePath));
        var secondHash = SHA256.HashData(File.ReadAllBytes(secondPath));
        Assert.Equal(firstHash, secondHash);
    }
}
