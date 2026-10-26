using System.IO;
using System.IO.Pipes;

namespace Tunnela.Service.Tests;

/// <summary>
/// Uses private local pipes with random names and the current user's default pipe security.
/// Never opens the product pipe, the service, the engine, or any network interface.
/// </summary>
public sealed class ControlPipeWorkerTests
{
    private const int TimeoutMilliseconds = 5000;
    private static readonly AcceptRetryPolicy FastPolicy =
        new(TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(40), MaxConsecutiveFaults: 3);

    [Fact]
    public async Task ClientClosingBeforeAcceptIsReportedAsAnAbortedClient()
    {
        // Documents the Windows behaviour the accept loop must survive (ERROR_NO_DATA).
        var name = NewPipeName();
        using var server = CreateServerPipe(name, firstInstance: true);
        using (var early = new NamedPipeClientStream(".", name, PipeDirection.InOut))
        {
            early.Connect(TimeoutMilliseconds);
        }

        var exception = await Assert.ThrowsAnyAsync<IOException>(() => server.WaitForConnectionAsync());
        Assert.True(ControlPipeWorker.IsAbortedClient(exception), $"Unexpected HRESULT 0x{exception.HResult:X8}.");
    }

    [Fact]
    public async Task ClientClosingBeforeAcceptDoesNotStopTheListenerOrCountAsAFault()
    {
        var name = NewPipeName();
        var created = 0;
        var handled = 0;
        var faults = 0;

        NamedPipeServerStream Create(bool firstInstance)
        {
            var pipe = CreateServerPipe(name, firstInstance);
            if (Interlocked.Increment(ref created) == 2)
            {
                // Reproduce the race deterministically: a client opens the next instance and
                // closes it before the loop starts waiting for a connection on that instance.
                using var early = new NamedPipeClientStream(".", name, PipeDirection.InOut);
                early.Connect(TimeoutMilliseconds);
            }
            return pipe;
        }

        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var loop = ControlPipeWorker.AcceptClientsAsync(Create, Answer(() => Interlocked.Increment(ref handled)),
            maxClients: 8, FastPolicy, () => Interlocked.Increment(ref faults), stop.Token);

        Assert.Equal(42, await ExchangeAsync(name));
        // The aborted client was on the second instance; the loop must replace it and keep serving.
        Assert.Equal(42, await ExchangeAsync(name));
        Assert.False(loop.IsCompleted);

        stop.Cancel();
        await loop.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(2, handled);
        Assert.Equal(0, faults);
        Assert.True(created >= 3);
    }

    [Fact]
    public async Task TransientPipeCreationFailureStillServesTheAcceptedClientAndRecovers()
    {
        var name = NewPipeName();
        var created = 0;
        var faults = 0;

        NamedPipeServerStream Create(bool firstInstance)
        {
            // The replacement after the first accepted client fails once, as under resource pressure.
            if (Interlocked.Increment(ref created) == 2) throw new IOException("Injected creation failure.");
            return CreateServerPipe(name, firstInstance);
        }

        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var loop = ControlPipeWorker.AcceptClientsAsync(Create, Answer(() => { }), maxClients: 8, FastPolicy,
            () => Interlocked.Increment(ref faults), stop.Token);

        Assert.Equal(42, await ExchangeAsync(name));
        Assert.Equal(42, await ExchangeAsync(name));
        Assert.False(loop.IsCompleted);
        Assert.Equal(1, faults);

        stop.Cancel();
        await loop.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task PersistentPipeCreationFailureStopsAfterTheRetryLimit()
    {
        var name = NewPipeName();
        var created = 0;
        var faults = 0;

        NamedPipeServerStream Create(bool firstInstance)
        {
            if (Interlocked.Increment(ref created) > 1) throw new InvalidOperationException("Injected persistent failure.");
            return CreateServerPipe(name, firstInstance);
        }

        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var loop = ControlPipeWorker.AcceptClientsAsync(Create, Answer(() => { }), maxClients: 8, FastPolicy,
            () => Interlocked.Increment(ref faults), stop.Token);

        Assert.Equal(42, await ExchangeAsync(name));
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => loop.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal("Injected persistent failure.", exception.Message);
        Assert.Equal(FastPolicy.MaxConsecutiveFaults, faults);
    }

    [Fact]
    public async Task CancellationStopsTheLoopWithoutAnException()
    {
        var name = NewPipeName();
        using var stop = new CancellationTokenSource();
        var loop = ControlPipeWorker.AcceptClientsAsync(first => CreateServerPipe(name, first),
            (_, _) => Task.CompletedTask, maxClients: 8, FastPolicy, () => { }, stop.Token);

        stop.Cancel();
        await loop.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void FaultDelayDoublesUpToTheLimit()
    {
        var policy = AcceptRetryPolicy.Default;
        Assert.Equal(TimeSpan.FromMilliseconds(200), policy.FaultDelay(1));
        Assert.Equal(TimeSpan.FromMilliseconds(400), policy.FaultDelay(2));
        Assert.Equal(TimeSpan.FromMilliseconds(3200), policy.FaultDelay(5));
        Assert.Equal(TimeSpan.FromSeconds(5), policy.FaultDelay(6));
        Assert.Equal(TimeSpan.FromSeconds(5), policy.FaultDelay(1000));

        // The service gives up only after a sustained outage, not after a short burst.
        var total = Enumerable.Range(1, policy.MaxConsecutiveFaults - 1).Sum(fault => policy.FaultDelay(fault).TotalSeconds);
        Assert.InRange(total, 60, 120);
    }

    private static Func<NamedPipeServerStream, CancellationToken, Task> Answer(Action onHandled) =>
        async (pipe, cancellationToken) =>
        {
            onHandled();
            await pipe.WriteAsync(new byte[] { 42 }, cancellationToken);
            await pipe.FlushAsync(cancellationToken);
        };

    private static string NewPipeName() => "Tunnela.Tests." + Guid.NewGuid().ToString("N");

    private static NamedPipeServerStream CreateServerPipe(string name, bool firstInstance) =>
        new(name, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | (firstInstance ? PipeOptions.FirstPipeInstance : PipeOptions.None));

    private static async Task<int> ExchangeAsync(string name)
    {
        using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(TimeoutMilliseconds);
        var buffer = new byte[1];
        using var timeout = new CancellationTokenSource(TimeoutMilliseconds);
        return await client.ReadAsync(buffer, timeout.Token) == 1 ? buffer[0] : -1;
    }
}
