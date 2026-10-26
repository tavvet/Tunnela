using Tunnela.Contracts.Localization;
using System.IO.Pipes;
using System.Runtime.ExceptionServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Tunnela.Contracts;

namespace Tunnela.Service;

internal sealed class ControlPipeWorker(InstallConfiguration installation, EngineSupervisor supervisor) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private const int MaxConcurrentClients = 8;

    // Win32 errors (as HRESULTs) meaning that the client went away before or while it was accepted:
    // ERROR_NO_DATA, ERROR_BROKEN_PIPE and ERROR_PIPE_NOT_CONNECTED.
    private static readonly int[] AbortedClientResults = [unchecked((int)0x800700E8), unchecked((int)0x8007006D), unchecked((int)0x800700E9)];

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        AcceptClientsAsync(CreatePipe, HandleClientAsync, MaxConcurrentClients, AcceptRetryPolicy.Default,
            supervisor.RecordControlChannelFault, stoppingToken);

    internal static bool IsAbortedClient(IOException exception) => AbortedClientResults.Contains(exception.HResult);

    /// <summary>
    /// Accepts clients until cancellation. An exception escaping this loop stops the host, and
    /// stopping the service disconnects the VPN, so only a fault that persists is allowed to escape:
    /// a client that went away is replaced at once, and other accept or pipe-creation failures are
    /// retried with growing delays until <see cref="AcceptRetryPolicy.MaxConsecutiveFaults"/>.
    /// </summary>
    internal static async Task AcceptClientsAsync(Func<bool, NamedPipeServerStream> createPipe,
        Func<NamedPipeServerStream, CancellationToken, Task> handleClient, int maxClients,
        AcceptRetryPolicy policy, Action onFault, CancellationToken stoppingToken)
    {
        using var clients = new SemaphoreSlim(maxClients, maxClients);
        var active = new List<Task>();
        var consecutiveFaults = 0;
        Exception? lastFault = null;
        // Without its first instance the service cannot own the pipe name, so this failure stays fatal.
        NamedPipeServerStream? listener = createPipe(true);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                listener ??= TryCreateInstance();
                if (listener is null)
                {
                    await RecordFaultAsync(lastFault!);
                    continue;
                }

                await clients.WaitAsync(stoppingToken);
                try
                {
                    await listener.WaitForConnectionAsync(stoppingToken);
                }
                catch (IOException exception)
                {
                    clients.Release();
                    // Create the replacement before disposing so the pipe name stays owned by the service.
                    var replacement = TryCreateInstance();
                    listener.Dispose();
                    listener = replacement;
                    if (replacement is not null && IsAbortedClient(exception))
                        await Task.Delay(policy.AbortedClientDelay, stoppingToken);
                    else
                        await RecordFaultAsync(replacement is null ? lastFault! : exception);
                    continue;
                }
                catch
                {
                    clients.Release();
                    throw;
                }

                consecutiveFaults = 0;
                var connected = listener;
                // Keep a server handle alive continuously to prevent pipe-name squatting between requests.
                listener = TryCreateInstance();
                active.RemoveAll(task => task.IsCompleted);
                // The accepted request is served even if its replacement could not be created yet.
                active.Add(ServeAsync(connected));
                if (listener is null) await RecordFaultAsync(lastFault!);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally
        {
            listener?.Dispose();
            await Task.WhenAll(active);
        }

        NamedPipeServerStream? TryCreateInstance()
        {
            try { return createPipe(false); }
            catch (Exception exception)
            {
                lastFault = exception;
                return null;
            }
        }

        async Task RecordFaultAsync(Exception fault)
        {
            consecutiveFaults++;
            onFault();
            if (consecutiveFaults >= policy.MaxConsecutiveFaults) ExceptionDispatchInfo.Throw(fault);
            await Task.Delay(policy.FaultDelay(consecutiveFaults), stoppingToken);
        }

        async Task ServeAsync(NamedPipeServerStream pipe)
        {
            try
            {
                using (pipe) await handleClient(pipe, stoppingToken);
            }
            finally { clients.Release(); }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // The engine has its own bounded graceful-stop deadline. A timeout leaves it reported as unknown.
        await supervisor.ShutdownAsync();
        await base.StopAsync(cancellationToken);
    }

    private NamedPipeServerStream CreatePipe(bool firstInstance)
    {
        var security = new PipeSecurity();
        security.SetAccessRuleProtection(true, false);
        security.SetOwner(ProtectedFiles.SystemSid);
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null),
            PipeAccessRights.FullControl, AccessControlType.Deny));
        security.AddAccessRule(new PipeAccessRule(ProtectedFiles.SystemSid, PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(ProtectedFiles.AdministratorsSid, PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(installation.ControllerSid),
            PipeAccessRights.ReadWrite | PipeAccessRights.Synchronize, AccessControlType.Allow));
        var options = PipeOptions.Asynchronous;
        if (firstInstance) options |= PipeOptions.FirstPipeInstance;
        return NamedPipeServerStreamAcl.Create(ServiceProtocol.PipeName, PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, options,
            4096, 4096, security, HandleInheritability.None);
    }

    // AcceptClientsAsync owns the pipe and the concurrency slot; this handler must not throw.
    private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken stoppingToken)
    {
        try
        {
            // Bound both memory and the time a client may hold a connection without sending a request.
            using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            readTimeout.CancelAfter(TimeSpan.FromSeconds(3));
            var requestBytes = await ReadLineAsync(pipe, readTimeout.Token);
            var request = JsonSerializer.Deserialize<ServiceRequest>(requestBytes, JsonOptions);
            if (request is null) return;

            ServiceOperationError? error;
            if (request.ProtocolVersion != 1) error = new("Service.ProtocolVersionMismatch");
            else
            {
                error = request.Command switch
                {
                    "status" or "logs" => null,
                    "connect" => await supervisor.ConnectAsync(request.Profile, stoppingToken),
                    "disconnect" => await supervisor.DisconnectAsync(stoppingToken),
                    _ => new("Service.CommandUnknown"),
                };
            }

            var response = new ServiceResponse
            {
                RequestId = request.RequestId,
                Success = error is null,
                Error = error?.Message,
                ErrorCode = error?.Code,
                Snapshot = supervisor.Snapshot,
                Logs = request.Command == "logs" ? supervisor.Logs : [],
            };
            var output = JsonSerializer.SerializeToUtf8Bytes(response);
            if (output.Length > ServiceProtocol.MaxMessageBytes) return;
            using var writeTimeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            writeTimeout.CancelAfter(TimeSpan.FromSeconds(3));
            await pipe.WriteAsync(output, writeTimeout.Token);
            await pipe.WriteAsync(new byte[] { (byte)'\n' }, writeTimeout.Token);
            await pipe.FlushAsync(writeTimeout.Token);
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException
            or JsonException or UnauthorizedAccessException or FormatException)
        {
            // Malformed requests and pipe errors must not leak their content to diagnostics.
        }
        catch (Exception)
        {
            // A malformed or unexpected single request must not terminate the Windows service.
            // Closing this request is reported by the client as a failed operation, never success.
        }
    }

    private static async Task<byte[]> ReadLineAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var message = new MemoryStream();
        var buffer = new byte[4096];
        while (true)
        {
            var count = await stream.ReadAsync(buffer, cancellationToken);
            if (count == 0) throw new IOException(Messages.GetEnglish("Service.RequestIncomplete"));
            var newline = Array.IndexOf(buffer, (byte)'\n', 0, count);
            var length = newline >= 0 ? newline : count;
            if (message.Length + length > ServiceProtocol.MaxMessageBytes) throw new IOException(Messages.GetEnglish("Service.RequestTooLarge"));
            message.Write(buffer, 0, length);
            if (newline >= 0) return message.ToArray();
        }
    }
}

/// <summary>Delays for the control pipe accept loop. Tests pass short values.</summary>
internal sealed record AcceptRetryPolicy(TimeSpan AbortedClientDelay, TimeSpan FirstFaultDelay, TimeSpan MaxFaultDelay,
    int MaxConsecutiveFaults)
{
    /// <summary>
    /// Gives up after about 75 seconds of failures without a single accepted client. A control
    /// channel that stays broken longer is reported by stopping the service instead of hiding it.
    /// </summary>
    public static AcceptRetryPolicy Default { get; } =
        new(TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(5), 20);

    /// <summary>Doubles from <see cref="FirstFaultDelay"/> for each consecutive fault, up to <see cref="MaxFaultDelay"/>.</summary>
    public TimeSpan FaultDelay(int consecutiveFaults)
    {
        var delay = FirstFaultDelay;
        for (var fault = 1; fault < consecutiveFaults && delay < MaxFaultDelay; fault++) delay += delay;
        return delay < MaxFaultDelay ? delay : MaxFaultDelay;
    }
}
