using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace YfmCompanion.RetroArch;

/// <summary>
/// Minimal, read-only client for RetroArch's UDP Network Control Interface.
/// The public API intentionally exposes status and memory reads only.
/// </summary>
public interface IRetroArchReadClient
{
    Task<RetroArchStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    Task<byte[]> ReadCoreMemoryAsync(uint address, int byteCount, CancellationToken cancellationToken = default);

    Task<byte[]> ReadCoreRamAsync(uint address, int byteCount, CancellationToken cancellationToken = default);
}

public sealed class RetroArchNetworkClient : IRetroArchReadClient, IDisposable
{
    private const int MaximumReadChunk = 256;
    private readonly IPEndPoint _endpoint;
    private readonly TimeSpan _timeout;
    private readonly int _attempts;
    private readonly SemaphoreSlim _requestLock = new(1, 1);
    private bool _disposed;

    public RetroArchNetworkClient(
        IPAddress? address = null,
        int port = RetroArchConfigInspector.DefaultNetworkCommandPort,
        TimeSpan? timeout = null,
        int attempts = 2)
    {
        address ??= IPAddress.Loopback;
        if (!IPAddress.IsLoopback(address))
        {
            throw new ArgumentException("Live memory access is restricted to this computer.", nameof(address));
        }

        if (port is <= 0 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(attempts, 1);

        var effectiveTimeout = timeout ?? TimeSpan.FromMilliseconds(650);
        if (effectiveTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "The network timeout must be positive.");
        }

        _endpoint = new IPEndPoint(address, port);
        _timeout = effectiveTimeout;
        _attempts = attempts;
    }

    public Task<string> GetVersionAsync(CancellationToken cancellationToken = default) =>
        RequestAsync("VERSION", "", cancellationToken);

    public async Task<RetroArchStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var response = await RequestAsync("GET_STATUS", "GET_STATUS", cancellationToken).ConfigureAwait(false);
        return ParseStatus(response);
    }

    public Task<byte[]> ReadCoreMemoryAsync(uint address, int byteCount, CancellationToken cancellationToken = default) =>
        ReadBytesAsync("READ_CORE_MEMORY", address, byteCount, cancellationToken);

    public Task<byte[]> ReadCoreRamAsync(uint address, int byteCount, CancellationToken cancellationToken = default) =>
        ReadBytesAsync("READ_CORE_RAM", address, byteCount, cancellationToken);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _requestLock.Dispose();
    }

    private async Task<byte[]> ReadBytesAsync(
        string command,
        uint address,
        int byteCount,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(byteCount);

        if (byteCount == 0)
        {
            return [];
        }

        var output = new byte[byteCount];
        var copied = 0;
        while (copied < output.Length)
        {
            var chunkLength = Math.Min(MaximumReadChunk, output.Length - copied);
            var chunkAddress = checked(address + (uint)copied);
            var request = string.Create(
                CultureInfo.InvariantCulture,
                $"{command} {chunkAddress:x} {chunkLength}");
            var expectedPrefix = string.Create(CultureInfo.InvariantCulture, $"{command} {chunkAddress:x}");
            var response = await RequestAsync(request, expectedPrefix, cancellationToken).ConfigureAwait(false);
            var chunk = ParseMemoryResponse(response, expectedPrefix, chunkLength);
            chunk.CopyTo(output, copied);
            copied += chunk.Length;
        }

        return output;
    }

    private async Task<string> RequestAsync(
        string request,
        string expectedPrefix,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _requestLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Exception? lastError = null;
            for (var attempt = 1; attempt <= _attempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    using var udp = new UdpClient(AddressFamily.InterNetwork);
                    udp.Connect(_endpoint);
                    var payload = Encoding.ASCII.GetBytes(request);
                    await udp.SendAsync(payload, cancellationToken).ConfigureAwait(false);
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeout.CancelAfter(_timeout);
                    var result = await udp.ReceiveAsync(timeout.Token).ConfigureAwait(false);
                    var response = Encoding.ASCII.GetString(result.Buffer).TrimEnd('\0', '\r', '\n');
                    if (expectedPrefix.Length > 0 && !response.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new RetroArchProtocolException($"Unexpected RetroArch response: {response}");
                    }

                    return response;
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    lastError = new TimeoutException($"RetroArch did not answer {request.Split(' ')[0]} within {_timeout.TotalMilliseconds:N0} ms.");
                }
                catch (SocketException exception)
                {
                    lastError = exception;
                }
            }

            throw lastError ?? new TimeoutException("RetroArch did not answer the read-only request.");
        }
        finally
        {
            _requestLock.Release();
        }
    }

    private static byte[] ParseMemoryResponse(string response, string expectedPrefix, int expectedCount)
    {
        var payload = response[expectedPrefix.Length..].Trim();
        if (payload.StartsWith("-1", StringComparison.Ordinal))
        {
            throw new RetroArchProtocolException(payload.Length > 2
                ? payload[2..].Trim()
                : "RetroArch could not read the requested memory.");
        }

        var tokens = payload.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length != expectedCount)
        {
            throw new RetroArchProtocolException($"RetroArch returned {tokens.Length} bytes; {expectedCount} were requested.");
        }

        var bytes = new byte[tokens.Length];
        for (var index = 0; index < tokens.Length; index++)
        {
            if (!byte.TryParse(tokens[index], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out bytes[index]))
            {
                throw new RetroArchProtocolException($"Invalid memory byte '{tokens[index]}'.");
            }
        }

        return bytes;
    }

    internal static RetroArchStatus ParseStatus(string response)
    {
        if (response.Equals("GET_STATUS CONTENTLESS", StringComparison.OrdinalIgnoreCase))
        {
            return new RetroArchStatus(RetroArchPlaybackState.Contentless, null, null, null, response);
        }

        const string prefix = "GET_STATUS ";
        if (!response.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new RetroArchProtocolException($"Unexpected status response: {response}");
        }

        var body = response[prefix.Length..];
        var firstSpace = body.IndexOf(' ');
        if (firstSpace <= 0)
        {
            throw new RetroArchProtocolException($"Incomplete status response: {response}");
        }

        var state = body[..firstSpace].ToUpperInvariant() switch
        {
            "PLAYING" => RetroArchPlaybackState.Playing,
            "PAUSED" => RetroArchPlaybackState.Paused,
            _ => RetroArchPlaybackState.Unknown
        };
        var details = body[(firstSpace + 1)..];
        var crcSeparator = details.LastIndexOf(",crc32=", StringComparison.OrdinalIgnoreCase);
        var identity = crcSeparator >= 0 ? details[..crcSeparator] : details;
        var identitySeparator = identity.IndexOf(',');
        var system = identitySeparator >= 0 ? identity[..identitySeparator] : identity;
        var game = identitySeparator >= 0 ? identity[(identitySeparator + 1)..] : null;
        uint? crc = null;
        if (crcSeparator >= 0 && uint.TryParse(
                details[(crcSeparator + 7)..],
                NumberStyles.AllowHexSpecifier,
                CultureInfo.InvariantCulture,
                out var parsedCrc))
        {
            crc = parsedCrc;
        }

        return new RetroArchStatus(state, system, game, crc, response);
    }
}
