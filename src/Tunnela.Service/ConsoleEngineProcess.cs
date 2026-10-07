using Tunnela.Contracts.Localization;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace Tunnela.Service;

internal sealed record EngineSessionRecord(int ProcessId, long StartTimeUtcTicks, string Token, Guid ProfileId,
    bool StopSignalAttempted = false);

internal sealed class ConsoleEngineProcess : IDisposable
{
    public Process Process { get; }
    public EngineSessionRecord Record { get; }
    public StreamReader? Output { get; }

    private ConsoleEngineProcess(Process process, EngineSessionRecord record, StreamReader? output)
    {
        Process = process;
        Record = record;
        Output = output;
    }

    public static ConsoleEngineProcess Start(Guid profileId)
    {
        var attributes = new NativeMethods.SecurityAttributes
        {
            Length = Marshal.SizeOf<NativeMethods.SecurityAttributes>(),
            InheritHandle = true,
        };
        if (!NativeMethods.CreatePipe(out var read, out var write, ref attributes, 0))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        // The child inherits its output writer and NUL input. Close the parent's copies after launch;
        // the reader stays local and is transferred to the returned session's StreamReader on success.
        using (write)
        using (var input = NativeMethods.CreateFile("NUL", NativeMethods.GenericRead,
            NativeMethods.FileShareRead | NativeMethods.FileShareWrite, ref attributes,
            NativeMethods.OpenExisting, 0, IntPtr.Zero))
        {
            Process? process = null;
            var nativeProcess = new NativeMethods.ProcessInformation();
            bool resumed = false;
            try
            {
                if (input.IsInvalid || !NativeMethods.SetHandleInformation(read, NativeMethods.HandleFlagInherit, 0))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                var startup = new NativeMethods.StartupInfo
                {
                    Size = Marshal.SizeOf<NativeMethods.StartupInfo>(),
                    Flags = NativeMethods.StartfUseStdHandles | NativeMethods.StartfUseShowWindow,
                    ShowWindow = NativeMethods.SwHide, // A private console without an interactive window.
                    StandardInput = input.DangerousGetHandle(),
                    StandardOutput = write.DangerousGetHandle(),
                    StandardError = write.DangerousGetHandle(),
                };
                // No shell and no user-supplied executable or arguments. A new console is needed for CTRL_C.
                var command = new StringBuilder($"\"{InstallConfiguration.EnginePath}\" --config \"{InstallConfiguration.RuntimeConfigPath}\" --loglevel info");
                if (!NativeMethods.CreateProcess(InstallConfiguration.EnginePath, command,
                    IntPtr.Zero, IntPtr.Zero, true, NativeMethods.CreateNewConsole | NativeMethods.CreateSuspended,
                    IntPtr.Zero, Path.GetDirectoryName(InstallConfiguration.EnginePath), ref startup, out nativeProcess))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                process = Process.GetProcessById((int)nativeProcess.ProcessId);
                var record = new EngineSessionRecord(process.Id, process.StartTime.ToUniversalTime().Ticks,
                    Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), profileId);
                // Publish ownership before resuming: a service restart can safely recognise this process.
                ProtectedFiles.WriteSecret(InstallConfiguration.ProcessRecordPath, JsonSerializer.Serialize(record));
                var output = new StreamReader(new FileStream(read, FileAccess.Read, 4096, false),
                    new UTF8Encoding(false, false), detectEncodingFromByteOrderMarks: true);
                var result = new ConsoleEngineProcess(process, record, output);
                if (NativeMethods.ResumeThread(nativeProcess.Thread) == uint.MaxValue)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
                resumed = true;
                return result;
            }
            catch
            {
                // Only a still-suspended failed launch may be terminated: it has not executed any VPN code.
                // This is NEVER used to disconnect an engine that has run.
                if (!resumed && nativeProcess.Process != IntPtr.Zero)
                {
                    NativeMethods.TerminateProcess(nativeProcess.Process, 1);
                }
                process?.Dispose();
                read.Dispose();
                throw;
            }
            finally
            {
                // These CreateProcess handles are independent of the managed Process object.
                // On success, the returned session owns that object until its exit has been observed.
                if (nativeProcess.Thread != IntPtr.Zero) NativeMethods.CloseHandle(nativeProcess.Thread);
                if (nativeProcess.Process != IntPtr.Zero) NativeMethods.CloseHandle(nativeProcess.Process);
            }
        }
    }

    public static ConsoleEngineProcess? TryRecover()
    {
        if (!File.Exists(InstallConfiguration.ProcessRecordPath)) return null;
        ProtectedFiles.RequireProtectedAcl(new FileInfo(InstallConfiguration.ProcessRecordPath));
        var record = JsonSerializer.Deserialize<EngineSessionRecord>(File.ReadAllText(InstallConfiguration.ProcessRecordPath))
            ?? throw new InvalidDataException(Messages.GetEnglish("Service.OwnershipRecordInvalid"));
        try
        {
            var process = Process.GetProcessById(record.ProcessId);
            if (Matches(process, record)) return new ConsoleEngineProcess(process, record, null);
            process.Dispose();
        }
        catch (ArgumentException) { }
        // A stale PID must never be signalled or treated as an active engine.
        return null;
    }

    internal static bool Matches(Process process, EngineSessionRecord record)
    {
        try
        {
            return !process.HasExited && process.StartTime.ToUniversalTime().Ticks == record.StartTimeUtcTicks
                && string.Equals(process.MainModule?.FileName, InstallConfiguration.EnginePath, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        Output?.Dispose();
        Process.Dispose();
    }
}

internal static class EngineSignalHelper
{
    internal static int Run(string token)
    {
        try
        {
            // No arbitrary PID or program path can be passed to this helper.
            if (!WindowsIdentity.GetCurrent().IsSystem || token.Length != 64) return 10;
            _ = InstallConfiguration.LoadAndValidate();
            ProtectedFiles.RequireProtectedAcl(new FileInfo(InstallConfiguration.ProcessRecordPath));
            var record = JsonSerializer.Deserialize<EngineSessionRecord>(File.ReadAllText(InstallConfiguration.ProcessRecordPath));
            if (record is null || !CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(record.Token), Encoding.ASCII.GetBytes(token))) return 11;
            using var process = Process.GetProcessById(record.ProcessId);
            if (!ConsoleEngineProcess.Matches(process, record)) return 12;
            // The CLI resets SIGINT to its default handler on the first signal. Persist the attempt
            // BEFORE sending it, so a service/helper crash can never cause a second, forceful CTRL_C.
            if (record.StopSignalAttempted) return 0;

            NativeMethods.FreeConsole();
            if (!NativeMethods.AttachConsole((uint)record.ProcessId)) return 13;
            // AttachConsole resets the control-handler table. Ignore CTRL_C only AFTER attaching.
            if (!NativeMethods.SetConsoleCtrlHandler(IntPtr.Zero, true)) return 14;
            ProtectedFiles.WriteSecret(InstallConfiguration.ProcessRecordPath,
                JsonSerializer.Serialize(record with { StopSignalAttempted = true }));
            if (!NativeMethods.GenerateConsoleCtrlEvent(NativeMethods.CtrlCEvent, 0)) // Same dedicated console.
            {
                // A confirmed submission failure may be retried. Preserve the one-shot guard for
                // successful or indeterminate outcomes, including a helper crash after submission.
                ProtectedFiles.WriteSecret(InstallConfiguration.ProcessRecordPath,
                    JsonSerializer.Serialize(record with { StopSignalAttempted = false }));
                NativeMethods.FreeConsole();
                return 15;
            }
            Thread.Sleep(100);
            NativeMethods.FreeConsole();
            return 0;
        }
        catch
        {
            return 16;
        }
    }
}

