using Tunnela.Desktop.Localization;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using Tunnela.Contracts;

namespace Tunnela.Desktop;

internal sealed class ServiceClient
{
    public async Task<ServiceResponse> SendAsync(string command, ServerProfile? profile = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = GetServiceProcessId(); // Fail promptly if our service is absent or stopped.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(command is "connect" or "disconnect" ? 25 : 8));
        using var pipe = new NamedPipeClientStream(".", ServiceProtocol.PipeName,
            PipeAccessRights.ReadWrite | PipeAccessRights.Synchronize, PipeOptions.Asynchronous,
            TokenImpersonationLevel.Identification, HandleInheritability.None);
        using (var connectionTimeout = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token))
        {
            connectionTimeout.CancelAfter(TimeSpan.FromSeconds(2));
            await pipe.ConnectAsync(connectionTimeout.Token);
        }
        VerifyServer(pipe);
        var request = new ServiceRequest { Command = command, Profile = profile };
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(request);
        try
        {
            if (payload.Length + 1 > ServiceProtocol.MaxMessageBytes) throw new InvalidDataException();
            await pipe.WriteAsync(payload, timeout.Token);
            await pipe.WriteAsync(new byte[] { 10 }, timeout.Token);
            await pipe.FlushAsync(timeout.Token);
        }
        finally { CryptographicOperations.ZeroMemory(payload); }
        using var message = new MemoryStream();
        var buffer = new byte[4096];
        while (true)
        {
            int count = await pipe.ReadAsync(buffer, timeout.Token);
            if (count == 0) throw new EndOfStreamException();
            int end = Array.IndexOf(buffer, (byte)10, 0, count);
            int length = end < 0 ? count : end;
            if (message.Length + length > ServiceProtocol.MaxMessageBytes) throw new InvalidDataException();
            message.Write(buffer, 0, length);
            if (end >= 0) break;
        }
        var response = JsonSerializer.Deserialize<ServiceResponse>(message.ToArray()) ?? throw new InvalidDataException();
        if (response.ProtocolVersion != 1 || response.RequestId != request.RequestId) throw new InvalidDataException();
        return response;
    }

    private static void VerifyServer(NamedPipeClientStream pipe)
    {
        if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out uint processId)) throw new UnauthorizedAccessException();
        if (processId != GetServiceProcessId()) throw new UnauthorizedAccessException();
    }

    private static uint GetServiceProcessId()
    {
        // Query the registered SCM service instead of opening a LocalSystem token:
        // token access can require SeDebugPrivilege, which the desktop must never request.
        using var manager = OpenSCManager(null, null, 0x0001);
        if (manager.IsInvalid) throw new UnauthorizedAccessException();
        using var service = OpenService(manager, ServiceProtocol.ServiceName, 0x0004);
        if (service.IsInvalid)
        {
            if (Marshal.GetLastWin32Error() == 1060) throw new ServiceUnavailableException("ServiceNotInstalled");
            throw new UnauthorizedAccessException();
        }
        if (!QueryServiceStatusEx(service, 0, out var status, Marshal.SizeOf<ServiceStatusProcess>(), out _)) throw new UnauthorizedAccessException();
        if (status.CurrentState != 4) throw new ServiceUnavailableException("ServiceNotRunning");
        if ((status.ServiceType & 0x10) == 0 || status.ProcessId == 0) throw new UnauthorizedAccessException();
        return status.ProcessId;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint serverProcessId);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ServiceHandle OpenSCManager(string? machine, string? database, uint access);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ServiceHandle OpenService(ServiceHandle manager, string serviceName, uint access);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryServiceStatusEx(ServiceHandle service, int informationLevel, out ServiceStatusProcess status, int bufferSize, out int bytesNeeded);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseServiceHandle(IntPtr service);

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatusProcess
    {
        public uint ServiceType;
        public uint CurrentState;
        public uint ControlsAccepted;
        public uint Win32ExitCode;
        public uint ServiceSpecificExitCode;
        public uint CheckPoint;
        public uint WaitHint;
        public uint ProcessId;
        public uint ServiceFlags;
    }

    private sealed class ServiceHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public ServiceHandle() : base(true) { }
        protected override bool ReleaseHandle() => CloseServiceHandle(handle);
    }
}

internal sealed class ServiceUnavailableException(string messageKey) : IOException(Text.Get(messageKey))
{
    public string MessageKey { get; } = messageKey;
}
