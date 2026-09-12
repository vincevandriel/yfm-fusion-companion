using System.Text.Json;
using YfmCompanion.Data;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: YfmCompanion.DataBuilder <PostgreSQL.sql> <output.db>");
    return 2;
}

try
{
    var report = PostgresDumpImporter.Import(args[0], args[1]);
    Console.WriteLine(JsonSerializer.Serialize(report, SerializerConfiguration.Indented));
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.ToString());
    return 1;
}

internal static class SerializerConfiguration
{
    internal static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };
}
