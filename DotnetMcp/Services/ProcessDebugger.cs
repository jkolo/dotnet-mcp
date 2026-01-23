using System.Diagnostics;
using System.Runtime.InteropServices;
using ClrDebug;
using DotnetMcp.Infrastructure;
using DotnetMcp.Models;
using Microsoft.Extensions.Logging;
using static System.Runtime.InteropServices.NativeLibrary;

namespace DotnetMcp.Services;

/// <summary>
/// Low-level process debugging operations using ICorDebug via ClrDebug.
/// </summary>
public sealed class ProcessDebugger : IProcessDebugger, IDisposable
{
    private readonly ILogger<ProcessDebugger> _logger;
    private DbgShim? _dbgShim;
    private CorDebug? _corDebug;
    private CorDebugProcess? _process;
    private readonly object _lock = new();

    private SessionState _currentState = SessionState.Disconnected;
    private PauseReason? _currentPauseReason;
    private SourceLocation? _currentLocation;
    private int? _activeThreadId;
    private StepMode? _pendingStepMode;

    public ProcessDebugger(ILogger<ProcessDebugger> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public event EventHandler<SessionStateChangedEventArgs>? StateChanged;

    /// <inheritdoc />
    public event EventHandler<BreakpointHitEventArgs>? BreakpointHit;

    /// <inheritdoc />
    public event EventHandler<ModuleLoadedEventArgs>? ModuleLoaded;

    /// <inheritdoc />
    public event EventHandler<ModuleUnloadedEventArgs>? ModuleUnloaded;

    /// <inheritdoc />
    public event EventHandler<StepCompleteEventArgs>? StepCompleted;

    /// <inheritdoc />
    public event EventHandler<ExceptionHitEventArgs>? ExceptionHit;

    /// <inheritdoc />
    public bool IsAttached
    {
        get
        {
            lock (_lock)
            {
                return _process != null;
            }
        }
    }

    /// <inheritdoc />
    public SessionState CurrentState
    {
        get
        {
            lock (_lock)
            {
                return _currentState;
            }
        }
    }

    /// <inheritdoc />
    public PauseReason? CurrentPauseReason
    {
        get
        {
            lock (_lock)
            {
                return _currentPauseReason;
            }
        }
    }

    /// <inheritdoc />
    public SourceLocation? CurrentLocation
    {
        get
        {
            lock (_lock)
            {
                return _currentLocation;
            }
        }
    }

    /// <inheritdoc />
    public int? ActiveThreadId
    {
        get
        {
            lock (_lock)
            {
                return _activeThreadId;
            }
        }
    }

    /// <inheritdoc />
    public bool IsNetProcess(int pid)
    {
        try
        {
            var process = Process.GetProcessById(pid);
            // Check for .NET runtime modules
            foreach (ProcessModule module in process.Modules)
            {
                var name = module.ModuleName.ToLowerInvariant();
                if (name == "coreclr.dll" || name == "libcoreclr.so" || name == "libcoreclr.dylib" ||
                    name == "clr.dll" || name == "clrjit.dll")
                {
                    return true;
                }
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <inheritdoc />
    public ProcessInfo? GetProcessInfo(int pid)
    {
        try
        {
            var process = Process.GetProcessById(pid);
            var mainModule = process.MainModule;

            return new ProcessInfo(
                Pid: pid,
                Name: process.ProcessName,
                ExecutablePath: mainModule?.FileName ?? string.Empty,
                IsManaged: IsNetProcess(pid),
                CommandLine: null, // Platform-specific to retrieve
                RuntimeVersion: null // Will be determined after attach
            );
        }
        catch
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<ProcessInfo> AttachAsync(int pid, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        // Validate process exists and is .NET
        var processInfo = GetProcessInfo(pid);
        if (processInfo == null)
        {
            throw new InvalidOperationException($"Process {pid} not found");
        }

        if (!processInfo.IsManaged)
        {
            throw new InvalidOperationException($"Process {pid} is not a .NET application");
        }

        // Initialize ICorDebug via dbgshim for this specific process
        await Task.Run(() => InitializeCorDebugForProcess(pid), cancellationToken);

        // Attach to process
        await Task.Run(() =>
        {
            lock (_lock)
            {
                _process = _corDebug!.DebugActiveProcess(pid, win32Attach: false);
                UpdateState(SessionState.Running);
            }
        }, cancellationToken);

        // Get runtime version after attach
        var runtimeVersion = GetRuntimeVersion();

        return processInfo with { RuntimeVersion = runtimeVersion };
    }

    /// <inheritdoc />
    public Task<ProcessInfo> LaunchAsync(
        string program,
        string[]? args = null,
        string? cwd = null,
        Dictionary<string, string>? env = null,
        bool stopAtEntry = true,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        // Validate program exists first
        if (!File.Exists(program))
        {
            throw new FileNotFoundException($"Program not found: {program}");
        }

        // Launch requires different DbgShim approach:
        // 1. CreateProcessForLaunch to start suspended
        // 2. RegisterForRuntimeStartup to get callback when CLR loads
        // This is not yet implemented
        throw new NotImplementedException(
            "Launch functionality requires DbgShim.CreateProcessForLaunch and RegisterForRuntimeStartup. " +
            "Use AttachAsync to attach to an already running process.");
    }

    /// <inheritdoc />
    public async Task DetachAsync(CancellationToken cancellationToken = default)
    {
        await Task.Run(() =>
        {
            lock (_lock)
            {
                if (_process != null)
                {
                    try
                    {
                        // Stop the process before detaching to ensure it's synchronized
                        _process.Stop(0);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Stop before detach failed (may already be stopped)");
                    }

                    try
                    {
                        _process.Detach();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Detach failed, process may have exited");
                    }
                    _process = null;
                }

                // Note: Don't call _corDebug.Terminate() here - after detach the process
                // continues running and ICorDebug may still have references.
                // Terminate should only be called when the debugged process has exited.
                // _corDebug can be reused for subsequent attach/launch operations.

                UpdateState(SessionState.Disconnected);
            }
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task TerminateAsync(CancellationToken cancellationToken = default)
    {
        await Task.Run(() =>
        {
            lock (_lock)
            {
                if (_process != null)
                {
                    _process.Terminate(exitCode: 0);
                    _process = null;
                }
                _corDebug?.Terminate();
                _corDebug = null;
                UpdateState(SessionState.Disconnected);
            }
        }, cancellationToken);
    }

    private void InitializeDbgShim()
    {
        lock (_lock)
        {
            if (_dbgShim != null) return;

            // Find dbgshim library
            var dbgshimPath = FindDbgShim();
            if (dbgshimPath == null)
            {
                throw new InvalidOperationException("Could not find dbgshim library. Ensure .NET SDK is installed.");
            }

            _logger.LogDebug("Loading dbgshim from: {Path}", dbgshimPath);

            // Load dbgshim native library and create wrapper
            var dbgShimHandle = Load(dbgshimPath);
            _dbgShim = new DbgShim(dbgShimHandle);
        }
    }

    private void InitializeCorDebugForProcess(int pid)
    {
        lock (_lock)
        {
            if (_corDebug != null) return;

            InitializeDbgShim();

            // Enumerate CLR instances in the target process
            var enumResult = _dbgShim!.EnumerateCLRs(pid);
            if (enumResult.Items.Length == 0)
            {
                throw new InvalidOperationException($"No CLR runtime found in process {pid}. Is it a .NET application?");
            }

            // Use the first CLR instance found
            var runtime = enumResult.Items[0];
            _logger.LogDebug("Found CLR at: {Path}", runtime.Path);

            // Get version string for this CLR
            var versionStr = _dbgShim.CreateVersionStringFromModule(pid, runtime.Path);
            _logger.LogDebug("CLR version: {Version}", versionStr);

            // Create ICorDebug interface for this version
            _corDebug = _dbgShim.CreateDebuggingInterfaceFromVersionEx(
                CorDebugInterfaceVersion.CorDebugVersion_4_0,
                versionStr);

            _corDebug.Initialize();

            // Set up managed callback
            var callback = CreateManagedCallback();
            _corDebug.SetManagedHandler(callback);
        }
    }

    private string? FindDbgShim()
    {
        // Get platform-specific package name and library name
        string packageName;
        string runtimePath;
        string libraryName;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            packageName = "microsoft.diagnostics.dbgshim.win-x64";
            runtimePath = Path.Combine("runtimes", "win-x64", "native");
            libraryName = "dbgshim.dll";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            packageName = "microsoft.diagnostics.dbgshim.linux-x64";
            runtimePath = Path.Combine("runtimes", "linux-x64", "native");
            libraryName = "libdbgshim.so";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            packageName = "microsoft.diagnostics.dbgshim.osx-x64";
            runtimePath = Path.Combine("runtimes", "osx-x64", "native");
            libraryName = "libdbgshim.dylib";
        }
        else
        {
            return null;
        }

        // Search through all potential NuGet package locations
        foreach (var nugetPath in GetNuGetPackagesPaths())
        {
            var packagePath = Path.Combine(nugetPath, packageName);
            if (!Directory.Exists(packagePath)) continue;

            var latestVersion = Directory.GetDirectories(packagePath)
                .OrderByDescending(d => d)
                .FirstOrDefault();

            if (latestVersion != null)
            {
                var libraryPath = Path.Combine(latestVersion, runtimePath, libraryName);
                if (File.Exists(libraryPath))
                {
                    _logger.LogDebug("Found dbgshim at: {Path}", libraryPath);
                    return libraryPath;
                }
            }
        }

        _logger.LogWarning("dbgshim library not found in any NuGet package location");
        return null;
    }

    private static IEnumerable<string> GetNuGetPackagesPaths()
    {
        // 1. Check NUGET_PACKAGES environment variable first (standard override)
        var envPath = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        if (!string.IsNullOrEmpty(envPath) && Directory.Exists(envPath))
        {
            yield return envPath;
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // 2. Check common NuGet cache locations
        // Linux non-standard location (some distros use this)
        var cachePath = Path.Combine(home, ".cache", "NuGetPackages");
        if (Directory.Exists(cachePath))
        {
            yield return cachePath;
        }

        // Standard NuGet packages folder (Linux/Windows/macOS default)
        var defaultPath = Path.Combine(home, ".nuget", "packages");
        if (Directory.Exists(defaultPath))
        {
            yield return defaultPath;
        }

        // Windows-specific: LocalApplicationData location
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var windowsPath = Path.Combine(localAppData, "NuGet", "v3-cache");
            if (Directory.Exists(windowsPath))
            {
                yield return windowsPath;
            }
        }
    }

    private string? GetRuntimeVersion()
    {
        // Runtime version detection after attach
        // This would be implemented using ICorDebugProcess queries
        return ".NET (version detection pending)";
    }

    private static string BuildCommandLine(string program, string[]? args)
    {
        if (program.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            // .NET DLL - need to launch via dotnet
            var argsList = new List<string> { "dotnet", $"\"{program}\"" };
            if (args != null)
            {
                argsList.AddRange(args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a));
            }
            return string.Join(" ", argsList);
        }
        else
        {
            // Direct executable
            var argsList = new List<string> { $"\"{program}\"" };
            if (args != null)
            {
                argsList.AddRange(args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a));
            }
            return string.Join(" ", argsList);
        }
    }

    private void UpdateState(SessionState newState, PauseReason? pauseReason = null,
        SourceLocation? location = null, int? threadId = null)
    {
        var oldState = _currentState;
        _currentState = newState;
        _currentPauseReason = pauseReason;
        _currentLocation = location;
        _activeThreadId = threadId;

        StateChanged?.Invoke(this, new SessionStateChangedEventArgs
        {
            NewState = newState,
            OldState = oldState,
            PauseReason = pauseReason,
            Location = location,
            ThreadId = threadId
        });
    }

    /// <inheritdoc />
    public IReadOnlyList<LoadedModuleInfo> GetLoadedModules()
    {
        lock (_lock)
        {
            if (_process == null)
            {
                return Array.Empty<LoadedModuleInfo>();
            }

            var modules = new List<LoadedModuleInfo>();

            try
            {
                // Enumerate all app domains
                foreach (var appDomain in _process.AppDomains)
                {
                    // Enumerate all assemblies in the app domain
                    foreach (var assembly in appDomain.Assemblies)
                    {
                        // Enumerate all modules in the assembly
                        foreach (var module in assembly.Modules)
                        {
                            var moduleName = module.Name ?? string.Empty;
                            var isDynamic = module.IsDynamic;
                            var isInMemory = module.IsInMemory;

                            modules.Add(new LoadedModuleInfo
                            {
                                ModulePath = moduleName,
                                IsDynamic = isDynamic,
                                IsInMemory = isInMemory,
                                NativeModule = module
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error enumerating loaded modules");
            }

            return modules;
        }
    }

    /// <inheritdoc />
    public async Task ContinueAsync(CancellationToken cancellationToken = default)
    {
        await Task.Run(() =>
        {
            lock (_lock)
            {
                if (_process == null)
                {
                    throw new InvalidOperationException("Cannot continue: debugger is not attached to any process");
                }

                if (_currentState != SessionState.Paused)
                {
                    throw new InvalidOperationException($"Cannot continue: process is not paused (current state: {_currentState})");
                }

                _logger.LogDebug("Continuing execution...");
                _process.Continue(false);
                UpdateState(SessionState.Running);
                _logger.LogInformation("Execution resumed");
            }
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task StepAsync(StepMode mode, CancellationToken cancellationToken = default)
    {
        await Task.Run(() =>
        {
            lock (_lock)
            {
                if (_process == null)
                {
                    throw new InvalidOperationException("Cannot step: debugger is not attached to any process");
                }

                if (_currentState != SessionState.Paused)
                {
                    throw new InvalidOperationException($"Cannot step: process is not paused (current state: {_currentState})");
                }

                if (_activeThreadId == null)
                {
                    throw new InvalidOperationException("Cannot step: no active thread");
                }

                _logger.LogDebug("Stepping {Mode}...", mode);

                // Get the active thread
                var thread = GetThreadById(_activeThreadId.Value);
                if (thread == null)
                {
                    throw new InvalidOperationException($"Cannot step: active thread {_activeThreadId} not found");
                }

                // Create stepper on the active frame
                var frame = thread.ActiveFrame as CorDebugILFrame;
                if (frame == null)
                {
                    throw new InvalidOperationException("Cannot step: no managed frame available");
                }

                var stepper = frame.CreateStepper();

                // Track pending step for callback
                _pendingStepMode = mode;

                // Configure step based on mode
                switch (mode)
                {
                    case StepMode.In:
                        stepper.Step(bStepIn: true);
                        break;
                    case StepMode.Over:
                        stepper.Step(bStepIn: false);
                        break;
                    case StepMode.Out:
                        stepper.StepOut();
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(mode), mode, "Invalid step mode");
                }

                // Continue execution - the stepper will pause at the next step location
                _process.Continue(false);
                UpdateState(SessionState.Running);
                _logger.LogInformation("Step {Mode} initiated", mode);
            }
        }, cancellationToken);
    }

    private CorDebugThread? GetThreadById(int threadId)
    {
        if (_process == null) return null;

        foreach (var thread in _process.Threads)
        {
            if ((int)thread.Id == threadId)
            {
                return thread;
            }
        }
        return null;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_process != null)
            {
                try
                {
                    _process.Detach();
                }
                catch
                {
                    // Process may have already exited
                }
                _process = null;
            }

            if (_corDebug != null)
            {
                try
                {
                    // Terminate can throw CORDBG_E_ILLEGAL_SHUTDOWN_ORDER if process
                    // was detached but is still running. This is safe to ignore.
                    _corDebug.Terminate();
                }
                catch
                {
                    // Safe to ignore shutdown order errors
                }
                _corDebug = null;
            }
        }
    }

    private CorDebugManagedCallback CreateManagedCallback()
    {
        var callback = new CorDebugManagedCallback();

        // Note: OnAnyEvent fires BEFORE specific handlers for every event.
        // Specific handlers below will call Continue when appropriate.
        // Events without specific handlers would cause the process to hang,
        // so we've added handlers for all known events.

        // CRITICAL: Handle process creation (needed for attach)
        callback.OnCreateProcess += (sender, e) =>
        {
            _logger.LogDebug("Process created/attached");
            e.Controller.Continue(false);
        };

        // CRITICAL: Must call Attach on new AppDomains
        callback.OnCreateAppDomain += (sender, e) =>
        {
            _logger.LogDebug("AppDomain created: {Name}", e.AppDomain.Name);
            e.AppDomain.Attach();
            e.Controller.Continue(false);
        };

        // Handle AppDomain exit
        callback.OnExitAppDomain += (sender, e) =>
        {
            _logger.LogDebug("AppDomain exited");
            e.Controller.Continue(false);
        };

        // Handle breakpoint events
        callback.OnBreakpoint += (sender, e) =>
        {
            var locationInfo = GetCurrentLocationInfo(e.Thread);
            var location = locationInfo?.Location;
            var threadId = (int)e.Thread.Id;
            var timestamp = DateTime.UtcNow;

            lock (_lock)
            {
                UpdateState(SessionState.Paused, Models.PauseReason.Breakpoint, location, threadId);
            }

            // Notify listeners about the breakpoint hit with full location info
            BreakpointHit?.Invoke(this, new BreakpointHitEventArgs
            {
                ThreadId = threadId,
                Location = location,
                Timestamp = timestamp,
                MethodToken = locationInfo?.MethodToken,
                ILOffset = locationInfo?.ILOffset,
                ModulePath = locationInfo?.ModulePath
            });

            // Don't call Continue - let the session manager decide
        };

        // Handle exception events (ManagedCallback1 - legacy, continue execution)
        callback.OnException += (sender, e) =>
        {
            // ManagedCallback1 OnException is called for first-chance exceptions
            // We use OnException2 for detailed handling, so just continue here
            e.Controller.Continue(false);
        };

        // Handle exception events with first-chance/second-chance differentiation (ManagedCallback2)
        callback.OnException2 += (sender, e) =>
        {
            var location = GetCurrentLocation(e.Thread);
            var threadId = (int)e.Thread.Id;
            var timestamp = DateTime.UtcNow;

            // Determine exception type from event
            var isFirstChance = e.EventType == ClrDebug.CorDebugExceptionCallbackType.DEBUG_EXCEPTION_FIRST_CHANCE ||
                                e.EventType == ClrDebug.CorDebugExceptionCallbackType.DEBUG_EXCEPTION_USER_FIRST_CHANCE;
            var isUnhandled = e.EventType == ClrDebug.CorDebugExceptionCallbackType.DEBUG_EXCEPTION_UNHANDLED;

            // Get exception information from the thread
            var (exceptionType, exceptionMessage) = GetExceptionInfo(e.Thread);

            _logger.LogDebug("Exception2: {Type} at {Location}, firstChance={IsFirstChance}, unhandled={IsUnhandled}",
                exceptionType, location?.File, isFirstChance, isUnhandled);

            // Fire the exception hit event for exception breakpoint matching
            ExceptionHit?.Invoke(this, new ExceptionHitEventArgs
            {
                ThreadId = threadId,
                Location = location,
                Timestamp = timestamp,
                ExceptionType = exceptionType,
                ExceptionMessage = exceptionMessage,
                IsFirstChance = isFirstChance,
                IsUnhandled = isUnhandled
            });

            // Only pause for unhandled exceptions by default
            // Exception breakpoints will control pausing for first-chance
            if (isUnhandled)
            {
                lock (_lock)
                {
                    UpdateState(SessionState.Paused, Models.PauseReason.Exception, location, threadId);
                }
                // Don't call Continue - let the session manager decide
            }
            else
            {
                // For first-chance exceptions, let the BreakpointManager decide
                // based on registered exception breakpoints
                // If no exception breakpoint handlers stop it, continue
                e.Controller.Continue(false);
            }
        };

        // Handle process exit
        callback.OnExitProcess += (sender, e) =>
        {
            lock (_lock)
            {
                _process = null;
                UpdateState(SessionState.Disconnected);
            }
            e.Controller.Continue(false);
        };

        // Handle module loads (for pending breakpoint binding)
        callback.OnLoadModule += (sender, e) =>
        {
            try
            {
                var module = e.Module;
                var moduleName = module.Name ?? string.Empty;
                var isDynamic = module.IsDynamic;
                var isInMemory = module.IsInMemory;

                // Get base address and size if available
                ulong baseAddress = 0;
                uint size = 0;
                try
                {
                    baseAddress = (ulong)module.BaseAddress;
                    size = (uint)module.Size;
                }
                catch
                {
                    // Some modules may not have this info
                }

                _logger.LogDebug("Module loaded: {ModuleName} (dynamic={IsDynamic}, inMemory={IsInMemory})",
                    moduleName, isDynamic, isInMemory);

                // Fire module loaded event for breakpoint binding
                ModuleLoaded?.Invoke(this, new ModuleLoadedEventArgs
                {
                    ModulePath = moduleName,
                    BaseAddress = baseAddress,
                    Size = size,
                    IsDynamic = isDynamic,
                    IsInMemory = isInMemory,
                    NativeModule = module
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error processing module load event");
            }

            // Continue after module load
            e.Controller.Continue(false);
        };

        // Handle module unloads (for bound breakpoint invalidation)
        callback.OnUnloadModule += (sender, e) =>
        {
            try
            {
                var module = e.Module;
                var moduleName = module.Name ?? string.Empty;

                _logger.LogDebug("Module unloaded: {ModuleName}", moduleName);

                // Fire module unloaded event for breakpoint state transition
                ModuleUnloaded?.Invoke(this, new ModuleUnloadedEventArgs
                {
                    ModulePath = moduleName
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error processing module unload event");
            }

            e.Controller.Continue(false);
        };

        // Handle assembly loads
        callback.OnLoadAssembly += (sender, e) =>
        {
            e.Controller.Continue(false);
        };

        // Handle thread creation
        callback.OnCreateThread += (sender, e) =>
        {
            e.Controller.Continue(false);
        };

        // Handle thread exit
        callback.OnExitThread += (sender, e) =>
        {
            e.Controller.Continue(false);
        };

        // Handle step complete
        callback.OnStepComplete += (sender, e) =>
        {
            var location = GetCurrentLocation(e.Thread);
            var threadId = (int)e.Thread.Id;
            var timestamp = DateTime.UtcNow;

            // Get the step mode that was pending
            StepMode stepMode;
            lock (_lock)
            {
                stepMode = _pendingStepMode ?? StepMode.Over;
                _pendingStepMode = null;
                UpdateState(SessionState.Paused, Models.PauseReason.Step, location, threadId);
            }

            // Map ClrDebug step reason to our enum
            var reason = e.Reason switch
            {
                ClrDebug.CorDebugStepReason.STEP_NORMAL => StepCompleteReason.Normal,
                ClrDebug.CorDebugStepReason.STEP_RETURN => StepCompleteReason.Normal,
                ClrDebug.CorDebugStepReason.STEP_CALL => StepCompleteReason.Normal,
                ClrDebug.CorDebugStepReason.STEP_EXCEPTION_FILTER => StepCompleteReason.Exception,
                ClrDebug.CorDebugStepReason.STEP_EXCEPTION_HANDLER => StepCompleteReason.Exception,
                ClrDebug.CorDebugStepReason.STEP_INTERCEPT => StepCompleteReason.Interrupted,
                ClrDebug.CorDebugStepReason.STEP_EXIT => StepCompleteReason.Normal,
                _ => StepCompleteReason.Normal
            };

            _logger.LogDebug("Step complete: thread={ThreadId}, reason={Reason}, location={Location}",
                threadId, reason, location?.File);

            // Notify listeners
            StepCompleted?.Invoke(this, new StepCompleteEventArgs
            {
                ThreadId = threadId,
                Location = location,
                StepMode = stepMode,
                Reason = reason,
                Timestamp = timestamp
            });

            // Don't call Continue - let the session manager decide
        };

        // Handle debug break (Ctrl+C or Debugger.Break())
        callback.OnBreak += (sender, e) =>
        {
            var location = GetCurrentLocation(e.Thread);
            var threadId = (int)e.Thread.Id;

            lock (_lock)
            {
                UpdateState(SessionState.Paused, Models.PauseReason.Pause, location, threadId);
            }

            _logger.LogDebug("Debug break on thread {ThreadId}", threadId);
            // Don't call Continue - let the session manager decide
        };

        // Handle assembly unload
        callback.OnUnloadAssembly += (sender, e) =>
        {
            e.Controller.Continue(false);
        };

        // Handle log messages
        callback.OnLogMessage += (sender, e) =>
        {
            _logger.LogDebug("Target log: [{Level}] {Message}", e.LogSwitchName, e.Message);
            e.Controller.Continue(false);
        };

        // Handle log switch changes
        callback.OnLogSwitch += (sender, e) =>
        {
            e.Controller.Continue(false);
        };

        // Handle name changes (thread/process renamed)
        callback.OnNameChange += (sender, e) =>
        {
            e.Controller.Continue(false);
        };

        // Handle symbol updates
        callback.OnUpdateModuleSymbols += (sender, e) =>
        {
            e.Controller.Continue(false);
        };

        // Handle debugger errors
        callback.OnDebuggerError += (sender, e) =>
        {
            _logger.LogError("Debugger error: HRESULT=0x{ErrorHR:X8}, ErrorCode={ErrorCode}",
                (uint)e.ErrorHR, e.ErrorCode);
            e.Controller.Continue(false);
        };

        // Handle eval completion
        callback.OnEvalComplete += (sender, e) =>
        {
            e.Controller.Continue(false);
        };

        // Handle eval exception
        callback.OnEvalException += (sender, e) =>
        {
            e.Controller.Continue(false);
        };

        // Handle edit and continue remap
        callback.OnEditAndContinueRemap += (sender, e) =>
        {
            e.Controller.Continue(false);
        };

        // Handle break on exception
        callback.OnBreakpointSetError += (sender, e) =>
        {
            _logger.LogWarning("Breakpoint set error on thread {ThreadId}", e.Thread.Id);
            e.Controller.Continue(false);
        };

        return callback;
    }

    private static SourceLocation? GetCurrentLocation(CorDebugThread thread)
    {
        var info = GetCurrentLocationInfo(thread);
        return info?.Location;
    }

    /// <summary>
    /// Gets detailed location information including IL offset for PDB mapping.
    /// </summary>
    private static LocationInfo? GetCurrentLocationInfo(CorDebugThread thread)
    {
        try
        {
            var frame = thread.ActiveFrame;
            if (frame == null) return null;

            var function = frame.Function;
            var module = function.Module;
            var methodToken = (int)function.Token;

            // Try to get IL offset from the frame
            int? ilOffset = null;
            try
            {
                // Cast to ILFrame to get IP
                var ilFrame = frame.Raw as ClrDebug.ICorDebugILFrame;
                if (ilFrame != null)
                {
                    ilFrame.GetIP(out var nOffset, out _);
                    ilOffset = (int)nOffset;
                }
            }
            catch
            {
                // IL offset not available
            }

            // Create basic location - PDB reading will enhance this later
            var location = new SourceLocation(
                File: "Unknown",
                Line: 0,
                FunctionName: $"0x{methodToken:X8}",
                ModuleName: module.Name
            );

            return new LocationInfo(
                Location: location,
                MethodToken: methodToken,
                ILOffset: ilOffset,
                ModulePath: module.Name
            );
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Extracts exception type and message from the current exception on a thread.
    /// </summary>
    private static (string Type, string Message) GetExceptionInfo(CorDebugThread thread)
    {
        try
        {
            // Get the current exception object from the thread
            var exceptionValue = thread.CurrentException;
            if (exceptionValue == null)
            {
                return ("Unknown", "");
            }

            // Get the exception type
            var exceptionType = GetExceptionTypeName(exceptionValue);

            // Try to get the exception message
            // Note: Full message extraction requires ICorDebugEval which is complex
            // For now, just return the type name
            var exceptionMessage = TryGetExceptionMessage(exceptionValue);

            return (exceptionType, exceptionMessage);
        }
        catch (Exception ex)
        {
            // Log is not available in static method, return empty
            return ("Unknown", $"Error getting exception info: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets the type name of an exception from its ICorDebugValue.
    /// </summary>
    private static string GetExceptionTypeName(CorDebugValue exceptionValue)
    {
        try
        {
            // Dereference if it's a reference
            var value = exceptionValue;
            if (value is CorDebugReferenceValue refValue)
            {
                var dereferencedValue = refValue.Dereference();
                if (dereferencedValue != null)
                {
                    value = dereferencedValue;
                }
            }

            // Get the object value
            if (value is CorDebugObjectValue objValue)
            {
                var classType = objValue.Class;
                if (classType != null)
                {
                    // Get the type token
                    var typeToken = classType.Token;

                    // Get the module to look up the type name
                    var module = classType.Module;
                    if (module != null)
                    {
                        // Try to get the type name from metadata
                        try
                        {
                            var metaImport = module.GetMetaDataInterface<ClrDebug.MetaDataImport>();
                            var typeProps = metaImport.GetTypeDefProps((int)typeToken);
                            return typeProps.szTypeDef ?? $"Type:0x{typeToken:X8}";
                        }
                        catch
                        {
                            return $"Type:0x{typeToken:X8}";
                        }
                    }
                }
            }

            // Try ExactType property as fallback
            try
            {
                var exactType = value.ExactType;
                if (exactType != null)
                {
                    var typeClass = exactType.Class;
                    if (typeClass != null)
                    {
                        var typeToken = typeClass.Token;
                        var module = typeClass.Module;
                        if (module != null)
                        {
                            try
                            {
                                var metaImport = module.GetMetaDataInterface<ClrDebug.MetaDataImport>();
                                var typeProps = metaImport.GetTypeDefProps((int)typeToken);
                                return typeProps.szTypeDef ?? $"Type:0x{typeToken:X8}";
                            }
                            catch
                            {
                                return $"Type:0x{typeToken:X8}";
                            }
                        }
                    }
                }
            }
            catch
            {
                // ExactType not available
            }

            return "Unknown";
        }
        catch
        {
            return "Unknown";
        }
    }

    /// <summary>
    /// Attempts to get the exception message from an ICorDebugValue.
    /// Note: Full implementation requires ICorDebugEval to call get_Message().
    /// This is a simplified version that returns empty string.
    /// </summary>
    private static string TryGetExceptionMessage(CorDebugValue exceptionValue)
    {
        // Getting the Message property requires calling the getter method via ICorDebugEval
        // which is complex and requires the process to be stopped in a specific way.
        // For T074, this is marked as incomplete - full implementation would:
        // 1. Get the Message property getter method token
        // 2. Create an ICorDebugEval
        // 3. Call the method and wait for completion
        // 4. Extract the string result
        //
        // For now, return empty string - the exception type is the most important info
        return "";
    }

    /// <summary>
    /// Internal record for passing location information.
    /// </summary>
    private sealed record LocationInfo(
        SourceLocation Location,
        int MethodToken,
        int? ILOffset,
        string ModulePath);
}
