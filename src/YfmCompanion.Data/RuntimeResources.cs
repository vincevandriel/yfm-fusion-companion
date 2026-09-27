namespace YfmCompanion.Data;

/// <summary>One resource root for an installed package; legacy/developer layout remains supported.</summary>
public static class RuntimeResources
{
    public static string FindRoot(string baseDirectory)
    {
        var root = Path.GetFullPath(baseDirectory);
        var resources = Path.Combine(root, "Resources");
        // Once the organized layout exists, missing files must fail there rather
        // than silently mixing resources from an older executable's directory.
        return Directory.Exists(resources) ? resources : root;
    }
}
