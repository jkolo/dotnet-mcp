using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Text;
using ClrDebug;
using DebugMcp.Infrastructure;
using DebugMcp.Models;
using DebugMcp.Models.Inspection;
using DebugMcp.Models.Memory;
using DebugMcp.Services.Breakpoints;
using Microsoft.Extensions.Logging;
using static System.Runtime.InteropServices.NativeLibrary;

namespace DebugMcp.Services;

/// <summary>
/// Result of an ICorDebugEval function call.
/// </summary>
internal sealed record EvalResult(
    bool Success,
    CorDebugValue? Value = null,
    Exception? Exception = null);

/// <summary>
/// Low-level process debugging operations using ICorDebug via ClrDebug.
/// </summary>
public sealed class ProcessDebugger : IProcessDebugger, IDisposable
{
    private readonly ILogger<ProcessDebugger> _logger;
    private readonly IPdbSymbolReader _pdbSymbolReader;
    private DbgShim? _dbgShim;
    private CorDebug? _corDebug;
    private CorDebugProcess? _process;
    private readonly object _lock = new();

    private SessionState _currentState = SessionState.Disconnected;
    private PauseReason? _currentPauseReason;
    private SourceLocation? _currentLocation;
    private int? _activeThreadId;
    private StepMode? _pendingStepMode;

    // ICorDebugEval support for expression evaluation
    private TaskCompletionSource<EvalResult>? _evalCompletionSource;
    private CorDebugEval? _currentEval;
    private readonly object _evalLock = new();

    // Launch state
    private IntPtr _unregisterToken;
    private bool _isLaunched;
    private bool _stopAtEntry;
    private int _launchPid;
    private TaskCompletionSource<bool>? _launchCompletionSource;
    private RuntimeStartupCallback? _startupCallbackDelegate; // prevent GC

    public ProcessDebugger(ILogger<ProcessDebugger> logger, IPdbSymbolReader pdbSymbolReader)
    {
        _logger = logger;
        _pdbSymbolReader = pdbSymbolReader;
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
    public async Task<ProcessInfo> LaunchAsync(
        string program,
        string[]? args = null,
        string? cwd = null,
        Dictionary<string, string>? env = null,
        bool stopAtEntry = true,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        // Validate program exists
        if (!File.Exists(program))
        {
            throw new FileNotFoundException($"Program not found: {program}");
        }

        // Validate working directory if specified
        if (cwd != null && !Directory.Exists(cwd))
        {
            throw new DirectoryNotFoundException($"Working directory not found: {cwd}");
        }

        // Default cwd to program's directory
        cwd ??= Path.GetDirectoryName(Path.GetFullPath(program));

        _stopAtEntry = stopAtEntry;
        _launchCompletionSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Initialize DbgShim
        InitializeDbgShim();

        // Build command line
        var commandLine = BuildCommandLine(program, args);
        _logger.LogDebug("Launch command line: {CommandLine}", commandLine);

        // Build environment block
        var envPtr = BuildEnvironmentBlock(env);

        // Step 1: Create process in suspended state
        var launchResult = _dbgShim!.CreateProcessForLaunch(
            commandLine,
            bSuspendProcess: true,
            lpEnvironment: envPtr,
            lpCurrentDirectory: cwd);

        var pid = (int)launchResult.ProcessId;
        _launchPid = pid;
        var resumeHandle = launchResult.ResumeHandle;
        _logger.LogDebug("Process created suspended, PID: {Pid}", pid);

        try
        {
            // Mark as launched before registering callback so ShouldAutoContinue()
            // correctly suppresses Continue calls when stopAtEntry is true
            _isLaunched = true;

            // Step 2: Register for runtime startup callback
            _startupCallbackDelegate = OnRuntimeStartup;
            _unregisterToken = _dbgShim.RegisterForRuntimeStartup(
                pid,
                _startupCallbackDelegate,
                IntPtr.Zero);

            _logger.LogDebug("Registered for runtime startup, PID: {Pid}", pid);

            // Step 3: Resume the process so CLR can load
            _dbgShim.ResumeProcess(resumeHandle);
            _dbgShim.CloseResumeHandle(resumeHandle);
            resumeHandle = IntPtr.Zero;
            _logger.LogDebug("Process resumed, waiting for CLR startup callback");

            // Step 4: Wait for the startup callback to complete
            var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(30);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(effectiveTimeout);

            try
            {
                await _launchCompletionSource.Task.WaitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Clean up the launched process on timeout
                try { Process.GetProcessById(pid).Kill(); } catch { /* ignore */ }
                throw new OperationCanceledException(
                    $"Launch timed out after {effectiveTimeout.TotalSeconds}s waiting for CLR startup");
            }

            var processName = Path.GetFileNameWithoutExtension(program);
            var runtimeVersion = GetRuntimeVersion() ?? ".NET";

            _logger.LaunchedProcess(pid, processName);

            return new ProcessInfo(
                Pid: pid,
                Name: processName,
                ExecutablePath: Path.GetFullPath(program),
                IsManaged: true,
                CommandLine: commandLine,
                RuntimeVersion: runtimeVersion);
        }
        catch
        {
            // Clean up on failure
            if (resumeHandle != IntPtr.Zero)
            {
                try { _dbgShim.CloseResumeHandle(resumeHandle); } catch { /* ignore */ }
            }
            CleanupLaunchState();
            throw;
        }
    }

    /// <summary>
    /// Callback invoked by DbgShim when the CLR runtime starts in the launched process.
    /// Initializes ICorDebug, sets managed handler, and signals launch completion.
    /// </summary>
    private void OnRuntimeStartup(CorDebug? pCordb, IntPtr parameter, HRESULT hr)
    {
        try
        {
            if (hr != HRESULT.S_OK)
            {
                _logger.LogError("Runtime startup callback failed: {HR}", hr);
                _launchCompletionSource?.TrySetException(
                    new InvalidOperationException($"CLR startup failed: {hr}"));
                return;
            }

            if (pCordb == null)
            {
                _launchCompletionSource?.TrySetException(
                    new InvalidOperationException("Runtime startup callback received null CorDebug"));
                return;
            }

            _logger.LogDebug("Runtime startup callback received, initializing debugger");

            lock (_lock)
            {
                _corDebug = pCordb;
                _corDebug.Initialize();

                var callback = CreateManagedCallback();
                _corDebug.SetManagedHandler(callback);

                // Start debugging the process — this triggers OnCreateProcess callback
                _process = _corDebug.DebugActiveProcess(_launchPid, win32Attach: false);

                if (_stopAtEntry)
                {
                    UpdateState(SessionState.Paused, PauseReason.Entry);
                }
                else
                {
                    UpdateState(SessionState.Running);
                }
            }

            _launchCompletionSource?.TrySetResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in runtime startup callback");
            _launchCompletionSource?.TrySetException(ex);
        }
    }

    /// <summary>
    /// Builds a null-terminated Unicode environment block for CreateProcessForLaunch.
    /// Format: "VAR1=val1\0VAR2=val2\0\0"
    /// </summary>
    private static IntPtr BuildEnvironmentBlock(Dictionary<string, string>? env)
    {
        if (env == null || env.Count == 0)
            return IntPtr.Zero;

        // Build null-terminated Unicode environment block: "VAR1=val1\0VAR2=val2\0\0"
        var sb = new StringBuilder();

        // Start with inherited environment
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            sb.Append($"{entry.Key}={entry.Value}\0");
        }

        // Override/add custom variables
        foreach (var (key, value) in env)
        {
            sb.Append($"{key}={value}\0");
        }

        sb.Append('\0'); // Double null terminator

        var block = sb.ToString();
        var ptr = Marshal.StringToHGlobalUni(block);
        return ptr;
    }

