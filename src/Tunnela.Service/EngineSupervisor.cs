using System.Diagnostics;
using System.Text;
using Tunnela.Contracts;
using Tunnela.Contracts.Localization;
using Tunnela.Core;

namespace Tunnela.Service;

internal sealed class EngineSupervisor
{
    private readonly InstallConfiguration _installation;
    // _commands serializes lifecycle changes across awaits. Output, exit and timeout observers
    // run independently; _stateLock protects their short shared-state updates, never an await.
    private readonly object _stateLock = new();
    private readonly SemaphoreSlim _commands = new(1, 1);
    private readonly Queue<DiagnosticEntry> _logs = new();
    // Owning a process is separate from a confirmed VPN connection in _snapshot.
    private ConsoleEngineProcess? _engine;
    private Task _exitTask = Task.CompletedTask;
    private TunnelSnapshot _snapshot;
    private bool _stopRequested;
    // Tracks the one-shot stop guard, not proof of signal delivery or process exit.
    private bool _stopSignalAttempted;
    private bool _ownershipBlocked;
    private bool _shuttingDown;

    public EngineSupervisor(InstallConfiguration installation)
    {
        _installation = installation;
        _snapshot = new TunnelSnapshot { EngineAvailable = installation.VerifyEngine(), MessageCode = "Service.Disconnected" };
        try
        {
            var recovered = ConsoleEngineProcess.TryRecover();
            if (recovered is not null)
            {
                _ = AdoptRecovered(recovered); // Shutdown cannot have begun during construction.
            }
            else if (HasUnownedEngine())
            {
                _ownershipBlocked = true;
                _snapshot = _snapshot with
                {
                    State = TunnelState.Unknown,
                    Message = Messages.GetEnglish("Service.UnownedEngine"),
                    MessageCode = "Service.UnownedEngine",
                    ErrorCode = "UnownedEngine",
                };
            }
            else
            {
                ProtectedFiles.DeleteRuntimeFiles();
            }
        }
        catch
        {
            _ownershipBlocked = true;
            _snapshot = _snapshot with
            {
                State = TunnelState.Unknown,
                Message = Messages.GetEnglish("Service.OwnershipCheckFailed"),
                MessageCode = "Service.OwnershipCheckFailed",
                ErrorCode = "OwnershipCheckFailed",
            };
        }
        AddLog("info", "Service.Ready");
    }

    /// <summary>
    /// Takes over an engine verified by its ownership record. Its earlier output stream cannot be regained.
    /// Refused once service shutdown has begun, so shutdown never stops an engine it was not already controlling.
    /// </summary>
    private bool AdoptRecovered(ConsoleEngineProcess engine)
    {
        lock (_stateLock)
        {
            // Checked under the same lock that ShutdownAsync uses to set the flag, so there is no gap.
            if (_shuttingDown)
            {
                engine.Dispose();
                return false;
            }
            _engine = engine;
            _stopRequested = false;
            _stopSignalAttempted = engine.Record.StopSignalAttempted;
            _snapshot = _snapshot with
            {
                State = TunnelState.Unknown,
                Message = Messages.GetEnglish("Service.RecoveredProcessUnknown"),
                MessageCode = "Service.RecoveredProcessUnknown",
                ProcessId = engine.Record.ProcessId,
                ProfileId = engine.Record.ProfileId,
                ErrorCode = "RecoveredProcessUnknown",
            };
        }
        _exitTask = ObserveExitAsync(engine, Task.CompletedTask);
        return true;
    }

    /// <summary>
    /// Re-checks an ownership block set at startup, so that it does not last until the service restarts.
    /// Call only while holding <see cref="_commands"/>. An engine at the Tunnela path that cannot be matched
    /// to a valid ownership record keeps the block. Without such a process, nothing can be controlled
    /// unsafely, so the stale runtime files are removed and the block is lifted. No process is ever
    /// signalled or terminated here.
    /// </summary>
    private void RecheckOwnership()
    {
        lock (_stateLock)
        {
            if (!_ownershipBlocked || _shuttingDown || _engine is not null) return;
        }
        try
        {
            ConsoleEngineProcess? recovered = null;
            // An unreadable record is not fatal: the process list below decides whether control is safe.
            try { recovered = ConsoleEngineProcess.TryRecover(); } catch { }
            if (recovered is not null)
            {
                if (!AdoptRecovered(recovered)) return; // Shutdown began meanwhile: keep the block.
            }
            else
            {
                if (HasUnownedEngine()) return;
                ProtectedFiles.DeleteRuntimeFiles();
                lock (_stateLock)
                {
                    _snapshot = _snapshot with
                    {
                        State = TunnelState.Disconnected,
                        Message = Messages.GetEnglish("Service.Disconnected"),
                        MessageCode = "Service.Disconnected",
                        ProcessId = null,
                        ProfileId = null,
                        StartedAt = null,
                        ErrorCode = null,
                    };
                }
                AddLog("info", "Service.Disconnected");
            }
            lock (_stateLock) _ownershipBlocked = false;
        }
        catch
        {
            // Ownership still cannot be checked: keep the block and the current state.
        }
    }

