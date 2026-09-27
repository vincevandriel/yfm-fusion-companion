using YfmCompanion.Data;

namespace YfmCompanion.Tests;

public sealed class RuntimeResourcesTests
{
    [Fact]
    public void DeveloperLayoutUsesApplicationDirectory()
    {
        var directory = Directory.CreateTempSubdirectory("yfm-resources-").FullName;
        try { Assert.Equal(directory, RuntimeResources.FindRoot(directory)); }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void OrganizedPackageNeverFallsBackToAnOlderSiblingDatabase()
    {
        var directory = Directory.CreateTempSubdirectory("yfm-resources-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(directory, "Resources"));
            Directory.CreateDirectory(Path.Combine(directory, "Data"));
            File.WriteAllText(Path.Combine(directory, "Data", "yfm.db"), "old database");
            var root = RuntimeResources.FindRoot(directory);
            Assert.Equal(Path.Combine(directory, "Resources"), root);
            Assert.False(File.Exists(Path.Combine(root, "Data", "yfm.db")));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
