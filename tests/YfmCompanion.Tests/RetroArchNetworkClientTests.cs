using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using YfmCompanion.RetroArch;

namespace YfmCompanion.Tests;

public sealed class RetroArchNetworkClientTests
{
    [Fact]
    public void ConstructorRejectsNonLoopbackAddress()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new RetroArchNetworkClient(IPAddress.Parse("192.168.1.20")));

        Assert.Contains("this computer", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public void ConstructorRejectsInvalidPorts(int port) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new RetroArchNetworkClient(port: port));

    [Fact]
    public void ConstructorRejectsNonPositiveTimeouts()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RetroArchNetworkClient(timeout: TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RetroArchNetworkClient(timeout: TimeSpan.FromMilliseconds(-1)));
    }

    [Fact]
    public async Task GetStatusParsesPlayingContent()
    {
        await using var server = new FakeRetroArchServer(request => request == "GET_STATUS"
            ? "GET_STATUS PLAYING Sony - PlayStation,Yu-Gi-Oh! Forbidden Memories (USA),crc32=1234ABCD\n"
            : null);
        using var client = new RetroArchNetworkClient(
            IPAddress.Loopback,
            server.Port,
            TimeSpan.FromMilliseconds(500),
            attempts: 1);

        var status = await client.GetStatusAsync();

        Assert.Equal(RetroArchPlaybackState.Playing, status.State);
        Assert.Equal("Sony - PlayStation", status.SystemId);
        Assert.Equal("Yu-Gi-Oh! Forbidden Memories (USA)", status.GameBasename);
        Assert.Equal(0x1234ABCDu, status.Crc32);
    }

    [Fact]
    public async Task MemoryReadsAreChunkedAndReassembled()
    {
        var requests = new List<string>();
        await using var server = new FakeRetroArchServer(request =>
        {
            lock (requests)
            {
                requests.Add(request);
            }

            var parts = request.Split(' ');
            var address = uint.Parse(parts[1], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
            var count = int.Parse(parts[2], CultureInfo.InvariantCulture);
            var bytes = Enumerable.Range(0, count)
                .Select(index => ((address + (uint)index) & 0xff).ToString("X2", CultureInfo.InvariantCulture));
            return $"{parts[0]} {parts[1]} {string.Join(' ', bytes)}";
        });
        using var client = new RetroArchNetworkClient(
            IPAddress.Loopback,
            server.Port,
            TimeSpan.FromMilliseconds(500),
            attempts: 1);

        var result = await client.ReadCoreMemoryAsync(0x1000, 600);

        Assert.Equal(600, result.Length);
        Assert.Equal(0x00, result[0]);
        Assert.Equal(0xff, result[255]);
        Assert.Equal(0x00, result[256]);
        Assert.Equal(3, requests.Count);
        Assert.All(requests, request => Assert.StartsWith("READ_CORE_MEMORY ", request, StringComparison.Ordinal));
    }

    [Fact]
    public async Task MemoryFailureIsReportedWithoutReturningPartialData()
    {
        await using var server = new FakeRetroArchServer(request =>
        {
            var parts = request.Split(' ');
            return $"{parts[0]} {parts[1]} -1 no memory map defined";
        });
        using var client = new RetroArchNetworkClient(
            IPAddress.Loopback,
            server.Port,
            TimeSpan.FromMilliseconds(500),
            attempts: 1);

        var exception = await Assert.ThrowsAsync<RetroArchProtocolException>(() =>
            client.ReadCoreMemoryAsync(0x1000, 16));

        Assert.Contains("no memory map defined", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MalformedMemoryBytesAreRejected()
    {
        await using var server = new FakeRetroArchServer(request =>
        {
            var parts = request.Split(' ');
            return $"{parts[0]} {parts[1]} GG";
        });
        using var client = new RetroArchNetworkClient(
            IPAddress.Loopback,
            server.Port,
            TimeSpan.FromMilliseconds(500),
            attempts: 1);

        var exception = await Assert.ThrowsAsync<RetroArchProtocolException>(() =>
            client.ReadCoreMemoryAsync(0x1000, 1));

        Assert.Contains("Invalid memory byte", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PausedAndContentlessStatusesAreParsed()
    {
        await using var pausedServer = new FakeRetroArchServer(_ =>
            "GET_STATUS PAUSED Sony - PlayStation,Yu-Gi-Oh! Forbidden Memories (USA),crc32=00000001");
        using var pausedClient = new RetroArchNetworkClient(
            IPAddress.Loopback,
            pausedServer.Port,
            TimeSpan.FromMilliseconds(500),
            attempts: 1);
        Assert.Equal(RetroArchPlaybackState.Paused, (await pausedClient.GetStatusAsync()).State);

        await using var contentlessServer = new FakeRetroArchServer(_ => "GET_STATUS CONTENTLESS");
        using var contentlessClient = new RetroArchNetworkClient(
            IPAddress.Loopback,
            contentlessServer.Port,
            TimeSpan.FromMilliseconds(500),
            attempts: 1);
        Assert.Equal(RetroArchPlaybackState.Contentless, (await contentlessClient.GetStatusAsync()).State);
    }

    [Fact]
    public async Task ZeroLengthReadReturnsWithoutNetworkTraffic()
    {
        var requests = 0;
        await using var server = new FakeRetroArchServer(_ =>
        {
            Interlocked.Increment(ref requests);
            return null;
        });
        using var client = new RetroArchNetworkClient(
            IPAddress.Loopback,
            server.Port,
            TimeSpan.FromMilliseconds(500),
            attempts: 1);

        Assert.Empty(await client.ReadCoreMemoryAsync(0x1000, 0));
        Assert.Equal(0, Volatile.Read(ref requests));
    }

    [Fact]
    public async Task TimeoutIsBounded()
    {
        await using var server = new FakeRetroArchServer(_ => null);
        using var client = new RetroArchNetworkClient(
            IPAddress.Loopback,
            server.Port,
            TimeSpan.FromMilliseconds(50),
            attempts: 1);

        await Assert.ThrowsAsync<TimeoutException>(() => client.GetStatusAsync());
    }

    private sealed class FakeRetroArchServer : IAsyncDisposable
    {
        private readonly UdpClient _udp = new(new IPEndPoint(IPAddress.Loopback, 0));
        private readonly CancellationTokenSource _cancellation = new();
        private readonly Func<string, string?> _handler;
        private readonly Task _loop;

        public FakeRetroArchServer(Func<string, string?> handler)
        {
            _handler = handler;
            Port = ((IPEndPoint)_udp.Client.LocalEndPoint!).Port;
            _loop = RunAsync();
        }

        public int Port { get; }

        public async ValueTask DisposeAsync()
        {
            await _cancellation.CancelAsync();
            _udp.Dispose();
            try
            {
                await _loop;
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }

            _cancellation.Dispose();
        }

        private async Task RunAsync()
        {
            while (!_cancellation.IsCancellationRequested)
            {
                var request = await _udp.ReceiveAsync(_cancellation.Token);
                var text = Encoding.ASCII.GetString(request.Buffer);
                var response = _handler(text);
                if (response is null)
                {
                    continue;
                }

                var payload = Encoding.ASCII.GetBytes(response);
                await _udp.SendAsync(payload, request.RemoteEndPoint, _cancellation.Token);
            }
        }
    }
}