    public TunnelSnapshot Snapshot
    {
        get { lock (_stateLock) return _snapshot; }
    }

    public List<DiagnosticEntry> Logs
    {
        get { lock (_stateLock) return _logs.ToList(); }
    }

    public Task<ServiceOperationError?> ShutdownAsync()
    {
        lock (_stateLock) _shuttingDown = true;
        return DisconnectAsync(CancellationToken.None);
    }

    public async Task<ServiceOperationError?> ConnectAsync(ServerProfile? profile, CancellationToken cancellationToken)
    {
        if (profile is null) return new("Service.ProfileMissing");
        var errors = ProfileValidator.Validate(profile);
        if (errors.Count != 0) return new("Service.ProfileInvalid");
        // Validate and serialize before touching the active engine or any existing config.
        // Runtime identity belongs to Tunnela. Keep the original imported profile unchanged for storage/export.
        var runtimeProfile = profile with { DeviceName = "Tunnela", LogLevel = "info" };
        string toml;
        try { toml = TomlProfileCodec.Export(runtimeProfile); }
        catch (FormatException) { return new("Service.ProfilePreparationFailed"); }

        await _commands.WaitAsync(cancellationToken);
        try
        {
            RecheckOwnership();
            lock (_stateLock)
            {
                if (_shuttingDown) return new("Service.ShuttingDown");
                if (_ownershipBlocked) return new("Service.OwnershipUnconfirmed");
                if (_engine is not null) return new("Service.AlreadyRunning");
            }
            if (!_installation.VerifyEngine())
            {
                SetFailure("EngineVerificationFailed", "Service.EngineVerificationFailed");
                return new("Service.EngineVerificationFailed");
            }
            if (HasUnownedEngine())
            {
                SetFailure("UnownedEngine", "Service.DuplicateEngineBlocked");
                return new("Service.DuplicateEngineBlocked");
            }

            try
            {
                ProtectedFiles.WriteSecret(InstallConfiguration.RuntimeConfigPath, toml);
                var engine = ConsoleEngineProcess.Start(profile.Id);
                lock (_stateLock)
                {
                    _engine = engine;
                    _stopRequested = false;
                    _stopSignalAttempted = false;
                    _snapshot = new TunnelSnapshot
                    {
                        State = TunnelState.Connecting,
                        Message = Messages.GetEnglish("Service.ProcessStarted"),
                        MessageCode = "Service.ProcessStarted",
                        ProfileId = profile.Id,
                        ProfileName = profile.Name.Length <= 128 ? profile.Name : profile.Name[..128],
                        ProcessId = engine.Record.ProcessId,
                        StartedAt = DateTimeOffset.UtcNow,
                        EngineAvailable = true,
                    };
                }
                AddLog("info", "Service.EngineStarted");
                var outputTask = ReadOutputAsync(engine);
                _exitTask = ObserveExitAsync(engine, outputTask);
                _ = MonitorInitialStateAsync(engine);
                return null;
            }
            catch
            {
                // Startup exceptions never expose TOML, connection links, or OS exception strings.
                SetFailure("EngineStartFailed", "Service.EngineStartFailed");
                try { ProtectedFiles.DeleteRuntimeFiles(); } catch { }
                return new("Service.EngineStartFailed");
            }
        }
        finally { _commands.Release(); }
    }