internal static class NativeMethods
{
    internal const uint GenericRead = 0x80000000;
    internal const uint FileShareRead = 0x00000001;
    internal const uint FileShareWrite = 0x00000002;
    internal const uint OpenExisting = 3;
    internal const uint HandleFlagInherit = 0x00000001;
    internal const uint StartfUseStdHandles = 0x00000100;
    internal const uint StartfUseShowWindow = 0x00000001;
    internal const ushort SwHide = 0;
    internal const uint CreateNewConsole = 0x00000010;
    internal const uint CreateSuspended = 0x00000004;
    internal const uint CtrlCEvent = 0;

    [StructLayout(LayoutKind.Sequential)]
    internal struct SecurityAttributes
    {
        public int Length;
        public IntPtr SecurityDescriptor;
        [MarshalAs(UnmanagedType.Bool)] public bool InheritHandle;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct StartupInfo
    {
        public int Size;
        public string? Reserved;
        public string? Desktop;
        public string? Title;
        public uint X, Y, XSize, YSize, XCountChars, YCountChars, FillAttribute, Flags;
        public ushort ShowWindow, Reserved2Size;
        public IntPtr Reserved2, StandardInput, StandardOutput, StandardError;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ProcessInformation
    {
        public IntPtr Process, Thread;
        public uint ProcessId, ThreadId;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CreatePipe(out SafeFileHandle readPipe, out SafeFileHandle writePipe, ref SecurityAttributes attributes, uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetHandleInformation(SafeFileHandle handle, uint mask, uint flags);

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern SafeFileHandle CreateFile(string fileName, uint access, uint share,
        ref SecurityAttributes attributes, uint creation, uint flags, IntPtr template);

    [DllImport("kernel32.dll", EntryPoint = "CreateProcessW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CreateProcess(string applicationName, StringBuilder commandLine, IntPtr processAttributes,
        IntPtr threadAttributes, [MarshalAs(UnmanagedType.Bool)] bool inheritHandles, uint flags, IntPtr environment,
        string? currentDirectory, ref StartupInfo startup, out ProcessInformation processInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern uint ResumeThread(IntPtr thread);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool TerminateProcess(IntPtr process, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool FreeConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool AttachConsole(uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetConsoleCtrlHandler(IntPtr handler, [MarshalAs(UnmanagedType.Bool)] bool add);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GenerateConsoleCtrlEvent(uint controlEvent, uint processGroupId);
}
