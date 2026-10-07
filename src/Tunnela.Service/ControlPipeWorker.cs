using Tunnela.Contracts.Localization;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Tunnela.Contracts;

namespace Tunnela.Service;

internal sealed class ControlPipeWorker(InstallConfiguration installation, EngineSupervisor supervisor) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly SemaphoreSlim _clients = new(8, 8);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        NamedPipeServerStream? listener = CreatePipe(firstInstance: true);
        var active = new List<Task>();
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await _clients.WaitAsync(stoppingToken);
                try
                {
                    await listener.WaitForConnectionAsync(stoppingToken);
                    var connected = listener;
                    // Keep a server handle alive continuously to prevent pipe-name squatting between requests.
                    listener = CreatePipe(firstInstance: false);
                    active.RemoveAll(task => task.IsCompleted);
                    active.Add(HandleClientAsync(connected, stoppingToken));
                }
                catch
                {
                    _clients.Release();
                    throw;
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally
        {
            listener?.Dispose();
            await Task.WhenAll(active);
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

    private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken stoppingToken)
    {
        using (pipe)
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
            finally { _clients.Release(); }
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