    public async Task<ServiceOperationError?> DisconnectAsync(CancellationToken cancellationToken)
    {
        await _commands.WaitAsync(cancellationToken);
        try
        {
            // Skipped during service shutdown, so stopping the service never takes over an engine it was not controlling.
            RecheckOwnership();
            ConsoleEngineProcess? engine;
            Task exitTask;
            lock (_stateLock)
            {
                if (_ownershipBlocked) return new("Service.UnsafeProcessControl");
                engine = _engine;
                exitTask = _exitTask;
                if (engine is null) return null;
                _stopRequested = true;
                _snapshot = _snapshot with
                {
                    State = TunnelState.Disconnecting,
                    Message = Messages.GetEnglish("Service.Disconnecting"),
                    MessageCode = "Service.Disconnecting",
                    ErrorCode = null,
                };
            }
            AddLog("info", "Service.DisconnectRequested");

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try
            {
                if (!exitTask.IsCompleted && !_stopSignalAttempted)
                {
                    // Trusted executable; no elevation, shell, or user-selected arguments.
                    var start = new ProcessStartInfo(Path.Combine(InstallConfiguration.InstallDirectory, "Tunnela.Service.exe"))
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden,
                    };
                    start.ArgumentList.Add("--signal-engine");
                    start.ArgumentList.Add(engine.Record.Token);
                    using var helper = Process.Start(start) ?? throw new InvalidOperationException(Messages.GetEnglish("Service.SignalHelperUnavailable"));
                    await helper.WaitForExitAsync(timeout.Token);
                    if (helper.ExitCode != 0 && !exitTask.IsCompleted)
                    {
                        SetStopFailure(engine, "EngineSignalFailed", "Service.EngineSignalFailed");
                        return new("Service.EngineSignalFailed");
                    }
                    if (helper.ExitCode == 0) _stopSignalAttempted = true;
                }
                await exitTask.WaitAsync(timeout.Token);
                return null;
            }
            catch (OperationCanceledException)
            {
                SetStopFailure(engine, "StopTimedOut", "Service.StopTimedOut");
                return new("Service.StopTimedOut");
            }
            catch
            {
                SetStopFailure(engine, "EngineSignalFailed", "Service.StopUnconfirmed");
                return new("Service.StopUnconfirmed");
            }
        }
        finally { _commands.Release(); }
    }

    private async Task ReadOutputAsync(ConsoleEngineProcess engine)
    {
        if (engine.Output is null) return;
        var buffer = new char[1024];
        var line = new StringBuilder(4096);
        bool tooLong = false;
        try
        {
            int count;
            while ((count = await engine.Output.ReadAsync(buffer)) > 0)
            {
                for (var index = 0; index < count; index++)
                {
                    var ch = buffer[index];
                    if (ch == '\n')
                    {
                        if (!tooLong) TryProcessOutputLine(engine, line.ToString());
                        line.Clear();
                        tooLong = false;
                    }
                    else if (line.Length < 4096) line.Append(ch);
                    else tooLong = true;
                }
            }
            if (line.Length != 0 && !tooLong) TryProcessOutputLine(engine, line.ToString());
        }
        catch
        {
            // The pipe failed or was closed. This task must not fault: ObserveExitAsync awaits it
            // before removing the runtime files. The finally block reports Unknown if the engine still runs.
        }
        finally
        {
            lock (_stateLock)
            {
                if (_engine == engine && !_stopRequested && !engine.Process.HasExited)
                {
                    _snapshot = _snapshot with
                    {
                        State = TunnelState.Unknown,
                        Message = Messages.GetEnglish("Service.EngineOutputUnavailable"),
                        MessageCode = "Service.EngineOutputUnavailable",
                        ErrorCode = "EngineOutputUnavailable",
                    };
                }
            }
        }
    }

    /// <summary>
    /// A line that cannot be processed is skipped. Reading must continue: if the service stopped
    /// draining stdout, the engine could block once the pipe buffer is full.
    /// </summary>
    private void TryProcessOutputLine(ConsoleEngineProcess engine, string line)
    {
        try { ProcessOutputLine(engine, line); }
        catch { /* Raw output is never logged, including on failure. */ }
    }

    private void ProcessOutputLine(ConsoleEngineProcess engine, string line)
    {
        var state = EngineLogStateParser.Parse(line);
        if (state is null) return; // Raw output is intentionally discarded, never retained or sent to the GUI.
        lock (_stateLock)
        {
            // Late output from an older session, or output during our stop request, must not
            // overwrite the current session's state. Other observers use the same identity guard.
            if (_engine != engine || _stopRequested) return;
            var messageCode = state switch
            {
                TunnelState.Connected => "Service.Connected",
                TunnelState.Connecting => "Service.Connecting",
                TunnelState.Reconnecting => "Service.Reconnecting",
                TunnelState.Disconnected => "Service.EngineDisconnected",
                _ => "Service.Unknown",
            };
            _snapshot = _snapshot with { State = state.Value, Message = Messages.GetEnglish(messageCode), MessageCode = messageCode, ErrorCode = null };
            AddLog("info", messageCode);
        }
    }

    private async Task ObserveExitAsync(ConsoleEngineProcess engine, Task outputTask)
    {
        await engine.Process.WaitForExitAsync();
        // Nothing below may throw before cleanup: a fault here would keep the plaintext runtime
        // configuration on disk and leave this engine registered, blocking every later connect.
        try { await outputTask.WaitAsync(TimeSpan.FromSeconds(2)); }
        catch (TimeoutException) { engine.Output?.Dispose(); }
        catch { /* Output reading already ended; only its state report was lost. */ }
        string exitCode;
        try { exitCode = engine.Process.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture); }
        catch { exitCode = "unknown"; }
        // Complete cleanup BEFORE publishing that a new engine may start, so an older exit cannot
        // delete the next connection's configuration or ownership record.
        try { ProtectedFiles.DeleteRuntimeFiles(); }
        catch { AddLog("warn", "Service.RuntimeCleanupFailed"); }
        lock (_stateLock)
        {
            if (_engine != engine) return;
            var expected = _stopRequested;
            var messageCode = expected ? "Service.ProcessStopped" : "Service.ProcessExited";
            _engine = null;
            _snapshot = _snapshot with
            {
                State = expected ? TunnelState.Disconnected : TunnelState.Error,
                Message = Messages.GetEnglish(messageCode),
                MessageCode = messageCode,
                ProcessId = null,
                StartedAt = null,
                ErrorCode = expected ? null : $"EngineExited:{exitCode}",
            };
            AddLog(expected ? "info" : "warn", messageCode);
        }
        engine.Dispose();
    }

    private async Task MonitorInitialStateAsync(ConsoleEngineProcess engine)
    {
        await Task.Delay(TimeSpan.FromSeconds(30));
        lock (_stateLock)
        {
            if (_engine == engine && !_stopRequested && _snapshot.State == TunnelState.Connecting)
            {
                _snapshot = _snapshot with
                {
                    State = TunnelState.Unknown,
                    Message = Messages.GetEnglish("Service.ConnectionUnconfirmed"),
                    MessageCode = "Service.ConnectionUnconfirmed",
                    ErrorCode = "ConnectionUnconfirmed",
                };
                AddLog("warn", "Service.ConnectionUnconfirmed");
            }
        }
    }

    private static bool HasUnownedEngine()
    {
        foreach (var process in Process.GetProcessesByName("trusttunnel_client"))
        {
            using (process)
            {
                try
                {
                    if (string.Equals(process.MainModule?.FileName, InstallConfiguration.EnginePath, StringComparison.OrdinalIgnoreCase)) return true;
                }
                catch (InvalidOperationException) { }
                // Access denied propagates: do not start a second engine if ownership cannot be checked.
            }
        }
        return false;
    }

    private void SetFailure(string code, string messageCode)
    {
        // Hashing the engine reads several megabytes; keep it outside the lock that status requests use.
        var engineAvailable = _installation.VerifyEngine();
        lock (_stateLock)
        {
            _snapshot = _snapshot with { State = TunnelState.Error, Message = Messages.GetEnglish(messageCode), MessageCode = messageCode, ErrorCode = code,
                EngineAvailable = engineAvailable };
            AddLog("error", messageCode);
        }
    }

    private void SetStopFailure(ConsoleEngineProcess engine, string code, string messageCode)
    {
        lock (_stateLock)
        {
            if (_engine != engine) return;
            _snapshot = _snapshot with { State = TunnelState.Unknown, Message = Messages.GetEnglish(messageCode), MessageCode = messageCode, ErrorCode = code };
            AddLog("error", messageCode);
        }
    }

    /// <summary>Records a retried control pipe failure; it becomes visible once a client connects again.</summary>
    public void RecordControlChannelFault() => AddLog("warn", "Service.ControlChannelFault");

    private void AddLog(string level, string messageCode)
    {
        lock (_stateLock)
        {
            _logs.Enqueue(new DiagnosticEntry(DateTimeOffset.UtcNow, level, Messages.GetEnglish(messageCode), messageCode));
            while (_logs.Count > 150) _logs.Dequeue();
        }
    }
}