    /// <summary>
    /// Cleans up launch-specific state: unregisters runtime startup callback and resets fields.
    /// </summary>
    private void CleanupLaunchState()
    {
        if (_unregisterToken != IntPtr.Zero)
        {
            try
            {
                _dbgShim?.UnregisterForRuntimeStartup(_unregisterToken);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "UnregisterForRuntimeStartup cleanup failed");
            }
            _unregisterToken = IntPtr.Zero;
        }
        _startupCallbackDelegate = null;
        _isLaunched = false;
        _launchCompletionSource = null;
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
                        // BUGFIX: Must continue the process before detaching.
                        // ICorDebug cannot detach from a stopped/paused process.
                        // The process will continue running after we detach.
                        _logger.LogDebug("Continuing process before detach");
                        _process.Continue(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Continue before detach failed (process may have exited)");
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

                // Clean up launch-specific state
                CleanupLaunchState();

                // BUGFIX: Terminate ICorDebug instance after detach to allow reattachment.
                // The previous comment was incorrect - ICorDebug cannot be reused after detach
                // because the managed callback and internal state are invalidated.
                // Without this, subsequent attach operations fail with ERROR_INVALID_PARAMETER.
                if (_corDebug != null)
                {
                    try
                    {
                        _logger.LogDebug("Terminating ICorDebug instance after detach");
                        _corDebug.Terminate();
                    }
                    catch (Exception ex)
                    {
                        // Safe to ignore - may get CORDBG_E_ILLEGAL_SHUTDOWN_ORDER if process
                        // was detached but is still running, or other cleanup errors
                        _logger.LogDebug(ex, "ICorDebug.Terminate() after detach (safe to ignore)");
                    }
                    _corDebug = null;
                    _logger.LogDebug("ICorDebug instance released, ready for new attachment");
                }

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

            // ICorDebug requires the process to be stopped for reliable enumeration.
            bool wasRunning = _currentState == SessionState.Running;
            if (wasRunning)
                _process.Stop(0);

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
            finally
            {
                if (wasRunning)
                    _process.Continue(false);
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
                _stopAtEntry = false; // Allow callbacks to auto-continue from now on
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

    /// <summary>
    /// Gets the thread for the specified threadId, or the active thread if null.
    /// </summary>
    private CorDebugThread? GetThread(int? threadId)
    {
        var effectiveThreadId = threadId ?? _activeThreadId;
        if (effectiveThreadId == null)
        {
            // Try to get the first thread as fallback
            return _process?.Threads.FirstOrDefault();
        }
        return GetThreadById(effectiveThreadId.Value);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ThreadInfo>> PauseAsync(CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            lock (_lock)
            {
                if (_process == null)
                {
                    throw new InvalidOperationException("Cannot pause: debugger is not attached to any process");
                }

                // If already paused, return current thread info (idempotent)
                if (_currentState == SessionState.Paused)
                {
                    _logger.LogDebug("Process already paused, returning current state");
                    return GetThreads();
                }

                _logger.LogDebug("Pausing execution...");

                // Stop all threads
                _process.Stop(0); // 0 = infinite wait

                // Select current thread (first stopped thread or main thread)
                int? selectedThreadId = null;
                foreach (var thread in _process.Threads)
                {
                    var threadId = (int)thread.Id;
                    if (selectedThreadId == null)
                    {
                        selectedThreadId = threadId;
                    }
                    // Prefer thread with active frame
                    try
                    {
                        if (thread.ActiveFrame != null)
                        {
                            selectedThreadId = threadId;
                            break;
                        }
                    }
                    catch
                    {
                        // Thread may not have active frame
                    }
                }

                var location = selectedThreadId != null ? GetLocationForThread(selectedThreadId.Value) : null;
                UpdateState(SessionState.Paused, Models.PauseReason.Pause, location, selectedThreadId);

                _logger.LogInformation("Execution paused, active thread: {ThreadId}", selectedThreadId);

                return GetThreads();
            }
        }, cancellationToken);
    }

    /// <inheritdoc />
    public IReadOnlyList<ThreadInfo> GetThreads()
    {
        lock (_lock)
        {
            if (_process == null)
            {
                throw new InvalidOperationException("Cannot get threads: debugger is not attached to any process");
            }

            var threads = new List<ThreadInfo>();

            foreach (var thread in _process.Threads)
            {
                var threadId = (int)thread.Id;
                var isCurrent = threadId == _activeThreadId;
                var state = MapThreadState(thread);
                var name = GetThreadName(thread);
                SourceLocation? location = null;

                // Get location for stopped threads or the active thread when paused
                if (state == Models.Inspection.ThreadState.Stopped ||
                    (isCurrent && _currentState == SessionState.Paused))
                {
                    location = GetLocationForThread(threadId);
                }

                threads.Add(new ThreadInfo(threadId, name, state, isCurrent, location));
            }

            return threads;
        }
    }

    /// <inheritdoc />
    public (IReadOnlyList<Models.Inspection.StackFrame> Frames, int TotalFrames) GetStackFrames(int? threadId = null, int startFrame = 0, int maxFrames = 20)
    {
        lock (_lock)
        {
            if (_process == null)
            {
                throw new InvalidOperationException("Cannot get stack frames: debugger is not attached to any process");
            }

            if (_currentState != SessionState.Paused)
            {
                throw new InvalidOperationException($"Cannot get stack frames: process is not paused (current state: {_currentState})");
            }

            var targetThreadId = threadId ?? _activeThreadId;
            if (targetThreadId == null)
            {
                throw new InvalidOperationException("Cannot get stack frames: no thread specified and no active thread");
            }

            var thread = GetThreadById(targetThreadId.Value);
            if (thread == null)
            {
                throw new InvalidOperationException($"Thread {targetThreadId} not found");
            }

            var allFrames = new List<Models.Inspection.StackFrame>();
            var frameIndex = 0;

            // Walk through chains and frames
            try
            {
                foreach (var chain in thread.Chains)
                {
                    // Skip unmanaged chains
                    if (!chain.IsManaged)
                    {
                        continue;
                    }

                    foreach (var frame in chain.Frames)
                    {
                        var stackFrame = CreateStackFrame(frame, frameIndex);
                        if (stackFrame != null)
                        {
                            allFrames.Add(stackFrame);
                            frameIndex++;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error enumerating stack frames for thread {ThreadId}", targetThreadId);
            }

            var totalFrames = allFrames.Count;

            // Apply pagination
            var pagedFrames = allFrames
                .Skip(startFrame)
                .Take(maxFrames)
                .ToList();

            return (pagedFrames, totalFrames);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<Variable> GetVariables(int? threadId = null, int frameIndex = 0, string scope = "all", string? expandPath = null)
    {
        lock (_lock)
        {
            if (_process == null)
            {
                throw new InvalidOperationException("Cannot get variables: debugger is not attached to any process");
            }

            if (_currentState != SessionState.Paused)
            {
                throw new InvalidOperationException($"Cannot get variables: process is not paused (current state: {_currentState})");
            }

            var targetThreadId = threadId ?? _activeThreadId;
            if (targetThreadId == null)
            {
                throw new InvalidOperationException("Cannot get variables: no thread specified and no active thread");
            }

            var thread = GetThreadById(targetThreadId.Value);
            if (thread == null)
            {
                throw new InvalidOperationException($"Thread {targetThreadId} not found");
            }

            // Get the specific frame
            CorDebugILFrame? ilFrame = null;
            var currentIndex = 0;

            try
            {
                foreach (var chain in thread.Chains)
                {
                    if (!chain.IsManaged) continue;

                    foreach (var frame in chain.Frames)
                    {
                        if (currentIndex == frameIndex)
                        {
                            ilFrame = frame as CorDebugILFrame;
                            break;
                        }
                        currentIndex++;
                    }
                    if (ilFrame != null) break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error getting frame {FrameIndex} for thread {ThreadId}", frameIndex, targetThreadId);
            }

            if (ilFrame == null)
            {
                throw new InvalidOperationException($"Frame index {frameIndex} not found or is not a managed frame");
            }

            var variables = new List<Variable>();

            // Get locals
            if (scope == "all" || scope == "locals")
            {
                var locals = GetLocals(ilFrame);
                variables.AddRange(locals);
            }

            // Get arguments
            if (scope == "all" || scope == "arguments")
            {
                var args = GetArguments(ilFrame);
                variables.AddRange(args);
            }

            // Get 'this' reference
            if (scope == "all" || scope == "this")
            {
                var thisRef = GetThisReference(ilFrame);
                if (thisRef != null)
                {
                    variables.Add(thisRef);
                }
            }

            // Handle expand path
            if (!string.IsNullOrEmpty(expandPath))
            {
                variables = ExpandVariable(variables, expandPath);
            }

            return variables;
        }
    }

    private Models.Inspection.ThreadState MapThreadState(CorDebugThread thread)
    {
        try
        {
            // When the session is paused (at breakpoint, exception, or user pause),
            // all threads are stopped by the debugger even if their UserState doesn't reflect it
            if (_currentState == SessionState.Paused)
            {
                // Check if this is the active thread (where the break occurred)
                var threadId = (int)thread.Id;
                if (threadId == _activeThreadId)
                {
                    return Models.Inspection.ThreadState.Stopped;
                }
            }

            var userState = thread.UserState;

            if ((userState & CorDebugUserState.USER_STOP_REQUESTED) != 0 ||
                (userState & CorDebugUserState.USER_SUSPENDED) != 0 ||
                (userState & CorDebugUserState.USER_STOPPED) != 0)
            {
                return Models.Inspection.ThreadState.Stopped;
            }

            if ((userState & CorDebugUserState.USER_WAIT_SLEEP_JOIN) != 0)
            {
                return Models.Inspection.ThreadState.Waiting;
            }

            if ((userState & CorDebugUserState.USER_UNSTARTED) != 0)
            {
                return Models.Inspection.ThreadState.NotStarted;
            }

            return Models.Inspection.ThreadState.Running;
        }
        catch
        {
            // If we can't get state, assume running
            return Models.Inspection.ThreadState.Running;
        }
    }

    private string? GetThreadName(CorDebugThread thread)
    {
        // T049: Thread name resolution via Thread.Name property
        // The Name property requires ICorDebugEval to call the getter,
        // but we can try to read the internal _name field directly for better performance.
        try
        {
            // Try to get the managed Thread object
            var threadObj = thread.Object;
            if (threadObj == null)
            {
                return null;
            }

            // Dereference if it's a reference
            CorDebugValue value = threadObj;
            if (value is CorDebugReferenceValue refValue && !refValue.IsNull)
            {
                value = refValue.Dereference();
            }

            // Try to get the _name field from the Thread object
            // In .NET, Thread.Name is backed by the _name field
            var nameValue = TryGetFieldValue(value, "_name");
            if (nameValue == null)
            {
                // Try other possible field names (varies by .NET version)
                nameValue = TryGetFieldValue(value, "m_Name");
            }

            if (nameValue != null)
            {
                // Format the string value
                var (displayValue, _, _, _) = FormatValue(nameValue);
                // Remove quotes from string formatting
                if (displayValue.StartsWith("\"") && displayValue.EndsWith("\"") && displayValue.Length >= 2)
                {
                    return displayValue[1..^1];
                }
                return displayValue == "null" ? null : displayValue;
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not get thread name for thread {ThreadId}", (int)thread.Id);
            return null;
        }
    }

    private SourceLocation? GetLocationForThread(int threadId)
    {
        var thread = GetThreadById(threadId);
        if (thread == null) return null;

        try
        {
            var frame = thread.ActiveFrame;
            if (frame == null) return null;

            var function = frame.Function;
            var module = function.Module;
            var methodToken = (int)function.Token;
            var modulePath = module.Name;

            // Try to get IL offset
            int? ilOffset = null;
            try
            {
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

            // Use PDB symbol reader to resolve source location
            if (ilOffset.HasValue && modulePath != null)
            {
                // Use synchronous wrapper for PDB lookup (blocking call in debug context is acceptable)
                var sourceLocationResult = _pdbSymbolReader.FindSourceLocationAsync(modulePath, methodToken, ilOffset.Value)
                    .ConfigureAwait(false).GetAwaiter().GetResult();
                if (sourceLocationResult != null)
                {
                    return new SourceLocation(
                        File: sourceLocationResult.FilePath,
                        Line: sourceLocationResult.Line,
                        FunctionName: sourceLocationResult.FunctionName,
                        ModuleName: modulePath
                    );
                }
            }

            // Fallback: return basic info without source
            var methodName = GetMethodName(function);
            return new SourceLocation(
                File: "Unknown",
                Line: 0,
                FunctionName: methodName,
                ModuleName: modulePath
            );
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error getting location for thread {ThreadId}", threadId);
            return null;
        }
    }

    private Models.Inspection.StackFrame? CreateStackFrame(CorDebugFrame frame, int index)
    {
        try
        {
            var function = frame.Function;
            var module = function.Module;
            var methodToken = (int)function.Token;
            var modulePath = module.Name ?? "Unknown";
            var methodName = GetMethodName(function);

            // Determine if external (no source available)
            var isExternal = true;
            SourceLocation? location = null;

            // Try to get IL offset and source location
            try
            {
                var ilFrame = frame as CorDebugILFrame;
                if (ilFrame != null)
                {
                    var rawFrame = ilFrame.Raw as ClrDebug.ICorDebugILFrame;
                    if (rawFrame != null)
                    {
                        rawFrame.GetIP(out var nOffset, out _);
                        var ilOffset = (int)nOffset;

                        var sourceLocationResult = _pdbSymbolReader.FindSourceLocationAsync(modulePath, methodToken, ilOffset)
                            .ConfigureAwait(false).GetAwaiter().GetResult();
                        if (sourceLocationResult != null)
                        {
                            location = new SourceLocation(
                                File: sourceLocationResult.FilePath,
                                Line: sourceLocationResult.Line,
                                FunctionName: sourceLocationResult.FunctionName,
                                ModuleName: modulePath
                            );
                            isExternal = false;
                        }
                    }
                }
            }
            catch
            {
                // Source location not available
            }

            // Get arguments for the frame
            var arguments = new List<Variable>();
            try
            {
                var ilFrame = frame as CorDebugILFrame;
                if (ilFrame != null)
                {
                    arguments = GetArguments(ilFrame);
                }
            }
            catch
            {
                // Arguments not available
            }

            return new Models.Inspection.StackFrame(
                Index: index,
                Function: methodName,
                Module: Path.GetFileName(modulePath),
                IsExternal: isExternal,
                Location: location,
                Arguments: arguments.Count > 0 ? arguments : null
            );
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error creating stack frame at index {Index}", index);
            return null;
        }
    }

    private string GetMethodName(CorDebugFunction function)
    {
        try
        {
            var module = function.Module;
            var metaImport = module.GetMetaDataInterface<ClrDebug.MetaDataImport>();
            var methodToken = (int)function.Token;

            // Get method properties
            var methodProps = metaImport.GetMethodProps(methodToken);
            var methodName = methodProps.szMethod ?? $"0x{methodToken:X8}";

            // Get the declaring type
            var typeToken = (int)methodProps.pClass;
            var typeProps = metaImport.GetTypeDefProps(typeToken);
            var typeName = typeProps.szTypeDef ?? "Unknown";

            return $"{typeName}.{methodName}";
        }
        catch
        {
            return $"0x{function.Token:X8}";
        }
    }

    private List<Variable> GetLocals(CorDebugILFrame ilFrame)
    {
        var locals = new List<Variable>();

        try
        {
            var localValues = ilFrame.EnumerateLocalVariables().ToList();

            for (int i = 0; i < localValues.Count; i++)
            {
                var value = localValues[i];
                var variable = CreateVariable($"local_{i}", value, VariableScope.Local);
                if (variable != null)
                {
                    locals.Add(variable);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error enumerating local variables");
        }

        return locals;
    }

    private List<Variable> GetArguments(CorDebugILFrame ilFrame)
    {
        var arguments = new List<Variable>();

        try
        {
            var argValues = ilFrame.EnumerateArguments().ToList();

            // Get method parameter names from metadata
            var function = ilFrame.Function;
            var paramNames = GetParameterNames(function);

            for (int i = 0; i < argValues.Count; i++)
            {
                var value = argValues[i];
                var name = i < paramNames.Count ? paramNames[i] : $"arg_{i}";

                // Skip 'this' parameter (first arg in instance methods)
                if (i == 0 && name == "this")
                {
                    continue;
                }

                var variable = CreateVariable(name, value, VariableScope.Argument);
                if (variable != null)
                {
                    arguments.Add(variable);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error enumerating arguments");
        }

        return arguments;
    }

    private Variable? GetThisReference(CorDebugILFrame ilFrame)
    {
        try
        {
            var argValues = ilFrame.EnumerateArguments().ToList();
            if (argValues.Count == 0) return null;

            // First argument is 'this' for instance methods
            var firstArg = argValues[0];

            // Check if this is actually 'this' (not a static method)
            var function = ilFrame.Function;
            var module = function.Module;
            var metaImport = module.GetMetaDataInterface<ClrDebug.MetaDataImport>();
            var methodToken = (int)function.Token;
            var methodProps = metaImport.GetMethodProps(methodToken);

            // Check if method is static
            var isStatic = ((int)methodProps.pdwAttr & 0x10) != 0; // mdStatic = 0x10
            if (isStatic) return null;

            return CreateVariable("this", firstArg, VariableScope.This);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error getting 'this' reference");
            return null;
        }
    }

    private List<string> GetParameterNames(CorDebugFunction function)
    {
        var names = new List<string>();

        try
        {
            var module = function.Module;
            var metaImport = module.GetMetaDataInterface<ClrDebug.MetaDataImport>();
            var methodToken = (int)function.Token;

            // Get method params
            var methodProps = metaImport.GetMethodProps(methodToken);
            var isStatic = ((int)methodProps.pdwAttr & 0x10) != 0;

            // Add 'this' placeholder for instance methods
            if (!isStatic)
            {
                names.Add("this");
            }

            // Enumerate parameters
            var paramTokens = metaImport.EnumParams(methodToken).ToList();
            foreach (var paramToken in paramTokens)
            {
                try
                {
                    var paramProps = metaImport.GetParamProps(paramToken);
                    names.Add(paramProps.szName ?? $"param_{paramProps.pulSequence}");
                }
                catch
                {
                    names.Add($"param_{names.Count}");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error getting parameter names");
        }

        return names;
    }

    private Variable? CreateVariable(string name, CorDebugValue value, VariableScope scope, string? path = null)
    {
        try
        {
            var (displayValue, typeName, hasChildren, childCount) = FormatValue(value);

            return new Variable(
                Name: name,
                Type: typeName,
                Value: displayValue,
                Scope: scope,
                HasChildren: hasChildren,
                ChildrenCount: childCount,
                Path: path
            );
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error creating variable {Name}", name);
            return new Variable(
                Name: name,
                Type: "Unknown",
                Value: "<unavailable>",
                Scope: scope,
                HasChildren: false
            );
        }
    }

    private (string Value, string Type, bool HasChildren, int? ChildCount) FormatValue(CorDebugValue value)
    {
        try
        {
            // Handle null/reference values
            if (value is CorDebugReferenceValue refValue)
            {
                if (refValue.IsNull)
                {
                    var nullType = GetTypeName(value);
                    return ("null", nullType, false, null);
                }

                var derefValue = refValue.Dereference();
                if (derefValue != null)
                {
                    return FormatValue(derefValue);
                }
            }

            // Handle string values
            if (value is CorDebugStringValue stringValue)
            {
                var len = stringValue.Length;
                var str = stringValue.GetString((int)len) ?? "";
                // Truncate long strings
                if (str.Length > 100)
                {
                    str = str.Substring(0, 100) + "...";
                }
                return ($"\"{str}\"", "System.String", false, null);
            }

            // Handle boxed values
            if (value is CorDebugBoxValue boxValue)
            {
                var unboxedValue = boxValue.Object;
                if (unboxedValue != null)
                {
                    return FormatValue(unboxedValue);
                }
            }

            // Handle array values
            if (value is CorDebugArrayValue arrayValue)
            {
                var elementType = GetTypeName(value);
                var count = (int)arrayValue.Count;
                var displayCount = count > 100 ? "100+" : count.ToString();
                return ($"{elementType}[{displayCount}]", elementType + "[]", count > 0, count);
            }

            // Handle object values
            if (value is CorDebugObjectValue objValue)
            {
                var typeName = GetTypeName(value);
                var fieldCount = CountFields(objValue);
                return ($"{{{typeName}}}", typeName, fieldCount > 0, fieldCount);
            }

            // Handle primitive values
            if (value is CorDebugGenericValue genericValue)
            {
                var primitiveValue = GetPrimitiveValue(genericValue);
                var typeName = GetTypeName(value);
                return (primitiveValue, typeName, false, null);
            }

            // Fallback
            var fallbackType = GetTypeName(value);
            return ($"{{{fallbackType}}}", fallbackType, false, null);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error formatting value");
            return ("<error>", "Unknown", false, null);
        }
    }

    private string GetTypeName(CorDebugValue value)
    {
        try
        {
            var exactType = value.ExactType;
            if (exactType != null)
            {
                var typeClass = exactType.Class;
                if (typeClass != null)
                {
                    var module = typeClass.Module;
                    var metaImport = module.GetMetaDataInterface<ClrDebug.MetaDataImport>();
                    var typeProps = metaImport.GetTypeDefProps((int)typeClass.Token);
                    return typeProps.szTypeDef ?? "Unknown";
                }
            }
        }
        catch
        {
            // Type info not available
        }

        return "Unknown";
    }

    private int CountFields(CorDebugObjectValue objValue)
    {
        try
        {
            var objClass = objValue.Class;
            if (objClass == null) return 0;

            var module = objClass.Module;
            var metaImport = module.GetMetaDataInterface<ClrDebug.MetaDataImport>();
            var fields = metaImport.EnumFields((int)objClass.Token).ToList();
            return fields.Count;
        }
        catch
        {
            return 0;
        }
    }

    private string GetPrimitiveValue(CorDebugGenericValue genericValue)
    {
        try
        {
            var size = genericValue.Size;

            // Allocate buffer and read value using Marshal
            var valueBytes = new byte[size];
            var handle = System.Runtime.InteropServices.GCHandle.Alloc(valueBytes, System.Runtime.InteropServices.GCHandleType.Pinned);
            try
            {
                genericValue.GetValue(handle.AddrOfPinnedObject());
            }
            finally
            {
                handle.Free();
            }

            // Determine type and format
            var type = genericValue.Type;

            return type switch
            {
                CorElementType.Boolean => BitConverter.ToBoolean(valueBytes, 0) ? "true" : "false",
                CorElementType.Char => $"'{(char)BitConverter.ToInt16(valueBytes, 0)}'",
                CorElementType.I1 => ((sbyte)valueBytes[0]).ToString(),
                CorElementType.U1 => valueBytes[0].ToString(),
                CorElementType.I2 => BitConverter.ToInt16(valueBytes, 0).ToString(),
                CorElementType.U2 => BitConverter.ToUInt16(valueBytes, 0).ToString(),
                CorElementType.I4 => BitConverter.ToInt32(valueBytes, 0).ToString(),
                CorElementType.U4 => BitConverter.ToUInt32(valueBytes, 0).ToString(),
                CorElementType.I8 => BitConverter.ToInt64(valueBytes, 0).ToString(),
                CorElementType.U8 => BitConverter.ToUInt64(valueBytes, 0).ToString(),
                CorElementType.R4 => BitConverter.ToSingle(valueBytes, 0).ToString(),
                CorElementType.R8 => BitConverter.ToDouble(valueBytes, 0).ToString(),
                CorElementType.I => IntPtr.Size == 4
                    ? BitConverter.ToInt32(valueBytes, 0).ToString()
                    : BitConverter.ToInt64(valueBytes, 0).ToString(),
                CorElementType.U => IntPtr.Size == 4
                    ? BitConverter.ToUInt32(valueBytes, 0).ToString()
                    : BitConverter.ToUInt64(valueBytes, 0).ToString(),
                _ => $"0x{BitConverter.ToString(valueBytes).Replace("-", "")}"
            };
        }
        catch
        {
            return "<unavailable>";
        }
    }

    private List<Variable> ExpandVariable(List<Variable> variables, string expandPath)
    {
        // For object expansion, we need to find the variable and get its children
        // Uses circular reference detection to prevent infinite loops
        var expanded = new List<Variable>();
        var visitedAddresses = new HashSet<ulong>();

        foreach (var variable in variables)
        {
            if (variable.Path == expandPath || variable.Name == expandPath)
            {
                // For now, return the variable itself with expanded flag
                // Full implementation would get child fields from the stored ICorDebugValue
                expanded.Add(variable);
            }
        }

        return expanded.Count > 0 ? expanded : variables;
    }

    /// <summary>
    /// Gets the memory address of a value for circular reference detection.
    /// </summary>
    private ulong? GetValueAddress(CorDebugValue value)
    {
        try
        {
            if (value is CorDebugReferenceValue refValue)
            {
                if (!refValue.IsNull)
                {
                    return refValue.Value;
                }
            }
        }
        catch
        {
            // Address not available
        }
        return null;
    }

    /// <summary>
    /// Checks if a value has been visited (circular reference detection).
    /// </summary>
    private bool IsCircularReference(CorDebugValue value, HashSet<ulong> visitedAddresses)
    {
        var address = GetValueAddress(value);
        if (address.HasValue)
        {
            if (visitedAddresses.Contains(address.Value))
            {
                return true;
            }
            visitedAddresses.Add(address.Value);
        }
        return false;
    }

    /// <summary>
    /// Formats a value with circular reference detection.
    /// </summary>
    private (string Value, string Type, bool HasChildren, int? ChildCount) FormatValueWithCircularCheck(
        CorDebugValue value,
        HashSet<ulong> visitedAddresses)
    {
        try
        {
            // Check for circular reference first
            if (IsCircularReference(value, visitedAddresses))
            {
                var typeName = GetTypeName(value);
                return ($"{{circular reference: {typeName}}}", typeName, false, null);
            }

            return FormatValue(value);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error formatting value with circular check");
            return ("<error>", "Unknown", false, null);
        }
    }

    /// <summary>
    /// Gets child variables of an object with circular reference detection.
    /// </summary>
    private List<Variable> GetChildVariables(CorDebugValue parentValue, string parentPath, HashSet<ulong>? visitedAddresses = null)
    {
        visitedAddresses ??= new HashSet<ulong>();
        var children = new List<Variable>();

        try
        {
            // Check for circular reference
            if (IsCircularReference(parentValue, visitedAddresses))
            {
                return children; // Return empty list for circular refs
            }

            // Dereference if reference value
            if (parentValue is CorDebugReferenceValue refValue)
            {
                if (refValue.IsNull) return children;
                parentValue = refValue.Dereference();
                if (parentValue == null) return children;
            }

            // Handle boxed values
            if (parentValue is CorDebugBoxValue boxValue)
            {
                parentValue = boxValue.Object;
            }

            // Handle array values
            if (parentValue is CorDebugArrayValue arrayValue)
            {
                var count = (int)arrayValue.Count;
                var maxElements = Math.Min(count, 100); // Limit to 100 elements
                for (int i = 0; i < maxElements; i++)
                {
                    try
                    {
                        var element = arrayValue.GetElementAtPosition(i);
                        var elementPath = $"{parentPath}[{i}]";
                        var (displayValue, typeName, hasChildren, childCount) = FormatValueWithCircularCheck(element, visitedAddresses);
                        children.Add(new Variable(
                            Name: $"[{i}]",
                            Type: typeName,
                            Value: displayValue,
                            Scope: VariableScope.Element,
                            HasChildren: hasChildren,
                            ChildrenCount: childCount,
                            Path: elementPath
                        ));
                    }
                    catch
                    {
                        // Skip unavailable elements
                    }
                }
                return children;
            }

            // Handle object values - enumerate fields
            if (parentValue is CorDebugObjectValue objValue)
            {
                var objClass = objValue.Class;
                if (objClass == null) return children;

                var module = objClass.Module;
                var metaImport = module.GetMetaDataInterface<ClrDebug.MetaDataImport>();

                var fields = metaImport.EnumFields((int)objClass.Token).ToList();
                foreach (var fieldToken in fields)
                {
                    try
                    {
                        var fieldProps = metaImport.GetFieldProps(fieldToken);
                        var fieldName = fieldProps.szField ?? $"field_{fieldToken}";
                        var fieldPath = $"{parentPath}.{fieldName}";

                        var fieldValue = objValue.GetFieldValue(objClass.Raw, (int)fieldToken);
                        if (fieldValue != null)
                        {
                            var (displayValue, typeName, hasChildren, childCount) = FormatValueWithCircularCheck(fieldValue, visitedAddresses);
                            children.Add(new Variable(
                                Name: fieldName,
                                Type: typeName,
                                Value: displayValue,
                                Scope: VariableScope.Field,
                                HasChildren: hasChildren,
                                ChildrenCount: childCount,
                                Path: fieldPath
                            ));
                        }
                    }
                    catch
                    {
                        // Skip unavailable fields
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error getting child variables for {ParentPath}", parentPath);
        }

        return children;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            CleanupLaunchState();

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

    /// <summary>
    /// Returns false when the process was launched with stopAtEntry and hasn't been
    /// explicitly continued yet — callbacks must NOT call Continue in that case.
    /// </summary>
    private bool ShouldAutoContinue()
        => !(_isLaunched && _stopAtEntry);

    private CorDebugManagedCallback CreateManagedCallback()
    {
        var callback = new CorDebugManagedCallback();

        // Note: OnAnyEvent fires BEFORE specific handlers for every event.
        // Specific handlers below will call Continue when appropriate.
        // Events without specific handlers would cause the process to hang,
        // so we've added handlers for all known events.

        // CRITICAL: Handle process creation (needed for attach and launch)
        callback.OnCreateProcess += (sender, e) =>
        {
            _logger.LogDebug("Process created/attached (isLaunched={IsLaunched}, stopAtEntry={StopAtEntry})",
                _isLaunched, _stopAtEntry);

            lock (_lock)
            {
                _process ??= e.Process;
            }

            if (ShouldAutoContinue())
                e.Controller.Continue(false);
            else
                _logger.LogDebug("Launched with stopAtEntry - process will remain paused");
        };

        // CRITICAL: Must call Attach on new AppDomains
        callback.OnCreateAppDomain += (sender, e) =>
        {
            _logger.LogDebug("AppDomain created: {Name}", e.AppDomain.Name);
            e.AppDomain.Attach();
            if (ShouldAutoContinue())
                e.Controller.Continue(false);
        };

        // Handle AppDomain exit
        callback.OnExitAppDomain += (sender, e) =>
        {
            _logger.LogDebug("AppDomain exited");
            if (ShouldAutoContinue())
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
            var args = new BreakpointHitEventArgs
            {
                ThreadId = threadId,
                Location = location,
                Timestamp = timestamp,
                MethodToken = locationInfo?.MethodToken,
                ILOffset = locationInfo?.ILOffset,
                ModulePath = locationInfo?.ModulePath
            };
            BreakpointHit?.Invoke(this, args);

            // If a listener (e.g. BreakpointManager) determined condition is false,
            // auto-continue instead of staying paused
            if (args.ShouldContinue)
            {
                lock (_lock)
                {
                    UpdateState(SessionState.Running);
                }
                e.Controller.Continue(false);
            }
            // Otherwise stay paused - let the session manager decide
        };

        // Handle exception events (ManagedCallback1 - legacy, continue execution)
        callback.OnException += (sender, e) =>
        {
            // ManagedCallback1 OnException is called for first-chance exceptions
            // We use OnException2 for detailed handling, so just continue here
            if (ShouldAutoContinue())
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
                if (ShouldAutoContinue())
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
            if (ShouldAutoContinue())
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

            if (ShouldAutoContinue())
                e.Controller.Continue(false);
        };

        // Handle assembly loads
        callback.OnLoadAssembly += (sender, e) =>
        {
            if (ShouldAutoContinue())
                e.Controller.Continue(false);
        };

        // Handle thread creation
        callback.OnCreateThread += (sender, e) =>
        {
            if (ShouldAutoContinue())
                e.Controller.Continue(false);
        };

        // Handle thread exit
        callback.OnExitThread += (sender, e) =>
        {
            if (ShouldAutoContinue())
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
            if (ShouldAutoContinue())
                e.Controller.Continue(false);
        };

        // Handle log messages
        callback.OnLogMessage += (sender, e) =>
        {
            _logger.LogDebug("Target log: [{Level}] {Message}", e.LogSwitchName, e.Message);
            if (ShouldAutoContinue())
                e.Controller.Continue(false);
        };

        // Handle log switch changes
        callback.OnLogSwitch += (sender, e) =>
        {
            if (ShouldAutoContinue())
                e.Controller.Continue(false);
        };

        // Handle name changes (thread/process renamed)
        callback.OnNameChange += (sender, e) =>
        {
            if (ShouldAutoContinue())
                e.Controller.Continue(false);
        };

        // Handle symbol updates
        callback.OnUpdateModuleSymbols += (sender, e) =>
        {
            if (ShouldAutoContinue())
                e.Controller.Continue(false);
        };

        // Handle debugger errors
        callback.OnDebuggerError += (sender, e) =>
        {
            _logger.LogError("Debugger error: HRESULT=0x{ErrorHR:X8}, ErrorCode={ErrorCode}",
                (uint)e.ErrorHR, e.ErrorCode);
            if (ShouldAutoContinue())
                e.Controller.Continue(false);
        };

        // Handle eval completion (T064)
        callback.OnEvalComplete += (sender, e) =>
        {
            _logger.LogDebug("EvalComplete received for eval on thread {ThreadId}", e.Thread?.Id);
            bool wasControlledEval = false;
            lock (_evalLock)
            {
                if (_evalCompletionSource != null && _currentEval != null)
                {
                    wasControlledEval = true;
                    try
                    {
                        var result = _currentEval.Result;
                        _evalCompletionSource.TrySetResult(new EvalResult(true, result));
                    }
                    catch (Exception ex)
                    {
                        _evalCompletionSource.TrySetResult(new EvalResult(false, null, ex));
                    }
                }
            }
            // If this was a controlled eval, don't Continue - we want to stay paused
            // The caller will handle resumption if needed
            if (!wasControlledEval && ShouldAutoContinue())
            {
                e.Controller.Continue(false);
            }
        };

        // Handle eval exception (T066)
        callback.OnEvalException += (sender, e) =>
        {
            _logger.LogDebug("EvalException received for eval on thread {ThreadId}", e.Thread?.Id);
            bool wasControlledEval = false;
            lock (_evalLock)
            {
                if (_evalCompletionSource != null && _currentEval != null)
                {
                    wasControlledEval = true;
                    try
                    {
                        // The exception object is the result
                        var exceptionValue = _currentEval.Result;
                        var (exType, exMessage) = FormatExceptionValue(exceptionValue);
                        _evalCompletionSource.TrySetResult(new EvalResult(
                            false,
                            exceptionValue,
                            new InvalidOperationException($"{exType}: {exMessage}")));
                    }
                    catch (Exception ex)
                    {
                        _evalCompletionSource.TrySetResult(new EvalResult(false, null, ex));
                    }
                }
            }
            // If this was a controlled eval, don't Continue - we want to stay paused
            if (!wasControlledEval && ShouldAutoContinue())
            {
                e.Controller.Continue(false);
            }
        };

        // Handle edit and continue remap
        callback.OnEditAndContinueRemap += (sender, e) =>
        {
            if (ShouldAutoContinue())
                e.Controller.Continue(false);
        };

        // Handle break on exception
        callback.OnBreakpointSetError += (sender, e) =>
        {
            _logger.LogWarning("Breakpoint set error on thread {ThreadId}", e.Thread.Id);
            if (ShouldAutoContinue())
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
    /// Formats an exception value from ICorDebugEval result.
    /// </summary>
    private static (string Type, string Message) FormatExceptionValue(CorDebugValue? exceptionValue)
    {
        if (exceptionValue == null)
        {
            return ("Unknown", "No exception details available");
        }

        var exType = GetExceptionTypeName(exceptionValue);
        var exMessage = TryGetExceptionMessage(exceptionValue);
        return (exType, string.IsNullOrEmpty(exMessage) ? "Exception occurred during evaluation" : exMessage);
    }

    /// <summary>
    /// Internal record for passing location information.
    /// </summary>
    private sealed record LocationInfo(
        SourceLocation Location,
        int MethodToken,
        int? ILOffset,
        string ModulePath);

    /// <summary>
    /// Calls a function in the debuggee via ICorDebugEval (T062, T063).
    /// </summary>
    /// <param name="thread">The thread to use for evaluation.</param>
    /// <param name="function">The function to call.</param>
    /// <param name="thisArg">The 'this' argument for instance methods, or null for static methods.</param>
    /// <param name="args">Additional arguments to pass to the function.</param>
    /// <param name="timeoutMs">Timeout in milliseconds (T065).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result of the function call, or null if it failed.</returns>
    private async Task<EvalResult> CallFunctionAsync(
        CorDebugThread thread,
        CorDebugFunction function,
        CorDebugValue? thisArg,
        CorDebugValue[]? args,
        int timeoutMs = 5000,
        CancellationToken cancellationToken = default)
    {
        CorDebugEval? eval = null;
        TaskCompletionSource<EvalResult> completionSource;

        lock (_evalLock)
        {
            if (_evalCompletionSource != null)
            {
                return new EvalResult(false, null, new InvalidOperationException("Another evaluation is already in progress"));
            }

            try
            {
                eval = thread.CreateEval();
                _currentEval = eval;
                _evalCompletionSource = new TaskCompletionSource<EvalResult>(TaskCreationOptions.RunContinuationsAsynchronously);
                completionSource = _evalCompletionSource;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create ICorDebugEval");
                return new EvalResult(false, null, ex);
            }
        }

        try
        {
            // Prepare arguments: for instance methods, 'this' is the first argument
            var allArgs = new List<CorDebugValue>();
            if (thisArg != null)
            {
                allArgs.Add(thisArg);
            }
            if (args != null)
            {
                allArgs.AddRange(args);
            }

            _logger.LogDebug("Calling function with {ArgCount} arguments (including this)", allArgs.Count);

            // Call the function
            // Note: CallFunction is obsolete but CallParameterizedFunction requires type arguments
            // For simple method calls, CallFunction should work
            eval.CallFunction(
                function.Raw,
                allArgs.Count,
                allArgs.Select(a => a.Raw).ToArray());

            // Continue execution to let the eval run
            lock (_lock)
            {
                _process?.Continue(false);
            }

            // Wait for completion with timeout (T065)
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeoutMs);

            try
            {
                var result = await completionSource.Task.WaitAsync(cts.Token);
                return result;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Timeout - abort the eval (T065)
                _logger.LogWarning("Function evaluation timed out after {TimeoutMs}ms, aborting", timeoutMs);
                try
                {
                    eval.Abort();
                }
                catch (Exception abortEx)
                {
                    _logger.LogDebug(abortEx, "Error aborting evaluation");
                }
                return new EvalResult(false, null, new TimeoutException($"Function evaluation timed out after {timeoutMs}ms"));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during function evaluation");
            return new EvalResult(false, null, ex);
        }
        finally
        {
            lock (_evalLock)
            {
                _currentEval = null;
                _evalCompletionSource = null;
            }
        }
    }

    /// <summary>
    /// Finds a property getter method on a type.
    /// </summary>
    /// <param name="objectValue">The object to find the property on.</param>
    /// <param name="propertyName">The name of the property.</param>
    /// <returns>The getter method, or null if not found.</returns>
    private CorDebugFunction? FindPropertyGetter(CorDebugValue objectValue, string propertyName)
    {
        try
        {
            // Dereference if needed
            var value = objectValue;
            if (value is CorDebugReferenceValue refValue && !refValue.IsNull)
            {
                value = refValue.Dereference();
            }

            if (value is not CorDebugObjectValue objValue)
            {
                return null;
            }

            var classType = objValue.Class;
            if (classType == null)
            {
                return null;
            }

            var module = classType.Module;
            if (module == null)
            {
                return null;
            }

            // Get metadata to find the property getter
            var metaImport = module.GetMetaDataInterface<ClrDebug.MetaDataImport>();

            // Look for get_PropertyName method
            var getterName = $"get_{propertyName}";

            // Traverse the type hierarchy (current type and base types)
            var currentTypeToken = (int)classType.Token;
            while (currentTypeToken != 0)
            {
                // Enumerate methods on the current type
                var methodEnum = metaImport.EnumMethods(currentTypeToken);
                foreach (var methodToken in methodEnum)
                {
                    try
                    {
                        var methodProps = metaImport.GetMethodProps(methodToken);
                        if (methodProps.szMethod == getterName)
                        {
                            // Found the getter - get the function
                            return module.GetFunctionFromToken((uint)methodToken);
                        }
                    }
                    catch
                    {
                        // Skip methods we can't inspect
                    }
                }

                // Get base type token
                try
                {
                    var typeProps = metaImport.GetTypeDefProps(currentTypeToken);
                    var baseTypeToken = (int)typeProps.ptkExtends;

                    // Check if base type is a TypeDef in this module or a TypeRef (external)
                    if (baseTypeToken == 0 || baseTypeToken == 0x01000000) // nil token or System.Object TypeRef
                    {
                        break; // No more base types to check
                    }

                    // If it's a TypeDef (0x02xxxxxx), continue traversal
                    if ((baseTypeToken & 0xFF000000) == 0x02000000)
                    {
                        currentTypeToken = baseTypeToken;
                        _logger.LogDebug("Traversing to base type {BaseTypeToken:X8} for property getter '{PropertyName}'",
                            baseTypeToken, propertyName);
                    }
                    else
                    {
                        // TypeRef (0x01xxxxxx) - cross-module base type, stop here
                        // (Would need to resolve to the actual module to continue)
                        _logger.LogDebug("Base type {BaseTypeToken:X8} is in another module, stopping traversal",
                            baseTypeToken);
                        break;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error getting base type for property getter lookup");
                    break;
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error finding property getter for '{PropertyName}'", propertyName);
            return null;
        }
    }

    /// <summary>
    /// Finds a method on a type by name.
    /// </summary>
    /// <param name="objectValue">The object to find the method on.</param>
    /// <param name="methodName">The name of the method.</param>
    /// <returns>The method, or null if not found.</returns>
    private CorDebugFunction? FindMethod(CorDebugValue objectValue, string methodName)
    {
        try
        {
            // Dereference if needed
            var value = objectValue;
            if (value is CorDebugReferenceValue refValue && !refValue.IsNull)
            {
                value = refValue.Dereference();
            }

            if (value is not CorDebugObjectValue objValue)
            {
                return null;
            }

            var classType = objValue.Class;
            if (classType == null)
            {
                return null;
            }

            var module = classType.Module;
            if (module == null)
            {
                return null;
            }

            // Get metadata to find the method
            var metaImport = module.GetMetaDataInterface<ClrDebug.MetaDataImport>();
            var typeToken = (int)classType.Token;

            // Enumerate methods on the type
            var methodEnum = metaImport.EnumMethods(typeToken);
            foreach (var methodToken in methodEnum)
            {
                try
                {
                    var methodProps = metaImport.GetMethodProps(methodToken);
                    if (methodProps.szMethod == methodName)
                    {
                        // Found the method
                        return module.GetFunctionFromToken((uint)methodToken);
                    }
                }
                catch
                {
                    // Skip methods we can't inspect
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error finding method '{MethodName}'", methodName);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<EvaluationResult> EvaluateAsync(
        string expression,
        int? threadId = null,
        int frameIndex = 0,
        int timeoutMs = 5000,
        CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_process == null)
            {
                throw new InvalidOperationException("Cannot evaluate: debugger is not attached to any process");
            }

            if (_currentState != SessionState.Paused)
            {
                throw new InvalidOperationException($"Cannot evaluate: process is not paused (current state: {_currentState})");
            }
        }

        if (string.IsNullOrWhiteSpace(expression))
        {
            return new EvaluationResult(
                Success: false,
                Error: new EvaluationError(
                    Code: "syntax_error",
                    Message: "Expression cannot be empty",
                    Position: 0));
        }

        _logger.LogDebug("Evaluating expression '{Expression}' on thread {ThreadId} frame {FrameIndex}",
            expression, threadId ?? ActiveThreadId ?? 0, frameIndex);

        try
        {
            // Get the thread for evaluation
            var thread = GetThread(threadId);
            if (thread == null)
            {
                return new EvaluationResult(
                    Success: false,
                    Error: new EvaluationError(
                        Code: "invalid_thread",
                        Message: $"Thread {threadId ?? ActiveThreadId ?? 0} not found"));
            }

            // Get the IL frame for the specified thread/frame
            var ilFrame = GetILFrame(threadId, frameIndex);
            if (ilFrame == null)
            {
                return new EvaluationResult(
                    Success: false,
                    Error: new EvaluationError(
                        Code: "variable_unavailable",
                        Message: "Could not access stack frame for evaluation"));
            }

            // Try to resolve the expression as a variable reference first
            var result = TryResolveAsVariable(expression, ilFrame);
            if (result != null)
            {
                return result;
            }

            // Try to resolve as property path using ICorDebugEval for property getters (T062, T063)
            result = await TryResolvePropertyPathAsync(
                expression,
                ilFrame,
                thread,
                timeoutMs,
                cancellationToken);
            if (result != null)
            {
                return result;
            }

            // If we get here, the expression wasn't recognized
            return new EvaluationResult(
                Success: false,
                Error: new EvaluationError(
                    Code: "syntax_error",
                    Message: $"Unrecognized expression: {expression}",
                    Position: 0));
        }
        catch (OperationCanceledException)
        {
            return new EvaluationResult(
                Success: false,
                Error: new EvaluationError(
                    Code: "eval_timeout",
                    Message: $"Expression evaluation timed out after {timeoutMs}ms"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error evaluating expression '{Expression}'", expression);
            return new EvaluationResult(
                Success: false,
                Error: new EvaluationError(
                    Code: "eval_exception",
                    Message: ex.Message,
                    ExceptionType: ex.GetType().FullName));
        }
    }

    private EvaluationResult? TryResolveAsVariable(string expression, CorDebugILFrame ilFrame)
    {
        try
        {
            // Try to find the expression as a local variable or argument
            var value = TryGetLocalOrArgument(expression, ilFrame);
            if (value != null)
            {
                var (displayValue, typeName, hasChildren, _) = FormatValue(value);
                return new EvaluationResult(
                    Success: true,
                    Value: displayValue,
                    Type: typeName,
                    HasChildren: hasChildren);
            }

            // Try 'this' reference
            if (expression == "this")
            {
                var thisValue = TryGetThisForEval(ilFrame);
                if (thisValue != null)
                {
                    var (displayValue, typeName, hasChildren, _) = FormatValue(thisValue);
                    return new EvaluationResult(
                        Success: true,
                        Value: displayValue,
                        Type: typeName,
                        HasChildren: hasChildren);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not resolve '{Expression}' as variable", expression);
        }

        return null;
    }

    private async Task<EvaluationResult?> TryResolvePropertyPathAsync(
        string expression,
        CorDebugILFrame ilFrame,
        CorDebugThread thread,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        // Handle simple property paths like "variable.Property" or "this.Field"
        var parts = expression.Split('.');
        if (parts.Length < 2) return null;

        try
        {
            // Get the root object
            CorDebugValue? currentValue = null;

            if (parts[0] == "this")
            {
                currentValue = TryGetThisForEval(ilFrame);
            }
            else
            {
                currentValue = TryGetLocalOrArgument(parts[0], ilFrame);
            }

            if (currentValue == null) return null;

            // Navigate the property path
            for (int i = 1; i < parts.Length; i++)
            {
                var memberName = parts[i];
                var pathSoFar = string.Join(".", parts.Take(i));

                // First try field access (fast, no ICorDebugEval needed)
                var fieldValue = TryGetFieldValue(currentValue, memberName);
                if (fieldValue != null)
                {
                    currentValue = fieldValue;
                    continue;
                }

                // Field not found - try property getter via ICorDebugEval (T062)
                var propertyGetter = FindPropertyGetter(currentValue, memberName);
                if (propertyGetter != null)
                {
                    _logger.LogDebug("Calling property getter for '{MemberName}' via ICorDebugEval", memberName);
                    var evalResult = await CallFunctionAsync(
                        thread,
                        propertyGetter,
                        currentValue,  // 'this' for the property getter
                        null,          // No additional arguments
                        timeoutMs,
                        cancellationToken);

                    if (!evalResult.Success || evalResult.Value == null)
                    {
                        var errorMessage = evalResult.Exception?.Message ?? "Property getter failed";
                        return new EvaluationResult(
                            Success: false,
                            Error: new EvaluationError(
                                Code: "eval_exception",
                                Message: $"Cannot access property '{memberName}' on '{pathSoFar}': {errorMessage}"));
                    }

                    currentValue = evalResult.Value;
                    continue;
                }

                // Try method call if it looks like a method (T063)
                if (memberName.EndsWith("()"))
                {
                    var methodName = memberName[..^2]; // Remove ()
                    var method = FindMethod(currentValue, methodName);
                    if (method != null)
                    {
                        _logger.LogDebug("Calling method '{MethodName}' via ICorDebugEval", methodName);
                        var evalResult = await CallFunctionAsync(
                            thread,
                            method,
                            currentValue,  // 'this' for the method
                            null,          // No additional arguments for parameterless methods
                            timeoutMs,
                            cancellationToken);

                        if (!evalResult.Success || evalResult.Value == null)
                        {
                            var errorMessage = evalResult.Exception?.Message ?? "Method call failed";
                            return new EvaluationResult(
                                Success: false,
                                Error: new EvaluationError(
                                    Code: "eval_exception",
                                    Message: $"Cannot call method '{methodName}' on '{pathSoFar}': {errorMessage}"));
                        }

                        currentValue = evalResult.Value;
                        continue;
                    }
                }

                // Neither field, property, nor method found
                return new EvaluationResult(
                    Success: false,
                    Error: new EvaluationError(
                        Code: "variable_unavailable",
                        Message: $"Cannot access member '{memberName}' on path '{pathSoFar}'"));
            }

            var (displayValue, typeName, hasChildren, _) = FormatValue(currentValue);
            return new EvaluationResult(
                Success: true,
                Value: displayValue,
                Type: typeName,
                HasChildren: hasChildren);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not resolve property path '{Expression}'", expression);
        }

        return null;
    }

    private CorDebugValue? TryGetLocalOrArgument(string name, CorDebugILFrame ilFrame)
    {
        try
        {
            // Get argument names
            var function = ilFrame.Function;
            var argNames = GetParameterNames(function);

            // Check arguments
            for (int i = 0; i < argNames.Count; i++)
            {
                if (argNames[i] == name || (name == "this" && i == 0 && argNames[i] == "this"))
                {
                    try
                    {
                        return ilFrame.GetArgument((int)i);
                    }
                    catch
                    {
                        // Argument not available
                    }
                }
            }

            // Check locals - use index-based matching since we don't have local names
            var localValues = ilFrame.EnumerateLocalVariables().ToList();
            for (int i = 0; i < localValues.Count; i++)
            {
                // Match local_N pattern
                if (name == $"local_{i}")
                {
                    try
                    {
                        return ilFrame.GetLocalVariable((int)i);
                    }
                    catch
                    {
                        // Local not available
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error getting local or argument '{Name}'", name);
        }

        return null;
    }

    private CorDebugValue? TryGetThisForEval(CorDebugILFrame ilFrame)
    {
        try
        {
            // 'this' is typically argument 0 for instance methods
            var function = ilFrame.Function;
            var module = function.Module;
            var metaImport = module.GetMetaDataInterface<ClrDebug.MetaDataImport>();
            var methodToken = (int)function.Token;
            var methodProps = metaImport.GetMethodProps(methodToken);
            var isStatic = ((int)methodProps.pdwAttr & 0x10) != 0;

            if (!isStatic)
            {
                return ilFrame.GetArgument(0);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error getting 'this' reference for evaluation");
        }

        return null;
    }

    private CorDebugValue? TryGetFieldValue(CorDebugValue parentValue, string fieldName)
    {
        try
        {
            // Dereference if reference value
            if (parentValue is CorDebugReferenceValue refValue)
            {
                if (refValue.IsNull) return null;
                parentValue = refValue.Dereference();
                if (parentValue == null) return null;
            }

            // Handle boxed values
            if (parentValue is CorDebugBoxValue boxValue)
            {
                parentValue = boxValue.Object;
            }

            // Get field value from object
            if (parentValue is CorDebugObjectValue objValue)
            {
                var objClass = objValue.Class;
                if (objClass == null) return null;

                var module = objClass.Module;
                var metaImport = module.GetMetaDataInterface<ClrDebug.MetaDataImport>();

                // Traverse the type hierarchy (current type and base types)
                var currentTypeToken = (int)objClass.Token;
                while (currentTypeToken != 0)
                {
                    // Find the field in current type
                    var fields = metaImport.EnumFields(currentTypeToken).ToList();
                    foreach (var fieldToken in fields)
                    {
                        try
                        {
                            var fieldProps = metaImport.GetFieldProps(fieldToken);
                            if (fieldProps.szField == fieldName)
                            {
                                // Get the class for the type that owns this field
                                var fieldClass = module.GetClassFromToken((uint)currentTypeToken);
                                return objValue.GetFieldValue(fieldClass.Raw, (int)fieldToken);
                            }
                        }
                        catch
                        {
                            // Continue to next field
                        }
                    }

                    // Get base type token
                    try
                    {
                        var typeProps = metaImport.GetTypeDefProps(currentTypeToken);
                        var baseTypeToken = (int)typeProps.ptkExtends;

                        // Check if base type is a TypeDef in this module or a TypeRef (external)
                        if (baseTypeToken == 0 || baseTypeToken == 0x01000000) // nil token or System.Object TypeRef
                        {
                            break; // No more base types to check
                        }

                        // If it's a TypeDef (0x02xxxxxx), continue traversal
                        if ((baseTypeToken & 0xFF000000) == 0x02000000)
                        {
                            currentTypeToken = baseTypeToken;
                            _logger.LogDebug("Traversing to base type {BaseTypeToken:X8} for field '{FieldName}'",
                                baseTypeToken, fieldName);
                        }
                        else
                        {
                            // TypeRef (0x01xxxxxx) - cross-module base type, stop here
                            // (Would need to resolve to the actual module to continue)
                            _logger.LogDebug("Base type {BaseTypeToken:X8} is in another module, stopping traversal",
                                baseTypeToken);
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Error getting base type for field lookup");
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error getting field '{FieldName}'", fieldName);
        }

        return null;
    }

    /// <summary>
    /// Attempts to resolve a member (field or property) value from an object.
    /// First tries direct field access, then backing field, then property getter.
    /// </summary>
    /// <param name="parentValue">The parent object value.</param>
    /// <param name="memberName">The name of the member to access.</param>
    /// <param name="thread">Optional thread for property getter evaluation.</param>
    /// <param name="timeoutMs">Timeout for property getter evaluation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The member value, or null if not found.</returns>
    private async Task<CorDebugValue?> TryGetMemberValueAsync(
        CorDebugValue parentValue,
        string memberName,
        CorDebugThread? thread = null,
        int timeoutMs = 5000,
        CancellationToken cancellationToken = default)
    {
        // 1. Try direct field access
        var fieldValue = TryGetFieldValue(parentValue, memberName);
        if (fieldValue != null)
        {
            _logger.LogDebug("Resolved '{MemberName}' as direct field", memberName);
            return fieldValue;
        }

        // 2. Try auto-property backing field (<Name>k__BackingField)
        var backingFieldName = $"<{memberName}>k__BackingField";
        fieldValue = TryGetFieldValue(parentValue, backingFieldName);
        if (fieldValue != null)
        {
            _logger.LogDebug("Resolved '{MemberName}' via backing field '{BackingFieldName}'", memberName, backingFieldName);
            return fieldValue;
        }

        // 3. Try property getter (requires thread for ICorDebugEval)
        if (thread != null)
        {
            var getter = FindPropertyGetter(parentValue, memberName);
            if (getter != null)
            {
                _logger.LogDebug("Calling property getter for '{MemberName}' via ICorDebugEval", memberName);
                var result = await CallFunctionAsync(thread, getter, parentValue, null, timeoutMs, cancellationToken);
                if (result.Success && result.Value != null)
                {
                    return result.Value;
                }

                if (result.Exception != null)
                {
                    _logger.LogDebug(result.Exception, "Property getter for '{MemberName}' threw exception", memberName);
                }
            }
        }

        _logger.LogDebug("Could not resolve member '{MemberName}'", memberName);
        return null;
    }

    private CorDebugILFrame? GetILFrame(int? threadId, int frameIndex)
    {
        try
        {
            var thread = GetThreadById(threadId ?? ActiveThreadId ?? 0);
            if (thread == null) return null;

            var chains = thread.ActiveChain;
            if (chains == null) return null;

            var frames = chains.EnumerateFrames().ToList();
            int currentIndex = 0;

            foreach (var frame in frames)
            {
                if (frame is CorDebugILFrame ilFrame)
                {
                    if (currentIndex == frameIndex)
                    {
                        return ilFrame;
                    }
                    currentIndex++;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error getting IL frame at index {FrameIndex}", frameIndex);
        }

        return null;
    }

    #region Memory Inspection Operations

    /// <inheritdoc />
    public async Task<ObjectInspection> InspectObjectAsync(
        string objectRef,
        int depth = 1,
        int? threadId = null,
        int frameIndex = 0,
        CancellationToken cancellationToken = default)
    {
        CorDebugThread? thread;

        lock (_lock)
        {
            if (_process == null)
            {
                throw new InvalidOperationException("Cannot inspect object: debugger is not attached to any process");
            }

            if (_currentState != SessionState.Paused)
            {
                throw new InvalidOperationException($"Cannot inspect object: process is not paused (current state: {_currentState})");
            }

            // Get thread for property getter evaluation (needed for ICorDebugEval)
            thread = GetThread(threadId);
        }

        _logger.LogDebug("Inspecting object '{ObjectRef}' with depth {Depth}", objectRef, depth);

        // Resolve the expression to get the object value (async for property getter support)
        var (value, errorMessage) = await ResolveExpressionToValueAsync(
            objectRef,
            threadId,
            frameIndex,
            thread,
            cancellationToken);

        if (value == null)
        {
            throw new InvalidOperationException($"Invalid reference: {errorMessage ?? $"could not resolve '{objectRef}'"}");
        }

        var visitedAddresses = new HashSet<ulong>();
        return InspectObjectValue(value, depth, visitedAddresses);
    }

    /// <summary>
    /// Resolves an expression to a CorDebugValue.
    /// Supports nested property paths like 'this._field.Property'.
    /// </summary>
    /// <param name="expression">The expression to resolve (e.g., "this._currentUser.HomeAddress")</param>
    /// <param name="threadId">Thread ID for ICorDebugEval (required for property getter calls)</param>
    /// <param name="frameIndex">Stack frame index</param>
    /// <param name="thread">Thread for property getter evaluation (can be null for field-only access)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Tuple of (value, errorMessage) - value is null if resolution failed</returns>
    private async Task<(CorDebugValue? Value, string? ErrorMessage)> ResolveExpressionToValueAsync(
        string expression,
        int? threadId,
        int frameIndex,
        CorDebugThread? thread,
        CancellationToken cancellationToken = default)
    {
        // Get the IL frame for the specified thread/frame
        var ilFrame = GetILFrame(threadId, frameIndex);
        if (ilFrame == null) return (null, "Could not get IL frame");

        // Try to resolve as a local variable or argument
        var parts = expression.Split('.');
        CorDebugValue? currentValue = null;
        var resolvedPath = new List<string>();

        // Get the root value
        if (parts[0] == "this")
        {
            currentValue = TryGetThisForEval(ilFrame);
            resolvedPath.Add("this");
        }
        else
        {
            currentValue = TryGetLocalOrArgument(parts[0], ilFrame);
            resolvedPath.Add(parts[0]);
        }

        if (currentValue == null)
        {
            return (null, $"Could not resolve '{parts[0]}'");
        }

        // Navigate the path if there are more parts
        for (int i = 1; i < parts.Length; i++)
        {
            var memberName = parts[i];

            // Use TryGetMemberValueAsync which handles fields, backing fields, and property getters
            var memberValue = await TryGetMemberValueAsync(
                currentValue,
                memberName,
                thread,
                timeoutMs: 5000,
                cancellationToken);

            if (memberValue == null)
            {
                var typeName = GetTypeName(currentValue);
                var currentPath = string.Join(".", resolvedPath);
                return (null, $"Member '{memberName}' not found on type '{typeName}' (at '{currentPath}')");
            }

            // Check for null intermediate value
            if (memberValue is CorDebugReferenceValue refValue && refValue.IsNull && i < parts.Length - 1)
            {
                var currentPath = string.Join(".", resolvedPath) + "." + memberName;
                var nextMember = parts[i + 1];
                return (null, $"Cannot access '{nextMember}': '{currentPath}' is null");
            }

            resolvedPath.Add(memberName);
            currentValue = memberValue;
        }

        return (currentValue, null);
    }

    /// <summary>
    /// Synchronous version of expression resolution using field-only access.
    /// This is a backwards-compatible fallback for methods that don't need property getter support.
    /// </summary>
    private CorDebugValue? ResolveExpressionToValue(string expression, int? threadId, int frameIndex)
    {
        // Get the IL frame for the specified thread/frame
        var ilFrame = GetILFrame(threadId, frameIndex);
        if (ilFrame == null) return null;

        // Try to resolve as a local variable or argument
        var parts = expression.Split('.');
        CorDebugValue? currentValue = null;

        // Get the root value
        if (parts[0] == "this")
        {
            currentValue = TryGetThisForEval(ilFrame);
        }
        else
        {
            currentValue = TryGetLocalOrArgument(parts[0], ilFrame);
        }

        if (currentValue == null) return null;

        // Navigate the path if there are more parts (field-only access)
        for (int i = 1; i < parts.Length; i++)
        {
            var memberName = parts[i];

            // Try field first
            var fieldValue = TryGetFieldValue(currentValue, memberName);
            if (fieldValue == null)
            {
                // Try backing field for auto-properties
                var backingFieldName = $"<{memberName}>k__BackingField";
                fieldValue = TryGetFieldValue(currentValue, backingFieldName);
            }

            if (fieldValue == null) return null;
            currentValue = fieldValue;
        }

        return currentValue;
    }

    private ObjectInspection InspectObjectValue(CorDebugValue value, int depth, HashSet<ulong> visitedAddresses)
    {
        // Handle null reference
        if (value is CorDebugReferenceValue refValue)
        {
            if (refValue.IsNull)
            {
                var nullType = GetTypeName(value);
                return new ObjectInspection
                {
                    Address = "0x0",
                    TypeName = nullType,
                    Size = 0,
                    Fields = [],
                    IsNull = true
                };
            }

            // Check for circular references
            var address = refValue.Value;
            if (!visitedAddresses.Add(address))
            {
                var circularType = GetTypeName(value);
                return new ObjectInspection
                {
                    Address = $"0x{address:X16}",
                    TypeName = circularType,
                    Size = 0,
                    Fields = [],
                    IsNull = false,
                    HasCircularRef = true
                };
            }

            var derefValue = refValue.Dereference();
            if (derefValue != null)
            {
                var result = InspectObjectValueCore(derefValue, depth, visitedAddresses);
                result = result with { Address = $"0x{address:X16}" };
                return result;
            }
        }

        return InspectObjectValueCore(value, depth, visitedAddresses);
    }

    private ObjectInspection InspectObjectValueCore(CorDebugValue value, int depth, HashSet<ulong> visitedAddresses)
    {
        var typeName = GetTypeName(value);
        var fields = new List<FieldDetail>();
        int size = 0;
        bool truncated = false;

        try
        {
            size = (int)value.Size;
        }
        catch
        {
            // Size not available
        }

        // Handle string values specially
        if (value is CorDebugStringValue stringValue)
        {
            var len = stringValue.Length;
            var str = stringValue.GetString((int)len) ?? "";
            if (str.Length > 1000)
            {
                str = str.Substring(0, 1000) + "...";
            }

            return new ObjectInspection
            {
                Address = "0x0",
                TypeName = "System.String",
                Size = size,
                Fields =
                [
                    new FieldDetail
                    {
                        Name = "value",
                        TypeName = "System.String",
                        Value = $"\"{str}\"",
                        Offset = 0,
                        Size = str.Length * 2,
                        HasChildren = false
                    }
                ],
                IsNull = false
            };
        }

        // Handle array values
        if (value is CorDebugArrayValue arrayValue)
        {
            var count = (int)arrayValue.Count;
            var elementTypeName = GetArrayElementTypeName(arrayValue);
            var maxElements = Math.Min(count, 100);

            for (int i = 0; i < maxElements; i++)
            {
                try
                {
                    var element = arrayValue.GetElementAtPosition(i);
                    if (element != null)
                    {
                        var (elemValue, elemType, hasChildren, childCount) = FormatValue(element);
                        fields.Add(new FieldDetail
                        {
                            Name = $"[{i}]",
                            TypeName = elemType,
                            Value = elemValue,
                            Offset = i * 8, // Approximate
                            Size = 8, // Pointer size for references
                            HasChildren = hasChildren,
                            ChildCount = childCount
                        });
                    }
                }
                catch
                {
                    // Skip inaccessible elements
                }
            }

            if (count > 100)
            {
                truncated = true;
            }

            return new ObjectInspection
            {
                Address = "0x0",
                TypeName = $"{elementTypeName}[]",
                Size = size,
                Fields = fields,
                IsNull = false,
                Truncated = truncated
            };
        }

        // Handle object values
        if (value is CorDebugObjectValue objValue)
        {
            try
            {
                var objClass = objValue.Class;
                if (objClass != null)
                {
                    var module = objClass.Module;
                    var metaImport = module.GetMetaDataInterface<ClrDebug.MetaDataImport>();
                    var fieldTokens = metaImport.EnumFields((int)objClass.Token).ToList();

                    int offset = 0;
                    foreach (var fieldToken in fieldTokens)
                    {
                        if (fields.Count >= 100)
                        {
                            truncated = true;
                            break;
                        }

                        try
                        {
                            var fieldProps = metaImport.GetFieldProps(fieldToken);
                            var fieldAttrs = fieldProps.pdwAttr;

                            // Skip static fields unless explicitly requested
                            bool isStatic = (fieldAttrs & CorFieldAttr.fdStatic) != 0;
                            if (isStatic) continue;

                            var fieldValue = objValue.GetFieldValue(objClass.Raw, (int)fieldToken);
                            if (fieldValue != null)
                            {
                                var (formattedValue, fieldType, hasChildren, childCount) = FormatValue(fieldValue);
                                int fieldSize = 0;
                                try { fieldSize = (int)fieldValue.Size; } catch { }

                                fields.Add(new FieldDetail
                                {
                                    Name = fieldProps.szField ?? $"field_{fieldToken}",
                                    TypeName = fieldType,
                                    Value = formattedValue,
                                    Offset = offset,
                                    Size = fieldSize,
                                    HasChildren = hasChildren,
                                    ChildCount = childCount,
                                    IsStatic = isStatic
                                });

                                offset += fieldSize > 0 ? fieldSize : 8;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogDebug(ex, "Error getting field {FieldToken}", fieldToken);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error enumerating fields for object");
            }
        }

        // Handle boxed values
        if (value is CorDebugBoxValue boxValue)
        {
            var unboxed = boxValue.Object;
            if (unboxed != null)
            {
                return InspectObjectValueCore(unboxed, depth, visitedAddresses);
            }
        }

        // Handle primitive values
        if (value is CorDebugGenericValue genericValue)
        {
            var primitiveValue = GetPrimitiveValue(genericValue);
            fields.Add(new FieldDetail
            {
                Name = "value",
                TypeName = typeName,
                Value = primitiveValue,
                Offset = 0,
                Size = size,
                HasChildren = false
            });
        }

        return new ObjectInspection
        {
            Address = "0x0",
            TypeName = typeName,
            Size = size,
            Fields = fields,
            IsNull = false,
            Truncated = truncated
        };
    }

    private string GetArrayElementTypeName(CorDebugArrayValue arrayValue)
    {
        try
        {
            var exactType = arrayValue.ExactType;
            if (exactType != null)
            {
                var firstTypeParam = exactType.TypeParameters?.FirstOrDefault();
                if (firstTypeParam != null)
                {
                    var typeClass = firstTypeParam.Class;
                    if (typeClass != null)
                    {
                        var module = typeClass.Module;
                        var metaImport = module.GetMetaDataInterface<ClrDebug.MetaDataImport>();
                        var typeProps = metaImport.GetTypeDefProps((int)typeClass.Token);
                        return typeProps.szTypeDef ?? "Object";
                    }
                }
            }
        }
        catch
        {
            // Fallback
        }

        return "Object";
    }

    /// <inheritdoc />
    public Task<MemoryRegion> ReadMemoryAsync(
        string address,
        int size = 256,
        CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_process == null)
            {
                throw new InvalidOperationException("Cannot read memory: debugger is not attached to any process");
            }

            if (_currentState != SessionState.Paused)
            {
                throw new InvalidOperationException($"Cannot read memory: process is not paused (current state: {_currentState})");
            }

            // Validate size limit (64KB max)
            const int MaxSize = 65536;
            if (size > MaxSize)
            {
                throw new ArgumentException($"Requested size {size} exceeds maximum limit of {MaxSize} bytes");
            }

            if (size <= 0)
            {
                throw new ArgumentException("Size must be positive");
            }

            // Parse address (supports "0x..." hex or decimal)
            ulong addr;
            try
            {
                var trimmed = address.Trim();
                if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    addr = Convert.ToUInt64(trimmed[2..], 16);
                else
                    addr = Convert.ToUInt64(trimmed, 10);
            }
            catch (Exception ex) when (ex is FormatException or OverflowException)
            {
                throw new ArgumentException($"Invalid memory address format: '{address}'", ex);
            }

            _logger.LogDebug("Reading {Size} bytes from address {Address}", size, address);

            // Read memory using ICorDebugProcess
            var buffer = new byte[size];
            int bytesRead = 0;
            string? error = null;

            try
            {
                var rawProcess = _process.Raw;
                // ICorDebugProcess.ReadMemory signature: (address, size, buffer, out bytesRead)
                var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
                try
                {
                    rawProcess.ReadMemory(addr, size, handle.AddrOfPinnedObject(), out bytesRead);
                }
                finally
                {
                    handle.Free();
                }

                if (bytesRead < size)
                {
                    error = $"Partial read: {bytesRead} of {size} bytes";
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Memory read failed at address {Address}", address);
                throw new InvalidOperationException($"Failed to read memory at address {address}: {ex.Message}");
            }

            // Format as hex dump with 16 bytes per line
            var hexBuilder = new StringBuilder();
            var asciiBuilder = new StringBuilder();

            for (int i = 0; i < bytesRead; i++)
            {
                if (i > 0 && i % 16 == 0)
                {
                    hexBuilder.AppendLine();
                    asciiBuilder.AppendLine();
                }
                else if (i > 0)
                {
                    hexBuilder.Append(' ');
                }

                hexBuilder.Append(buffer[i].ToString("X2"));

                // ASCII representation
                char c = (char)buffer[i];
                asciiBuilder.Append(c >= 0x20 && c <= 0x7E ? c : '.');
            }

            return Task.FromResult(new MemoryRegion
            {
                Address = $"0x{addr:X16}",
                RequestedSize = size,
                ActualSize = bytesRead,
                Bytes = hexBuilder.ToString(),
                Ascii = asciiBuilder.ToString(),
                Error = error
            });
        }
    }

    /// <inheritdoc />
    public Task<ReferencesResult> GetOutboundReferencesAsync(
        string objectRef,
        bool includeArrays = true,
        int maxResults = 50,
        int? threadId = null,
        int frameIndex = 0,
        CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_process == null)
            {
                throw new InvalidOperationException("Cannot get references: debugger is not attached to any process");
            }

            if (_currentState != SessionState.Paused)
            {
                throw new InvalidOperationException($"Cannot get references: process is not paused (current state: {_currentState})");
            }

            // Clamp maxResults to 100
            maxResults = Math.Min(maxResults, 100);

            _logger.LogDebug("Getting outbound references for '{ObjectRef}' (max: {MaxResults})", objectRef, maxResults);

            // Resolve the expression to get the object value
            var value = ResolveExpressionToValue(objectRef, threadId, frameIndex);

            if (value == null)
            {
                throw new InvalidOperationException($"Invalid reference: could not resolve '{objectRef}'");
            }

            var references = new List<ReferenceInfo>();
            var targetAddress = "0x0";
            var targetType = GetTypeName(value);

            // Get the actual address if it's a reference
            if (value is CorDebugReferenceValue refValue)
            {
                if (refValue.IsNull)
                {
                    return Task.FromResult(new ReferencesResult
                    {
                        TargetAddress = "0x0",
                        TargetType = targetType,
                        Outbound = [],
                        OutboundCount = 0
                    });
                }

                targetAddress = $"0x{refValue.Value:X16}";
                var derefValue = refValue.Dereference();
                if (derefValue != null)
                {
                    EnumerateOutboundReferences(derefValue, targetAddress, targetType, references, includeArrays, maxResults);
                }
            }
            else
            {
                EnumerateOutboundReferences(value, targetAddress, targetType, references, includeArrays, maxResults);
            }

            var truncated = references.Count >= maxResults;
            var actualCount = references.Count;

            return Task.FromResult(new ReferencesResult
            {
                TargetAddress = targetAddress,
                TargetType = targetType,
                Outbound = references,
                OutboundCount = actualCount,
                Truncated = truncated
            });
        }
    }

    private void EnumerateOutboundReferences(
        CorDebugValue value,
        string sourceAddress,
        string sourceType,
        List<ReferenceInfo> references,
        bool includeArrays,
        int maxResults)
    {
        if (references.Count >= maxResults) return;

        // Handle array values
        if (value is CorDebugArrayValue arrayValue && includeArrays)
        {
            var count = (int)arrayValue.Count;
            var maxElements = Math.Min(count, maxResults - references.Count);

            for (int i = 0; i < maxElements && references.Count < maxResults; i++)
            {
                try
                {
                    var element = arrayValue.GetElementAtPosition(i);
                    if (element is CorDebugReferenceValue elemRef && !elemRef.IsNull)
                    {
                        var elemType = GetTypeName(element);
                        references.Add(new ReferenceInfo
                        {
                            SourceAddress = sourceAddress,
                            SourceType = sourceType,
                            TargetAddress = $"0x{elemRef.Value:X16}",
                            TargetType = elemType,
                            Path = $"[{i}]",
                            ReferenceType = Models.Memory.ReferenceType.ArrayElement
                        });
                    }
                }
                catch
                {
                    // Skip inaccessible elements
                }
            }

            return;
        }

        // Handle object values - enumerate reference fields
        if (value is CorDebugObjectValue objValue)
        {
            try
            {
                var objClass = objValue.Class;
                if (objClass != null)
                {
                    var module = objClass.Module;
                    var metaImport = module.GetMetaDataInterface<ClrDebug.MetaDataImport>();
                    var fieldTokens = metaImport.EnumFields((int)objClass.Token).ToList();

                    foreach (var fieldToken in fieldTokens)
                    {
                        if (references.Count >= maxResults) break;

                        try
                        {
                            var fieldProps = metaImport.GetFieldProps(fieldToken);
                            var fieldAttrs = fieldProps.pdwAttr;

                            // Skip static fields
                            bool isStatic = (fieldAttrs & CorFieldAttr.fdStatic) != 0;
                            if (isStatic) continue;

                            var fieldValue = objValue.GetFieldValue(objClass.Raw, (int)fieldToken);
                            if (fieldValue is CorDebugReferenceValue fieldRef && !fieldRef.IsNull)
                            {
                                var fieldType = GetTypeName(fieldValue);
                                references.Add(new ReferenceInfo
                                {
                                    SourceAddress = sourceAddress,
                                    SourceType = sourceType,
                                    TargetAddress = $"0x{fieldRef.Value:X16}",
                                    TargetType = fieldType,
                                    Path = fieldProps.szField ?? $"field_{fieldToken}",
                                    ReferenceType = Models.Memory.ReferenceType.Field
                                });
                            }
                        }
                        catch
                        {
                            // Skip inaccessible fields
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error enumerating references for object");
            }
        }
    }

    /// <inheritdoc />
    public Task<Models.Memory.TypeLayout> GetTypeLayoutAsync(
        string typeName,
        bool includeInherited = true,
        bool includePadding = true,
        int? threadId = null,
        int frameIndex = 0,
        CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_process == null)
            {
                throw new InvalidOperationException("Cannot get type layout: debugger is not attached to any process");
            }

            if (_currentState != SessionState.Paused)
            {
                throw new InvalidOperationException($"Cannot get type layout: process is not paused (current state: {_currentState})");
            }

            _logger.LogDebug("Getting layout for type '{TypeName}'", typeName);

            // Try to resolve the type name as an expression first (could be a variable)
            var value = ResolveExpressionToValue(typeName, threadId, frameIndex);

            CorDebugType? debugType = null;
            string resolvedTypeName = typeName;

            if (value != null)
            {
                // Get type from value
                debugType = value.ExactType;
                resolvedTypeName = GetTypeName(value);
            }
            else
            {
                // Search for type by name in loaded modules
                debugType = FindTypeByName(typeName);
                if (debugType != null)
                {
                    resolvedTypeName = typeName;
                }
            }

            if (debugType == null)
            {
                throw new InvalidOperationException($"Type '{typeName}' not found in loaded modules");
            }

            return Task.FromResult(GetTypeLayoutFromDebugType(debugType, resolvedTypeName, includeInherited, includePadding));
        }
    }

    private CorDebugType? FindTypeByName(string typeName)
    {
        // Search through all loaded modules for the type
        lock (_lock)
        {
            if (_process == null) return null;

            try
            {
                foreach (var appDomain in _process.AppDomains)
                {
                    foreach (var assembly in appDomain.Assemblies)
                    {
                        foreach (var module in assembly.Modules)
                        {
                            try
                            {
                                var metaImport = module.GetMetaDataInterface<ClrDebug.MetaDataImport>();

                                // Try to find the type definition
                                try
                                {
                                    var typeDefResult = metaImport.FindTypeDefByName(typeName, 0);
                                    if (typeDefResult != 0)
                                    {
                                        var debugClass = module.GetClassFromToken(typeDefResult);
                                        if (debugClass != null)
                                        {
                                            // Create a type from the class
                                            return debugClass.GetParameterizedType(CorElementType.Class, 0, null);
                                        }
                                    }
                                }
                                catch
                                {
                                    // Type not found in this module
                                }
                            }
                            catch
                            {
                                // Can't get metadata from this module
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error searching for type '{TypeName}'", typeName);
            }
        }

        return null;
    }

    private Models.Memory.TypeLayout GetTypeLayoutFromDebugType(CorDebugType debugType, string typeName, bool includeInherited, bool includePadding)
    {
        var fields = new List<LayoutField>();
        var padding = new List<PaddingRegion>();
        int totalSize = 0;
        int headerSize = 0;
        bool isValueType = false;
        string? baseType = null;

        try
        {
            var typeClass = debugType.Class;
            if (typeClass != null)
            {
                var module = typeClass.Module;
                var metaImport = module.GetMetaDataInterface<ClrDebug.MetaDataImport>();
                var typeProps = metaImport.GetTypeDefProps((int)typeClass.Token);

                // Check if value type
                isValueType = (typeProps.pdwTypeDefFlags & CorTypeAttr.tdSealed) != 0 &&
                              typeProps.szTypeDef?.StartsWith("System.ValueType") == false &&
                              !typeName.Contains("class", StringComparison.OrdinalIgnoreCase);

                // Get base type
                if (typeProps.ptkExtends != 0)
                {
                    try
                    {
                        var baseTypeProps = metaImport.GetTypeDefProps((int)typeProps.ptkExtends);
                        baseType = baseTypeProps.szTypeDef;
                    }
                    catch
                    {
                        // Base type info not available
                    }
                }

                // Header size: 16 bytes for reference types on 64-bit, 0 for value types
                headerSize = isValueType ? 0 : 16;

                // Enumerate fields
                var fieldTokens = metaImport.EnumFields((int)typeClass.Token).ToList();
                int currentOffset = 0;

                foreach (var fieldToken in fieldTokens)
                {
                    try
                    {
                        var fieldProps = metaImport.GetFieldProps(fieldToken);
                        var fieldAttrs = fieldProps.pdwAttr;

                        // Skip static fields
                        if ((fieldAttrs & CorFieldAttr.fdStatic) != 0) continue;

                        // Get field type info
                        var fieldTypeName = GetFieldTypeName(metaImport, fieldProps.ppvSigBlob);
                        var fieldSize = GetFieldSize(fieldTypeName);
                        var alignment = GetFieldAlignment(fieldTypeName);
                        bool isReference = IsReferenceType(fieldTypeName);

                        // Calculate offset with alignment
                        if (currentOffset % alignment != 0)
                        {
                            var paddingNeeded = alignment - (currentOffset % alignment);

                            if (includePadding && paddingNeeded > 0)
                            {
                                padding.Add(new PaddingRegion
                                {
                                    Offset = currentOffset,
                                    Size = paddingNeeded,
                                    Reason = $"Alignment for {fieldTypeName} ({alignment}-byte aligned)"
                                });
                            }

                            currentOffset += paddingNeeded;
                        }

                        fields.Add(new LayoutField
                        {
                            Name = fieldProps.szField ?? $"field_{fieldToken}",
                            TypeName = fieldTypeName,
                            Offset = currentOffset,
                            Size = fieldSize,
                            Alignment = alignment,
                            IsReference = isReference
                        });

                        currentOffset += fieldSize;
                    }
                    catch
                    {
                        // Skip problematic fields
                    }
                }

                totalSize = headerSize + currentOffset;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error getting type layout for '{TypeName}'", typeName);
        }

        return new Models.Memory.TypeLayout
        {
            TypeName = typeName,
            TotalSize = totalSize,
            HeaderSize = headerSize,
            DataSize = totalSize - headerSize,
            Fields = fields,
            Padding = padding,
            IsValueType = isValueType,
            BaseType = baseType
        };
    }

    private string GetFieldTypeName(ClrDebug.MetaDataImport metaImport, IntPtr sigBlob)
    {
        // Simplified field type name extraction
        // In a full implementation, you'd parse the signature blob
        return "Unknown";
    }

    private int GetFieldSize(string typeName)
    {
        return typeName switch
        {
            "System.Boolean" or "System.Byte" or "System.SByte" => 1,
            "System.Char" or "System.Int16" or "System.UInt16" => 2,
            "System.Int32" or "System.UInt32" or "System.Single" => 4,
            "System.Int64" or "System.UInt64" or "System.Double" => 8,
            "System.Decimal" => 16,
            "System.IntPtr" or "System.UIntPtr" => 8, // 64-bit
            _ => 8 // Reference types are pointer-sized
        };
    }

    private int GetFieldAlignment(string typeName)
    {
        return typeName switch
        {
            "System.Boolean" or "System.Byte" or "System.SByte" => 1,
            "System.Char" or "System.Int16" or "System.UInt16" => 2,
            "System.Int32" or "System.UInt32" or "System.Single" => 4,
            _ => 8 // 8-byte alignment for most other types on 64-bit
        };
    }

    private bool IsReferenceType(string typeName)
    {
        return typeName switch
        {
            "System.Boolean" or "System.Byte" or "System.SByte" => false,
            "System.Char" or "System.Int16" or "System.UInt16" => false,
            "System.Int32" or "System.UInt32" or "System.Single" => false,
            "System.Int64" or "System.UInt64" or "System.Double" => false,
            "System.Decimal" => false,
            "System.IntPtr" or "System.UIntPtr" => false,
            _ => true
        };
    }

    #endregion

    #region Module Inspection Operations

    /// <inheritdoc />
    public Task<IReadOnlyList<Models.Modules.ModuleInfo>> GetModulesAsync(
        bool includeSystem = true,
        string? nameFilter = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            lock (_lock)
            {
                if (_process == null)
                {
                    throw new InvalidOperationException("Cannot get modules: debugger is not attached to any process");
                }

                _logger.LogDebug("Getting modules (includeSystem={IncludeSystem}, nameFilter={NameFilter})",
                    includeSystem, nameFilter);

                // ICorDebug requires the process to be stopped for reliable
                // enumeration of AppDomains/Assemblies/Modules.
                bool wasRunning = _currentState == SessionState.Running;
                if (wasRunning)
                    _process.Stop(0);

                var modules = new List<Models.Modules.ModuleInfo>();
                var moduleIdCounter = 0;

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
                                try
                                {
                                    var moduleInfo = ExtractModuleInfo(module, ref moduleIdCounter);

                                    // Apply system filter
                                    if (!includeSystem && IsSystemModule(moduleInfo.Name))
                                    {
                                        continue;
                                    }

                                    // Apply name filter (supports wildcards)
                                    if (!string.IsNullOrEmpty(nameFilter) && !MatchesWildcardPattern(moduleInfo.Name, nameFilter))
                                    {
                                        continue;
                                    }

                                    modules.Add(moduleInfo);
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogWarning(ex, "Error extracting info for module");
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error enumerating modules");
                    throw new InvalidOperationException($"Failed to enumerate modules: {ex.Message}", ex);
                }
                finally
                {
                    if (wasRunning)
                        _process.Continue(false);
                }

                _logger.LogInformation("Retrieved {Count} modules", modules.Count);
                return (IReadOnlyList<Models.Modules.ModuleInfo>)modules;
            }
        }, cancellationToken);
    }

    /// <summary>
    /// Extracts module information from an ICorDebugModule.
    /// </summary>
    private Models.Modules.ModuleInfo ExtractModuleInfo(CorDebugModule module, ref int moduleIdCounter)
    {
        var modulePath = module.Name ?? string.Empty;
        var moduleName = ExtractModuleName(modulePath);
        var isDynamic = module.IsDynamic;
        var isInMemory = module.IsInMemory;

        // Get base address and size
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

        // Try to get version from assembly metadata
        var version = ExtractModuleVersion(module, modulePath);

        // Check for symbols
        var hasSymbols = CheckHasSymbols(modulePath);

        var moduleId = $"mod-{++moduleIdCounter}";

        return new Models.Modules.ModuleInfo(
            Name: moduleName,
            FullName: modulePath,
            Path: isInMemory ? null : modulePath,
            Version: version,
            IsManaged: true, // ICorDebug only enumerates managed modules
            IsDynamic: isDynamic,
            HasSymbols: hasSymbols,
            ModuleId: moduleId,
            BaseAddress: baseAddress > 0 ? $"0x{baseAddress:X16}" : null,
            Size: (int)size
        );
    }

    /// <summary>
    /// Extracts the assembly name from a module path.
    /// </summary>
    private static string ExtractModuleName(string modulePath)
    {
        if (string.IsNullOrEmpty(modulePath))
        {
            return "<unknown>";
        }

        // For file paths, extract the file name without extension
        if (modulePath.Contains(Path.DirectorySeparatorChar) || modulePath.Contains(Path.AltDirectorySeparatorChar))
        {
            var fileName = Path.GetFileNameWithoutExtension(modulePath);
            return string.IsNullOrEmpty(fileName) ? modulePath : fileName;
        }

        // For in-memory modules, the name might already be just the assembly name
        // Remove .dll extension if present
        if (modulePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            return modulePath[..^4];
        }
        if (modulePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return modulePath[..^4];
        }

        return modulePath;
    }

    /// <summary>
    /// Extracts the version from module metadata.
    /// </summary>
    private string ExtractModuleVersion(CorDebugModule module, string modulePath)
    {
        try
        {
            // Try to get version from file if it exists
            if (!string.IsNullOrEmpty(modulePath) && File.Exists(modulePath))
            {
                var fileVersionInfo = FileVersionInfo.GetVersionInfo(modulePath);
                if (!string.IsNullOrEmpty(fileVersionInfo.FileVersion))
                {
                    return fileVersionInfo.FileVersion;
                }

                // Fall back to product version
                if (!string.IsNullOrEmpty(fileVersionInfo.ProductVersion))
                {
                    return fileVersionInfo.ProductVersion;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not extract version from module {ModulePath}", modulePath);
        }

        return "0.0.0.0";
    }

    /// <summary>
    /// Checks if the module has symbols (PDB) available.
    /// </summary>
    private static bool CheckHasSymbols(string modulePath)
    {
        if (string.IsNullOrEmpty(modulePath) || !File.Exists(modulePath))
        {
            return false;
        }

        try
        {
            // Check for PDB file in same directory
            var directory = Path.GetDirectoryName(modulePath);
            var baseName = Path.GetFileNameWithoutExtension(modulePath);

            if (directory == null)
            {
                return false;
            }

            var pdbPath = Path.Combine(directory, baseName + ".pdb");
            return File.Exists(pdbPath);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Determines if a module is a system module.
    /// </summary>
    private static bool IsSystemModule(string moduleName)
    {
        // Common system module prefixes
        if (moduleName.StartsWith("System.", StringComparison.OrdinalIgnoreCase) ||
            moduleName.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase) ||
            moduleName.StartsWith("netstandard", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Well-known system modules
        return moduleName switch
        {
            "System" => true,
            "mscorlib" => true,
            "System.Private.CoreLib" => true,
            "WindowsBase" => true,
            "PresentationCore" => true,
            "PresentationFramework" => true,
            _ => false
        };
    }

    /// <summary>
    /// Matches a string against a wildcard pattern (supports * wildcard).
    /// </summary>
    private static bool MatchesWildcardPattern(string input, string pattern)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            return true;
        }

        // Simple wildcard matching
        if (pattern == "*")
        {
            return true;
        }

        // Handle prefix wildcard: *suffix
        if (pattern.StartsWith('*') && !pattern.EndsWith('*'))
        {
            var suffix = pattern[1..];
            return input.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
        }

        // Handle suffix wildcard: prefix*
        if (pattern.EndsWith('*') && !pattern.StartsWith('*'))
        {
            var prefix = pattern[..^1];
            return input.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        // Handle contains wildcard: *middle*
        if (pattern.StartsWith('*') && pattern.EndsWith('*'))
        {
            var middle = pattern[1..^1];
            return input.Contains(middle, StringComparison.OrdinalIgnoreCase);
        }

        // No wildcard - exact match (case-insensitive)
        return input.Equals(pattern, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks if a string matches a wildcard pattern with configurable case sensitivity.
    /// </summary>
    private static bool MatchesWildcardPattern(string input, string pattern, bool caseSensitive)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            return true;
        }

        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        // Simple wildcard matching
        if (pattern == "*")
        {
            return true;
        }

        // Handle prefix wildcard: *suffix
        if (pattern.StartsWith('*') && !pattern.EndsWith('*'))
        {
            var suffix = pattern[1..];
            return input.EndsWith(suffix, comparison);
        }

        // Handle suffix wildcard: prefix*
        if (pattern.EndsWith('*') && !pattern.StartsWith('*'))
        {
            var prefix = pattern[..^1];
            return input.StartsWith(prefix, comparison);
        }

        // Handle contains wildcard: *middle*
        if (pattern.StartsWith('*') && pattern.EndsWith('*'))
        {
            var middle = pattern[1..^1];
            return input.Contains(middle, comparison);
        }

        // No wildcard - exact match
        return input.Equals(pattern, comparison);
    }

    /// <inheritdoc />
    public Task<Models.Modules.TypesResult> GetTypesAsync(
        string moduleName,
        string? namespaceFilter = null,
        Models.Modules.TypeKind? kind = null,
        Models.Modules.Visibility? visibility = null,
        int maxResults = 100,
        string? continuationToken = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            lock (_lock)
            {
                if (_process == null)
                {
                    throw new InvalidOperationException("Cannot get types: debugger is not attached to any process");
                }

                _logger.LogDebug("Getting types from module {ModuleName} (namespaceFilter={NamespaceFilter}, kind={Kind}, visibility={Visibility})",
                    moduleName, namespaceFilter, kind, visibility);

                // Find the module
                var moduleInfo = FindModuleByName(moduleName);
                if (moduleInfo == null)
                {
                    throw new InvalidOperationException($"Module '{moduleName}' not found in loaded modules");
                }

                // Read types from PE metadata
                var (types, namespaces, totalCount) = ReadTypesFromModule(
                    moduleInfo.FullName,
                    moduleName,
                    namespaceFilter,
                    kind,
                    visibility,
                    maxResults,
                    continuationToken);

                var truncated = types.Count < totalCount;
                string? nextToken = truncated ? $"offset:{types.Count}" : null;

                _logger.LogInformation("Retrieved {Count}/{Total} types from module {ModuleName}",
                    types.Count, totalCount, moduleName);

                return new Models.Modules.TypesResult(
                    ModuleName: moduleName,
                    NamespaceFilter: namespaceFilter,
                    Types: types.ToArray(),
                    Namespaces: namespaces.ToArray(),
                    TotalCount: totalCount,
                    ReturnedCount: types.Count,
                    Truncated: truncated,
                    ContinuationToken: nextToken
                );
            }
        }, cancellationToken);
    }

    /// <summary>
    /// Finds a module by name (case-insensitive).
    /// </summary>
    private Models.Modules.ModuleInfo? FindModuleByName(string moduleName)
    {
        var moduleIdCounter = 0;
        foreach (var appDomain in _process!.AppDomains)
        {
            foreach (var assembly in appDomain.Assemblies)
            {
                foreach (var module in assembly.Modules)
                {
                    var info = ExtractModuleInfo(module, ref moduleIdCounter);
                    if (info.Name.Equals(moduleName, StringComparison.OrdinalIgnoreCase) ||
                        info.FullName.Equals(moduleName, StringComparison.OrdinalIgnoreCase))
                    {
                        return info;
                    }
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Reads types from a module using System.Reflection.Metadata.
    /// </summary>
    private (List<Models.Modules.TypeInfo> Types, List<Models.Modules.NamespaceNode> Namespaces, int TotalCount)
        ReadTypesFromModule(
            string modulePath,
            string moduleName,
            string? namespaceFilter,
            Models.Modules.TypeKind? kindFilter,
            Models.Modules.Visibility? visibilityFilter,
            int maxResults,
            string? continuationToken)
    {
        var types = new List<Models.Modules.TypeInfo>();
        var namespaceMap = new Dictionary<string, (int TypeCount, HashSet<string> Children)>(StringComparer.OrdinalIgnoreCase);
        var totalCount = 0;

        // Parse continuation token for offset
        var offset = 0;
        if (!string.IsNullOrEmpty(continuationToken) && continuationToken.StartsWith("offset:"))
        {
            int.TryParse(continuationToken.AsSpan(7), out offset);
        }

        // Check if file exists
        if (string.IsNullOrEmpty(modulePath) || !File.Exists(modulePath))
        {
            _logger.LogWarning("Module file not found: {ModulePath}", modulePath);
            return (types, new List<Models.Modules.NamespaceNode>(), 0);
        }

        try
        {
            using var peStream = File.OpenRead(modulePath);
            using var peReader = new PEReader(peStream);

            if (!peReader.HasMetadata)
            {
                _logger.LogWarning("Module has no metadata: {ModulePath}", modulePath);
                return (types, new List<Models.Modules.NamespaceNode>(), 0);
            }

            var metadataReader = peReader.GetMetadataReader();
            var currentIndex = 0;

            foreach (var typeDefHandle in metadataReader.TypeDefinitions)
            {
                var typeDef = metadataReader.GetTypeDefinition(typeDefHandle);

                // Skip nested types (they're handled by their parent)
                if (typeDef.IsNested) continue;

                // Skip <Module> type
                var typeName = metadataReader.GetString(typeDef.Name);
                if (typeName == "<Module>") continue;

                var namespaceName = metadataReader.GetString(typeDef.Namespace);

                // Apply namespace filter
                if (!string.IsNullOrEmpty(namespaceFilter) &&
                    !MatchesWildcardPattern(namespaceName, namespaceFilter))
                {
                    continue;
                }

                // Extract type info
                var typeKind = GetTypeKind(typeDef, metadataReader);
                var typeVisibility = GetTypeVisibility(typeDef.Attributes);

                // Apply kind filter
                if (kindFilter.HasValue && typeKind != kindFilter.Value)
                {
                    continue;
                }

                // Apply visibility filter
                if (visibilityFilter.HasValue && typeVisibility != visibilityFilter.Value)
                {
                    continue;
                }

                // Track namespace
                TrackNamespace(namespaceMap, namespaceName);

                totalCount++;

                // Handle pagination
                if (currentIndex < offset)
                {
                    currentIndex++;
                    continue;
                }

                if (types.Count >= maxResults)
                {
                    continue; // Continue counting but don't add more
                }

                // Build type info
                var typeInfo = BuildTypeInfo(typeDef, metadataReader, moduleName);
                types.Add(typeInfo);
                currentIndex++;
            }
        }
        catch (BadImageFormatException ex)
        {
            _logger.LogWarning(ex, "Invalid PE format for module: {ModulePath}", modulePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error reading metadata from module: {ModulePath}", modulePath);
        }

        // Build namespace hierarchy
        var namespaces = BuildNamespaceHierarchy(namespaceMap);

        return (types, namespaces, totalCount);
    }

    /// <summary>
    /// Determines the TypeKind from TypeDefinition.
    /// </summary>
    private static Models.Modules.TypeKind GetTypeKind(TypeDefinition typeDef, MetadataReader reader)
    {
        var attributes = typeDef.Attributes;

        // Check for enum
        var baseTypeHandle = typeDef.BaseType;
        if (!baseTypeHandle.IsNil)
        {
            var baseTypeName = GetTypeReferenceName(baseTypeHandle, reader);
            if (baseTypeName == "System.Enum")
                return Models.Modules.TypeKind.Enum;
            if (baseTypeName == "System.ValueType")
                return Models.Modules.TypeKind.Struct;
            if (baseTypeName == "System.MulticastDelegate" || baseTypeName == "System.Delegate")
                return Models.Modules.TypeKind.Delegate;
        }

        // Check for interface
        if ((attributes & TypeAttributes.Interface) != 0)
            return Models.Modules.TypeKind.Interface;

        // Check for struct (value type without Enum base)
        if ((attributes & TypeAttributes.Sealed) != 0 &&
            (attributes & TypeAttributes.SequentialLayout) != 0)
            return Models.Modules.TypeKind.Struct;

        return Models.Modules.TypeKind.Class;
    }

    /// <summary>
    /// Gets the name of a type reference.
    /// </summary>
    private static string GetTypeReferenceName(EntityHandle handle, MetadataReader reader)
    {
        if (handle.IsNil) return string.Empty;

        if (handle.Kind == HandleKind.TypeReference)
        {
            var typeRef = reader.GetTypeReference((TypeReferenceHandle)handle);
            var ns = reader.GetString(typeRef.Namespace);
            var name = reader.GetString(typeRef.Name);
            return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
        }

        if (handle.Kind == HandleKind.TypeDefinition)
        {
            var typeDef = reader.GetTypeDefinition((TypeDefinitionHandle)handle);
            var ns = reader.GetString(typeDef.Namespace);
            var name = reader.GetString(typeDef.Name);
            return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
        }

        return string.Empty;
    }

    /// <summary>
    /// Maps TypeAttributes to Visibility enum.
    /// </summary>
    private static Models.Modules.Visibility GetTypeVisibility(TypeAttributes attributes)
    {
        var visibilityMask = attributes & TypeAttributes.VisibilityMask;
        return visibilityMask switch
        {
            TypeAttributes.Public => Models.Modules.Visibility.Public,
            TypeAttributes.NotPublic => Models.Modules.Visibility.Internal,
            TypeAttributes.NestedPublic => Models.Modules.Visibility.Public,
            TypeAttributes.NestedPrivate => Models.Modules.Visibility.Private,
            TypeAttributes.NestedFamily => Models.Modules.Visibility.Protected,
            TypeAttributes.NestedAssembly => Models.Modules.Visibility.Internal,
            TypeAttributes.NestedFamORAssem => Models.Modules.Visibility.ProtectedInternal,
            TypeAttributes.NestedFamANDAssem => Models.Modules.Visibility.PrivateProtected,
            _ => Models.Modules.Visibility.Internal
        };
    }

    /// <summary>
    /// Builds a TypeInfo from a TypeDefinition.
    /// </summary>
    private Models.Modules.TypeInfo BuildTypeInfo(TypeDefinition typeDef, MetadataReader reader, string moduleName)
    {
        var typeName = reader.GetString(typeDef.Name);
        var namespaceName = reader.GetString(typeDef.Namespace);
        var fullName = string.IsNullOrEmpty(namespaceName) ? typeName : $"{namespaceName}.{typeName}";

        // Handle generic parameters
        var genericParams = new List<string>();
        var isGeneric = false;
        foreach (var paramHandle in typeDef.GetGenericParameters())
        {
            isGeneric = true;
            var param = reader.GetGenericParameter(paramHandle);
            genericParams.Add(reader.GetString(param.Name));
        }

        // Clean up generic type name (remove `1, `2, etc.)
        if (isGeneric && typeName.Contains('`'))
        {
            var tickIndex = typeName.IndexOf('`');
            typeName = typeName[..tickIndex];
            fullName = string.IsNullOrEmpty(namespaceName) ? typeName : $"{namespaceName}.{typeName}";
        }

        // Get base type
        string? baseTypeName = null;
        if (!typeDef.BaseType.IsNil)
        {
            baseTypeName = GetTypeReferenceName(typeDef.BaseType, reader);
            if (baseTypeName == "System.Object")
            {
                baseTypeName = null; // Don't show Object as base
            }
        }

        // Get interfaces
        var interfaces = new List<string>();
        foreach (var interfaceImpl in typeDef.GetInterfaceImplementations())
        {
            var impl = reader.GetInterfaceImplementation(interfaceImpl);
            var interfaceName = GetTypeReferenceName(impl.Interface, reader);
            if (!string.IsNullOrEmpty(interfaceName))
            {
                interfaces.Add(interfaceName);
            }
        }

        return new Models.Modules.TypeInfo(
            FullName: fullName,
            Name: typeName,
            Namespace: namespaceName,
            Kind: GetTypeKind(typeDef, reader),
            Visibility: GetTypeVisibility(typeDef.Attributes),
            IsGeneric: isGeneric,
            GenericParameters: genericParams.ToArray(),
            IsNested: typeDef.IsNested,
            DeclaringType: null, // Nested types are already filtered out
            ModuleName: moduleName,
            BaseType: baseTypeName,
            Interfaces: interfaces.ToArray()
        );
    }

    /// <summary>
    /// Tracks a namespace in the namespace map.
    /// </summary>
    private static void TrackNamespace(Dictionary<string, (int TypeCount, HashSet<string> Children)> map, string namespaceName)
    {
        if (string.IsNullOrEmpty(namespaceName))
        {
            namespaceName = "(global)";
        }

        // Increment count for this namespace
        if (map.TryGetValue(namespaceName, out var entry))
        {
            map[namespaceName] = (entry.TypeCount + 1, entry.Children);
        }
        else
        {
            map[namespaceName] = (1, new HashSet<string>());
        }

        // Track parent namespaces
        var parts = namespaceName.Split('.');
        for (var i = 1; i < parts.Length; i++)
        {
            var parentNs = string.Join('.', parts.Take(i));
            var childNs = string.Join('.', parts.Take(i + 1));

            if (!map.TryGetValue(parentNs, out var parentEntry))
            {
                parentEntry = (0, new HashSet<string>());
            }
            parentEntry.Children.Add(childNs);
            map[parentNs] = parentEntry;
        }
    }

    /// <summary>
    /// Builds namespace hierarchy from the namespace map.
    /// </summary>
    private static List<Models.Modules.NamespaceNode> BuildNamespaceHierarchy(
        Dictionary<string, (int TypeCount, HashSet<string> Children)> map)
    {
        var nodes = new List<Models.Modules.NamespaceNode>();

        foreach (var (fullName, (typeCount, children)) in map)
        {
            var depth = fullName == "(global)" ? 0 : fullName.Count(c => c == '.') + 1;
            var name = fullName == "(global)" ? "(global)" : fullName.Split('.').Last();

            nodes.Add(new Models.Modules.NamespaceNode(
                Name: name,
                FullName: fullName,
                TypeCount: typeCount,
                ChildNamespaces: children.ToArray(),
                Depth: depth
            ));
        }

        // Sort by full name
        nodes.Sort((a, b) => string.Compare(a.FullName, b.FullName, StringComparison.OrdinalIgnoreCase));

        return nodes;
    }

    /// <inheritdoc />
    public async Task<Models.Modules.TypeMembersResult> GetMembersAsync(
        string typeName,
        string? moduleName = null,
        bool includeInherited = false,
        string[]? memberKinds = null,
        Models.Modules.Visibility? visibility = null,
        bool includeStatic = true,
        bool includeInstance = true,
        CancellationToken cancellationToken = default)
    {
        if (_process == null)
        {
            throw new InvalidOperationException("Cannot get members: debugger is not attached to any process");
        }

        // Determine which member kinds to include
        var includeMethods = memberKinds == null || memberKinds.Contains("methods", StringComparer.OrdinalIgnoreCase);
        var includeProperties = memberKinds == null || memberKinds.Contains("properties", StringComparer.OrdinalIgnoreCase);
        var includeFields = memberKinds == null || memberKinds.Contains("fields", StringComparer.OrdinalIgnoreCase);
        var includeEvents = memberKinds == null || memberKinds.Contains("events", StringComparer.OrdinalIgnoreCase);

        // Find the type in modules
        var (moduleInfo, typeDefHandle, metadataReader, peReader) = await Task.Run(() =>
            FindTypeDefinition(typeName, moduleName), cancellationToken);

        if (moduleInfo == null || metadataReader == null || peReader == null)
        {
            throw new InvalidOperationException($"Type '{typeName}' not found" +
                (moduleName != null ? $" in module '{moduleName}'" : " in any loaded module"));
        }

        try
        {
            var typeDef = metadataReader.GetTypeDefinition(typeDefHandle);
            var methods = new List<Models.Modules.MethodMemberInfo>();
            var properties = new List<Models.Modules.PropertyMemberInfo>();
            var fields = new List<Models.Modules.FieldMemberInfo>();
            var events = new List<Models.Modules.EventMemberInfo>();

            // Read members from the type
            if (includeMethods)
            {
                methods.AddRange(ReadMethods(typeDef, metadataReader, typeName, visibility, includeStatic, includeInstance));
            }

            if (includeProperties)
            {
                properties.AddRange(ReadProperties(typeDef, metadataReader, typeName, visibility, includeStatic, includeInstance));
            }

            if (includeFields)
            {
                fields.AddRange(ReadFields(typeDef, metadataReader, typeName, visibility, includeStatic, includeInstance));
            }

            if (includeEvents)
            {
                events.AddRange(ReadEvents(typeDef, metadataReader, typeName, visibility, includeStatic, includeInstance));
            }

            // Handle inherited members if requested
            if (includeInherited)
            {
                var baseTypeHandle = typeDef.BaseType;
                if (!baseTypeHandle.IsNil)
                {
                    var inheritedMembers = await ReadInheritedMembersAsync(
                        baseTypeHandle, metadataReader, peReader, moduleInfo.Path!,
                        memberKinds, visibility, includeStatic, includeInstance, cancellationToken);

                    if (includeMethods) methods.AddRange(inheritedMembers.Methods);
                    if (includeProperties) properties.AddRange(inheritedMembers.Properties);
                    if (includeFields) fields.AddRange(inheritedMembers.Fields);
                    if (includeEvents) events.AddRange(inheritedMembers.Events);
                }
            }

            return new Models.Modules.TypeMembersResult(
                TypeName: typeName,
                Methods: methods.ToArray(),
                Properties: properties.ToArray(),
                Fields: fields.ToArray(),
                Events: events.ToArray(),
                IncludesInherited: includeInherited,
                MethodCount: methods.Count,
                PropertyCount: properties.Count,
                FieldCount: fields.Count,
                EventCount: events.Count);
        }
        finally
        {
            peReader.Dispose();
        }
    }

    /// <summary>
    /// Finds a type definition by name in loaded modules.
    /// </summary>
    private (Models.Modules.ModuleInfo? Module, TypeDefinitionHandle TypeHandle, MetadataReader? Reader, PEReader? PeReader)
        FindTypeDefinition(string typeName, string? moduleName)
    {
        // If module is specified, search only that module
        if (moduleName != null)
        {
            var moduleInfo = FindModuleByName(moduleName);
            if (moduleInfo == null)
            {
                throw new InvalidOperationException($"Module '{moduleName}' not found in loaded modules");
            }

            var (handle, reader, peReader) = FindTypeInModule(moduleInfo.Path!, typeName);
            return (moduleInfo, handle, reader, peReader);
        }

        // Search all modules
        var moduleIdCounter = 0;
        foreach (var appDomain in _process!.AppDomains)
        {
            foreach (var assembly in appDomain.Assemblies)
            {
                foreach (var module in assembly.Modules)
                {
                    var info = ExtractModuleInfo(module, ref moduleIdCounter);
                    if (string.IsNullOrEmpty(info.Path) || !File.Exists(info.Path))
                        continue;

                    var (handle, reader, peReader) = FindTypeInModule(info.Path, typeName);
                    if (!handle.IsNil && reader != null && peReader != null)
                    {
                        return (info, handle, reader, peReader);
                    }
                }
            }
        }

        return (null, default, null, null);
    }

    /// <summary>
    /// Finds a type in a specific module by name.
    /// </summary>
    private (TypeDefinitionHandle Handle, MetadataReader? Reader, PEReader? PeReader) FindTypeInModule(string modulePath, string typeName)
    {
        try
        {
            var peReader = new PEReader(File.OpenRead(modulePath));
            if (!peReader.HasMetadata)
            {
                peReader.Dispose();
                return (default, null, null);
            }

            var reader = peReader.GetMetadataReader();

            // Parse the type name to get namespace and name
            var lastDot = typeName.LastIndexOf('.');
            var targetNamespace = lastDot > 0 ? typeName.Substring(0, lastDot) : "";
            var targetName = lastDot > 0 ? typeName.Substring(lastDot + 1) : typeName;

            // Handle nested types (Outer+Inner)
            var plusIndex = targetName.IndexOf('+');
            if (plusIndex > 0)
            {
                // For nested types, find parent first
                var outerTypeName = lastDot > 0 ? targetNamespace + "." + targetName.Substring(0, plusIndex) : targetName.Substring(0, plusIndex);
                var (outerHandle, _, _) = FindTypeInModule(modulePath, outerTypeName);
                if (outerHandle.IsNil)
                {
                    peReader.Dispose();
                    return (default, null, null);
                }

                // Use a fresh reader since we dispose the old one
                peReader = new PEReader(File.OpenRead(modulePath));
                reader = peReader.GetMetadataReader();

                var outerType = reader.GetTypeDefinition(outerHandle);
                var nestedName = targetName.Substring(plusIndex + 1);

                foreach (var nestedHandle in outerType.GetNestedTypes())
                {
                    var nestedDef = reader.GetTypeDefinition(nestedHandle);
                    var name = reader.GetString(nestedDef.Name);
                    if (name == nestedName)
                    {
                        return (nestedHandle, reader, peReader);
                    }
                }

                peReader.Dispose();
                return (default, null, null);
            }

            // Search for top-level type
            foreach (var typeHandle in reader.TypeDefinitions)
            {
                var typeDef = reader.GetTypeDefinition(typeHandle);
                var ns = reader.GetString(typeDef.Namespace);
                var name = reader.GetString(typeDef.Name);

                if (ns == targetNamespace && name == targetName)
                {
                    return (typeHandle, reader, peReader);
                }
            }

            peReader.Dispose();
            return (default, null, null);
        }
        catch
        {
            return (default, null, null);
        }
    }

    /// <summary>
    /// Reads methods from a type definition.
    /// </summary>
    private List<Models.Modules.MethodMemberInfo> ReadMethods(
        TypeDefinition typeDef,
        MetadataReader reader,
        string declaringType,
        Models.Modules.Visibility? visibility,
        bool includeStatic,
        bool includeInstance)
    {
        var methods = new List<Models.Modules.MethodMemberInfo>();

        foreach (var methodHandle in typeDef.GetMethods())
        {
            var methodDef = reader.GetMethodDefinition(methodHandle);
            var methodName = reader.GetString(methodDef.Name);

            // Skip special methods (property accessors, event handlers) unless they're constructors
            if (methodName.StartsWith("get_") || methodName.StartsWith("set_") ||
                methodName.StartsWith("add_") || methodName.StartsWith("remove_"))
            {
                continue;
            }

            var isStatic = (methodDef.Attributes & MethodAttributes.Static) != 0;
            if (isStatic && !includeStatic) continue;
            if (!isStatic && !includeInstance) continue;

            var methodVisibility = GetMethodVisibility(methodDef.Attributes);
            if (visibility.HasValue && methodVisibility != visibility.Value) continue;

            var isVirtual = (methodDef.Attributes & MethodAttributes.Virtual) != 0;
            var isAbstract = (methodDef.Attributes & MethodAttributes.Abstract) != 0;

            // Decode signature
            var signature = methodDef.DecodeSignature(new SignatureTypeProvider(reader), null);
            var returnType = signature.ReturnType;
            var parameters = ReadMethodParameters(methodDef, reader, signature);
            var genericParams = ReadGenericParameters(methodDef.GetGenericParameters(), reader);

            var signatureStr = BuildMethodSignature(methodName, returnType, parameters, genericParams);

            methods.Add(new Models.Modules.MethodMemberInfo(
                Name: methodName,
                Signature: signatureStr,
                ReturnType: returnType,
                Parameters: parameters,
                Visibility: methodVisibility,
                IsStatic: isStatic,
                IsVirtual: isVirtual,
                IsAbstract: isAbstract,
                IsGeneric: genericParams.Length > 0,
                GenericParameters: genericParams.Length > 0 ? genericParams : null,
                DeclaringType: declaringType));
        }

        return methods;
    }

    /// <summary>
    /// Reads properties from a type definition.
    /// </summary>
    private List<Models.Modules.PropertyMemberInfo> ReadProperties(
        TypeDefinition typeDef,
        MetadataReader reader,
        string declaringType,
        Models.Modules.Visibility? visibility,
        bool includeStatic,
        bool includeInstance)
    {
        var properties = new List<Models.Modules.PropertyMemberInfo>();

        foreach (var propHandle in typeDef.GetProperties())
        {
            var propDef = reader.GetPropertyDefinition(propHandle);
            var propName = reader.GetString(propDef.Name);
            var accessors = propDef.GetAccessors();

            Models.Modules.Visibility? getterVis = null;
            Models.Modules.Visibility? setterVis = null;
            bool isStatic = false;
            bool isIndexer = false;
            Models.Modules.ParameterInfo[]? indexerParams = null;

            if (!accessors.Getter.IsNil)
            {
                var getter = reader.GetMethodDefinition(accessors.Getter);
                getterVis = GetMethodVisibility(getter.Attributes);
                isStatic = (getter.Attributes & MethodAttributes.Static) != 0;

                // Check if it's an indexer
                var sig = getter.DecodeSignature(new SignatureTypeProvider(reader), null);
                if (sig.ParameterTypes.Length > 0)
                {
                    isIndexer = true;
                    indexerParams = sig.ParameterTypes.Select((t, i) =>
                        new Models.Modules.ParameterInfo($"index{i}", t, false, false, false, null)).ToArray();
                }
            }

            if (!accessors.Setter.IsNil)
            {
                var setter = reader.GetMethodDefinition(accessors.Setter);
                setterVis = GetMethodVisibility(setter.Attributes);
                if (accessors.Getter.IsNil)
                {
                    isStatic = (setter.Attributes & MethodAttributes.Static) != 0;
                }
            }

            if (isStatic && !includeStatic) continue;
            if (!isStatic && !includeInstance) continue;

            // Overall visibility is the most accessible
            var propVisibility = getterVis ?? setterVis ?? Models.Modules.Visibility.Private;
            if (setterVis.HasValue && IsMoreAccessible(setterVis.Value, propVisibility))
            {
                propVisibility = setterVis.Value;
            }

            if (visibility.HasValue && propVisibility != visibility.Value) continue;

            // Get property type
            var propSig = propDef.DecodeSignature(new SignatureTypeProvider(reader), null);
            var propType = propSig.ReturnType;

            properties.Add(new Models.Modules.PropertyMemberInfo(
                Name: propName,
                Type: propType,
                Visibility: propVisibility,
                IsStatic: isStatic,
                HasGetter: !accessors.Getter.IsNil,
                HasSetter: !accessors.Setter.IsNil,
                GetterVisibility: getterVis,
                SetterVisibility: setterVis,
                IsIndexer: isIndexer,
                IndexerParameters: indexerParams));
        }

        return properties;
    }

    /// <summary>
    /// Reads fields from a type definition.
    /// </summary>
    private List<Models.Modules.FieldMemberInfo> ReadFields(
        TypeDefinition typeDef,
        MetadataReader reader,
        string declaringType,
        Models.Modules.Visibility? visibility,
        bool includeStatic,
        bool includeInstance)
    {
        var fields = new List<Models.Modules.FieldMemberInfo>();

        foreach (var fieldHandle in typeDef.GetFields())
        {
            var fieldDef = reader.GetFieldDefinition(fieldHandle);
            var fieldName = reader.GetString(fieldDef.Name);

            // Skip compiler-generated backing fields
            if (fieldName.StartsWith("<") && fieldName.Contains(">"))
            {
                continue;
            }

            var isStatic = (fieldDef.Attributes & FieldAttributes.Static) != 0;
            if (isStatic && !includeStatic) continue;
            if (!isStatic && !includeInstance) continue;

            var fieldVisibility = GetFieldVisibility(fieldDef.Attributes);
            if (visibility.HasValue && fieldVisibility != visibility.Value) continue;

            var isReadOnly = (fieldDef.Attributes & FieldAttributes.InitOnly) != 0;
            var isConst = (fieldDef.Attributes & FieldAttributes.Literal) != 0;

            // Get field type
            var fieldSig = fieldDef.DecodeSignature(new SignatureTypeProvider(reader), null);

            // Get constant value if const
            string? constValue = null;
            if (isConst)
            {
                var defaultValue = fieldDef.GetDefaultValue();
                if (!defaultValue.IsNil)
                {
                    var constant = reader.GetConstant(defaultValue);
                    var blob = reader.GetBlobReader(constant.Value);
                    constValue = ReadConstantValue(blob, constant.TypeCode);
                }
            }

            fields.Add(new Models.Modules.FieldMemberInfo(
                Name: fieldName,
                Type: fieldSig,
                Visibility: fieldVisibility,
                IsStatic: isStatic,
                IsReadOnly: isReadOnly,
                IsConst: isConst,
                ConstValue: constValue));
        }

        return fields;
    }

    /// <summary>
    /// Reads events from a type definition.
    /// </summary>
    private List<Models.Modules.EventMemberInfo> ReadEvents(
        TypeDefinition typeDef,
        MetadataReader reader,
        string declaringType,
        Models.Modules.Visibility? visibility,
        bool includeStatic,
        bool includeInstance)
    {
        var events = new List<Models.Modules.EventMemberInfo>();

        foreach (var eventHandle in typeDef.GetEvents())
        {
            var eventDef = reader.GetEventDefinition(eventHandle);
            var eventName = reader.GetString(eventDef.Name);
            var accessors = eventDef.GetAccessors();

            Models.Modules.Visibility eventVis = Models.Modules.Visibility.Private;
            bool isStatic = false;
            string? addMethod = null;
            string? removeMethod = null;

            if (!accessors.Adder.IsNil)
            {
                var adder = reader.GetMethodDefinition(accessors.Adder);
                eventVis = GetMethodVisibility(adder.Attributes);
                isStatic = (adder.Attributes & MethodAttributes.Static) != 0;
                addMethod = reader.GetString(adder.Name);
            }

            if (!accessors.Remover.IsNil)
            {
                var remover = reader.GetMethodDefinition(accessors.Remover);
                removeMethod = reader.GetString(remover.Name);
                if (accessors.Adder.IsNil)
                {
                    eventVis = GetMethodVisibility(remover.Attributes);
                    isStatic = (remover.Attributes & MethodAttributes.Static) != 0;
                }
            }

            if (isStatic && !includeStatic) continue;
            if (!isStatic && !includeInstance) continue;
            if (visibility.HasValue && eventVis != visibility.Value) continue;

            // Get event type
            var eventType = eventDef.Type;
            var eventTypeName = GetTypeReferenceName(eventType, reader);

            events.Add(new Models.Modules.EventMemberInfo(
                Name: eventName,
                Type: eventTypeName,
                Visibility: eventVis,
                IsStatic: isStatic,
                AddMethod: addMethod,
                RemoveMethod: removeMethod));
        }

        return events;
    }

    /// <summary>
    /// Reads inherited members from base types.
    /// </summary>
    private async Task<(List<Models.Modules.MethodMemberInfo> Methods,
                        List<Models.Modules.PropertyMemberInfo> Properties,
                        List<Models.Modules.FieldMemberInfo> Fields,
                        List<Models.Modules.EventMemberInfo> Events)>
        ReadInheritedMembersAsync(
            EntityHandle baseTypeHandle,
            MetadataReader reader,
            PEReader peReader,
            string currentModulePath,
            string[]? memberKinds,
            Models.Modules.Visibility? visibility,
            bool includeStatic,
            bool includeInstance,
            CancellationToken cancellationToken)
    {
        var methods = new List<Models.Modules.MethodMemberInfo>();
        var properties = new List<Models.Modules.PropertyMemberInfo>();
        var fields = new List<Models.Modules.FieldMemberInfo>();
        var events = new List<Models.Modules.EventMemberInfo>();

        var baseTypeName = GetTypeReferenceName(baseTypeHandle, reader);

        // Don't include Object's members as they're implicit
        if (baseTypeName == "System.Object" || baseTypeName == "Object")
        {
            return (methods, properties, fields, events);
        }

        // Try to find and read the base type
        try
        {
            var result = await GetMembersAsync(
                baseTypeName,
                moduleName: null, // Search all modules
                includeInherited: true, // Recursively get inherited members
                memberKinds,
                visibility,
                includeStatic,
                includeInstance,
                cancellationToken);

            methods.AddRange(result.Methods);
            properties.AddRange(result.Properties);
            fields.AddRange(result.Fields);
            events.AddRange(result.Events);
        }
        catch (InvalidOperationException)
        {
            // Base type not found in loaded modules - ignore
        }

        return (methods, properties, fields, events);
    }

    /// <summary>
    /// Reads method parameters from a method definition.
    /// </summary>
    private Models.Modules.ParameterInfo[] ReadMethodParameters(
        MethodDefinition methodDef,
        MetadataReader reader,
        MethodSignature<string> signature)
    {
        var parameters = new List<Models.Modules.ParameterInfo>();
        var paramHandles = methodDef.GetParameters().ToArray();

        for (int i = 0; i < signature.ParameterTypes.Length; i++)
        {
            var paramType = signature.ParameterTypes[i];
            var isOut = paramType.StartsWith("out ");
            var isRef = paramType.StartsWith("ref ");

            string paramName = $"arg{i}";
            bool isOptional = false;
            string? defaultValue = null;

            // Find matching parameter handle (they're 1-indexed, 0 is return)
            foreach (var ph in paramHandles)
            {
                var param = reader.GetParameter(ph);
                if (param.SequenceNumber == i + 1)
                {
                    paramName = reader.GetString(param.Name);
                    isOptional = (param.Attributes & ParameterAttributes.Optional) != 0 ||
                                (param.Attributes & ParameterAttributes.HasDefault) != 0;

                    if ((param.Attributes & ParameterAttributes.HasDefault) != 0)
                    {
                        var defaultHandle = param.GetDefaultValue();
                        if (!defaultHandle.IsNil)
                        {
                            var constant = reader.GetConstant(defaultHandle);
                            var blob = reader.GetBlobReader(constant.Value);
                            defaultValue = ReadConstantValue(blob, constant.TypeCode);
                        }
                    }
                    break;
                }
            }

            parameters.Add(new Models.Modules.ParameterInfo(
                Name: paramName,
                Type: paramType.Replace("out ", "").Replace("ref ", ""),
                IsOptional: isOptional,
                IsOut: isOut,
                IsRef: isRef,
                DefaultValue: defaultValue));
        }

        return parameters.ToArray();
    }

    /// <summary>
    /// Reads generic parameters from a type or method.
    /// </summary>
    private string[] ReadGenericParameters(GenericParameterHandleCollection handles, MetadataReader reader)
    {
        var names = new List<string>();
        foreach (var handle in handles)
        {
            var param = reader.GetGenericParameter(handle);
            names.Add(reader.GetString(param.Name));
        }
        return names.ToArray();
    }

    /// <summary>
    /// Builds a method signature string.
    /// </summary>
    private string BuildMethodSignature(string name, string returnType, Models.Modules.ParameterInfo[] parameters, string[] genericParams)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(returnType);
        sb.Append(' ');
        sb.Append(name);

        if (genericParams.Length > 0)
        {
            sb.Append('<');
            sb.Append(string.Join(", ", genericParams));
            sb.Append('>');
        }

        sb.Append('(');
        for (int i = 0; i < parameters.Length; i++)
        {
            if (i > 0) sb.Append(", ");

            var p = parameters[i];
            if (p.IsOut) sb.Append("out ");
            else if (p.IsRef) sb.Append("ref ");

            sb.Append(p.Type);
            sb.Append(' ');
            sb.Append(p.Name);

            if (p.IsOptional && p.DefaultValue != null)
            {
                sb.Append(" = ");
                sb.Append(p.DefaultValue);
            }
        }
        sb.Append(')');

        return sb.ToString();
    }

    /// <summary>
    /// Gets visibility from method attributes.
    /// </summary>
    private static Models.Modules.Visibility GetMethodVisibility(MethodAttributes attributes)
    {
        return (attributes & MethodAttributes.MemberAccessMask) switch
        {
            MethodAttributes.Public => Models.Modules.Visibility.Public,
            MethodAttributes.Family => Models.Modules.Visibility.Protected,
            MethodAttributes.Assembly => Models.Modules.Visibility.Internal,
            MethodAttributes.FamORAssem => Models.Modules.Visibility.ProtectedInternal,
            MethodAttributes.FamANDAssem => Models.Modules.Visibility.PrivateProtected,
            MethodAttributes.Private => Models.Modules.Visibility.Private,
            _ => Models.Modules.Visibility.Private
        };
    }

    /// <summary>
    /// Gets visibility from field attributes.
    /// </summary>
    private static Models.Modules.Visibility GetFieldVisibility(FieldAttributes attributes)
    {
        return (attributes & FieldAttributes.FieldAccessMask) switch
        {
            FieldAttributes.Public => Models.Modules.Visibility.Public,
            FieldAttributes.Family => Models.Modules.Visibility.Protected,
            FieldAttributes.Assembly => Models.Modules.Visibility.Internal,
            FieldAttributes.FamORAssem => Models.Modules.Visibility.ProtectedInternal,
            FieldAttributes.FamANDAssem => Models.Modules.Visibility.PrivateProtected,
            FieldAttributes.Private => Models.Modules.Visibility.Private,
            _ => Models.Modules.Visibility.Private
        };
    }

    /// <summary>
    /// Checks if one visibility is more accessible than another.
    /// </summary>
    private static bool IsMoreAccessible(Models.Modules.Visibility a, Models.Modules.Visibility b)
    {
        int GetAccessLevel(Models.Modules.Visibility v) => v switch
        {
            Models.Modules.Visibility.Public => 5,
            Models.Modules.Visibility.ProtectedInternal => 4,
            Models.Modules.Visibility.Protected => 3,
            Models.Modules.Visibility.Internal => 3,
            Models.Modules.Visibility.PrivateProtected => 2,
            Models.Modules.Visibility.Private => 1,
            _ => 0
        };
        return GetAccessLevel(a) > GetAccessLevel(b);
    }

    /// <summary>
    /// Reads a constant value from a blob reader.
    /// </summary>
    private static string? ReadConstantValue(BlobReader reader, ConstantTypeCode typeCode)
    {
        try
        {
            return typeCode switch
            {
                ConstantTypeCode.Boolean => reader.ReadBoolean().ToString().ToLowerInvariant(),
                ConstantTypeCode.Char => $"'{reader.ReadChar()}'",
                ConstantTypeCode.SByte => reader.ReadSByte().ToString(),
                ConstantTypeCode.Byte => reader.ReadByte().ToString(),
                ConstantTypeCode.Int16 => reader.ReadInt16().ToString(),
                ConstantTypeCode.UInt16 => reader.ReadUInt16().ToString(),
                ConstantTypeCode.Int32 => reader.ReadInt32().ToString(),
                ConstantTypeCode.UInt32 => reader.ReadUInt32().ToString(),
                ConstantTypeCode.Int64 => reader.ReadInt64().ToString(),
                ConstantTypeCode.UInt64 => reader.ReadUInt64().ToString(),
                ConstantTypeCode.Single => reader.ReadSingle().ToString(),
                ConstantTypeCode.Double => reader.ReadDouble().ToString(),
                ConstantTypeCode.String => $"\"{reader.ReadSerializedString()}\"",
                ConstantTypeCode.NullReference => "null",
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Signature type provider for decoding method signatures.
    /// </summary>
    private sealed class SignatureTypeProvider : ISignatureTypeProvider<string, object?>
    {
        private readonly MetadataReader _reader;

        public SignatureTypeProvider(MetadataReader reader)
        {
            _reader = reader;
        }

        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode switch
        {
            PrimitiveTypeCode.Boolean => "bool",
            PrimitiveTypeCode.Byte => "byte",
            PrimitiveTypeCode.SByte => "sbyte",
            PrimitiveTypeCode.Char => "char",
            PrimitiveTypeCode.Int16 => "short",
            PrimitiveTypeCode.UInt16 => "ushort",
            PrimitiveTypeCode.Int32 => "int",
            PrimitiveTypeCode.UInt32 => "uint",
            PrimitiveTypeCode.Int64 => "long",
            PrimitiveTypeCode.UInt64 => "ulong",
            PrimitiveTypeCode.Single => "float",
            PrimitiveTypeCode.Double => "double",
            PrimitiveTypeCode.String => "string",
            PrimitiveTypeCode.Object => "object",
            PrimitiveTypeCode.IntPtr => "IntPtr",
            PrimitiveTypeCode.UIntPtr => "UIntPtr",
            PrimitiveTypeCode.Void => "void",
            PrimitiveTypeCode.TypedReference => "TypedReference",
            _ => typeCode.ToString()
        };

        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
        {
            var typeDef = reader.GetTypeDefinition(handle);
            var ns = reader.GetString(typeDef.Namespace);
            var name = reader.GetString(typeDef.Name);
            return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
        }

        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
        {
            var typeRef = reader.GetTypeReference(handle);
            var ns = reader.GetString(typeRef.Namespace);
            var name = reader.GetString(typeRef.Name);
            return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
        }

        public string GetSZArrayType(string elementType) => $"{elementType}[]";
        public string GetArrayType(string elementType, ArrayShape shape) => $"{elementType}[{new string(',', shape.Rank - 1)}]";
        public string GetByReferenceType(string elementType) => $"ref {elementType}";
        public string GetPointerType(string elementType) => $"{elementType}*";
        public string GetPinnedType(string elementType) => elementType;
        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;

        public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments)
        {
            var name = genericType;
            var backtick = name.IndexOf('`');
            if (backtick > 0) name = name.Substring(0, backtick);
            return $"{name}<{string.Join(", ", typeArguments)}>";
        }

        public string GetGenericTypeParameter(object? genericContext, int index) => $"T{index}";
        public string GetGenericMethodParameter(object? genericContext, int index) => $"TM{index}";
        public string GetFunctionPointerType(MethodSignature<string> signature) => "delegate*";
        public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
        {
            var spec = reader.GetTypeSpecification(handle);
            return spec.DecodeSignature(this, genericContext);
        }
    }

    /// <inheritdoc />
    public async Task<Models.Modules.SearchResult> SearchModulesAsync(
        string pattern,
        Models.Modules.SearchType searchType = Models.Modules.SearchType.Both,
        string? moduleFilter = null,
        bool caseSensitive = false,
        int maxResults = 50,
        CancellationToken cancellationToken = default)
    {
        if (_process == null)
        {
            throw new InvalidOperationException("Cannot search modules: debugger is not attached to any process");
        }

        // Clamp max results
        maxResults = Math.Clamp(maxResults, 1, 100);

        var types = new List<Models.Modules.TypeInfo>();
        var methods = new List<Models.Modules.MethodSearchMatch>();
        var totalMatches = 0;

        var comparisonType = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        await Task.Run(() =>
        {
            var moduleIdCounter = 0;
            foreach (var appDomain in _process!.AppDomains)
            {
                foreach (var assembly in appDomain.Assemblies)
                {
                    foreach (var module in assembly.Modules)
                    {
                        var info = ExtractModuleInfo(module, ref moduleIdCounter);

                        // Apply module filter
                        if (moduleFilter != null)
                        {
                            if (!MatchesWildcardPattern(info.Name, moduleFilter, caseSensitive) &&
                                !MatchesWildcardPattern(info.FullName, moduleFilter, caseSensitive))
                            {
                                continue;
                            }
                        }

                        if (string.IsNullOrEmpty(info.Path) || !File.Exists(info.Path))
                            continue;

                        try
                        {
                            SearchInModule(info, pattern, searchType, caseSensitive,
                                types, methods, ref totalMatches, maxResults);
                        }
                        catch
                        {
                            // Skip modules that can't be read
                        }

                        // Early exit if we have enough results
                        if (types.Count + methods.Count >= maxResults)
                            break;
                    }

                    if (types.Count + methods.Count >= maxResults)
                        break;
                }

                if (types.Count + methods.Count >= maxResults)
                    break;
            }
        }, cancellationToken);

        var returnedMatches = types.Count + methods.Count;
        var truncated = totalMatches > returnedMatches;

        return new Models.Modules.SearchResult(
            Query: pattern,
            SearchType: searchType,
            Types: types.ToArray(),
            Methods: methods.ToArray(),
            TotalMatches: totalMatches,
            ReturnedMatches: returnedMatches,
            Truncated: truncated,
            ContinuationToken: truncated ? $"offset:{returnedMatches}" : null);
    }

    /// <summary>
    /// Searches for types and methods in a single module.
    /// </summary>
    private void SearchInModule(
        Models.Modules.ModuleInfo moduleInfo,
        string pattern,
        Models.Modules.SearchType searchType,
        bool caseSensitive,
        List<Models.Modules.TypeInfo> types,
        List<Models.Modules.MethodSearchMatch> methods,
        ref int totalMatches,
        int maxResults)
    {
        using var peReader = new PEReader(File.OpenRead(moduleInfo.Path!));
        if (!peReader.HasMetadata)
            return;

        var reader = peReader.GetMetadataReader();

        // Search types
        if (searchType == Models.Modules.SearchType.Types || searchType == Models.Modules.SearchType.Both)
        {
            foreach (var typeHandle in reader.TypeDefinitions)
            {
                var typeDef = reader.GetTypeDefinition(typeHandle);
                var name = reader.GetString(typeDef.Name);
                var ns = reader.GetString(typeDef.Namespace);
                var fullName = string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";

                // Skip compiler-generated types
                if (name.StartsWith("<") || name.Contains("AnonymousType"))
                    continue;

                // Check if matches pattern
                if (MatchesWildcardPattern(name, pattern, caseSensitive) ||
                    MatchesWildcardPattern(fullName, pattern, caseSensitive))
                {
                    totalMatches++;

                    if (types.Count < maxResults)
                    {
                        var typeInfo = BuildTypeInfo(typeDef, reader, moduleInfo.Name);
                        types.Add(typeInfo);
                    }
                }
            }
        }

        // Search methods
        if (searchType == Models.Modules.SearchType.Methods || searchType == Models.Modules.SearchType.Both)
        {
            foreach (var typeHandle in reader.TypeDefinitions)
            {
                var typeDef = reader.GetTypeDefinition(typeHandle);
                var typeName = reader.GetString(typeDef.Name);
                var typeNs = reader.GetString(typeDef.Namespace);
                var fullTypeName = string.IsNullOrEmpty(typeNs) ? typeName : $"{typeNs}.{typeName}";

                // Skip compiler-generated types
                if (typeName.StartsWith("<") || typeName.Contains("AnonymousType"))
                    continue;

                foreach (var methodHandle in typeDef.GetMethods())
                {
                    var methodDef = reader.GetMethodDefinition(methodHandle);
                    var methodName = reader.GetString(methodDef.Name);

                    // Skip special methods
                    if (methodName.StartsWith("get_") || methodName.StartsWith("set_") ||
                        methodName.StartsWith("add_") || methodName.StartsWith("remove_") ||
                        methodName.StartsWith("<"))
                    {
                        continue;
                    }

                    // Check if method name matches pattern
                    if (MatchesWildcardPattern(methodName, pattern, caseSensitive))
                    {
                        totalMatches++;

                        if (methods.Count < maxResults)
                        {
                            try
                            {
                                var methodInfo = BuildMethodInfo(methodDef, reader, fullTypeName);
                                methods.Add(new Models.Modules.MethodSearchMatch(
                                    DeclaringType: fullTypeName,
                                    ModuleName: moduleInfo.Name,
                                    Method: methodInfo,
                                    MatchReason: "name"));
                            }
                            catch
                            {
                                // Skip methods that can't be decoded
                            }
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Builds a MethodMemberInfo from a method definition.
    /// </summary>
    private Models.Modules.MethodMemberInfo BuildMethodInfo(
        MethodDefinition methodDef,
        MetadataReader reader,
        string declaringType)
    {
        var methodName = reader.GetString(methodDef.Name);
        var isStatic = (methodDef.Attributes & MethodAttributes.Static) != 0;
        var isVirtual = (methodDef.Attributes & MethodAttributes.Virtual) != 0;
        var isAbstract = (methodDef.Attributes & MethodAttributes.Abstract) != 0;
        var methodVisibility = GetMethodVisibility(methodDef.Attributes);

        // Decode signature
        var signature = methodDef.DecodeSignature(new SignatureTypeProvider(reader), null);
        var returnType = signature.ReturnType;
        var parameters = ReadMethodParameters(methodDef, reader, signature);
        var genericParams = ReadGenericParameters(methodDef.GetGenericParameters(), reader);

        var signatureStr = BuildMethodSignature(methodName, returnType, parameters, genericParams);

        return new Models.Modules.MethodMemberInfo(
            Name: methodName,
            Signature: signatureStr,
            ReturnType: returnType,
            Parameters: parameters,
            Visibility: methodVisibility,
            IsStatic: isStatic,
            IsVirtual: isVirtual,
            IsAbstract: isAbstract,
            IsGeneric: genericParams.Length > 0,
            GenericParameters: genericParams.Length > 0 ? genericParams : null,
            DeclaringType: declaringType);
    }

    #endregion
}
