using YfmCompanion.Data;

namespace YfmCompanion.Tests;

public sealed class DatabaseFixture : IDisposable
{
    public DatabaseFixture()
    {
        SourcePath = Environment.GetEnvironmentVariable("YFM_SQL_PATH")
            ?? FindRepositorySql();
        DirectoryPath = Path.Combine(Path.GetTempPath(), "YfmCompanionTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DirectoryPath);
        DatabasePath = Path.Combine(DirectoryPath, "yfm.db");
        Report = PostgresDumpImporter.Import(SourcePath, DatabasePath);
        Catalog = FusionCatalog.Load(DatabasePath);
    }

    public string SourcePath { get; }
    public string DirectoryPath { get; }
    public string DatabasePath { get; }
    public DataBuildReport Report { get; }
    public FusionCatalog Catalog { get; }

    private static string FindRepositorySql()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "database-source", "YuGiOh_Forbidden_Memories_PostgreSQL.sql");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("The repository SQL source was not found. Set YFM_SQL_PATH to an explicit source file.");
    }

    public void Dispose()
    {
        if (Directory.Exists(DirectoryPath))
        {
            Directory.Delete(DirectoryPath, recursive: true);
        }
    }
}

[CollectionDefinition(Name)]
public sealed class DatabaseCollection : ICollectionFixture<DatabaseFixture>
{
    public const string Name = "Generated database";
}
