using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;

namespace YfmCompanion.RetroArch;

public static class RetroArchConfigInspector
{
    public const int DefaultNetworkCommandPort = 55355;

    public static bool IsRetroArchRunning()
    {
        try
        {
            var processes = Process.GetProcessesByName("retroarch");
            try
            {
                return processes.Length > 0;
            }
            finally
            {
                foreach (var process in processes)
                {
                    process.Dispose();
                }
            }
        }
        catch
        {
            return false;
        }
    }

    public static string? FindConfigurationPath(IEnumerable<string>? additionalCandidates = null)
    {
        var candidates = new List<string>();
        try
        {
            foreach (var process in Process.GetProcessesByName("retroarch"))
            {
                using (process)
                {
                    var executable = process.MainModule?.FileName;
                    if (!string.IsNullOrWhiteSpace(executable))
                    {
                        candidates.Add(Path.Combine(Path.GetDirectoryName(executable)!, "retroarch.cfg"));
                    }
                }
            }
        }
        catch
        {
            // Process path access can be restricted. Explicit/common candidates still work.
        }

        if (additionalCandidates is not null)
        {
            candidates.AddRange(additionalCandidates);
        }

        candidates.Add(Path.Combine(AppContext.BaseDirectory, "retroarch.cfg"));
        return candidates
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(File.Exists);
    }

    public static RetroArchConfiguration Read(string path)
    {
        if (!File.Exists(path))
        {
            return new RetroArchConfiguration(path, false, false, DefaultNetworkCommandPort, null);
        }

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in File.ReadLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            values[line[..separator].Trim()] = line[(separator + 1)..].Trim().Trim('"');
        }

        var enabled = values.TryGetValue("network_cmd_enable", out var enabledText) &&
                      bool.TryParse(enabledText, out var parsedEnabled) && parsedEnabled;
        var port = values.TryGetValue("network_cmd_port", out var portText) &&
                   int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedPort) &&
                   parsedPort is > 0 and <= 65535
            ? parsedPort
            : DefaultNetworkCommandPort;
        values.TryGetValue("savefile_directory", out var saveDirectory);
        return new RetroArchConfiguration(path, true, enabled, port, saveDirectory);
    }

    public static bool IsUdpListenerExposedBeyondLoopback(int port)
    {
        try
        {
            return IPGlobalProperties.GetIPGlobalProperties()
                .GetActiveUdpListeners()
                .Where(endpoint => endpoint.Port == port)
                .Any(endpoint => endpoint.Address.Equals(IPAddress.Any) ||
                                 endpoint.Address.Equals(IPAddress.IPv6Any) ||
                                 !IPAddress.IsLoopback(endpoint.Address));
        }
        catch
        {
            return false;
        }
    }
}
