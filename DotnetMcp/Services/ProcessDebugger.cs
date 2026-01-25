using System.Diagnostics;
using System.Runtime.InteropServices;
using ClrDebug;
using DotnetMcp.Infrastructure;
using DotnetMcp.Models;
using DotnetMcp.Models.Inspection;
using DotnetMcp.Services.Breakpoints;
using Microsoft.Extensions.Logging;
using static System.Runtime.InteropServices.NativeLibrary;

namespace DotnetMcp.Services;

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
            if (!wasControlledEval)
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
            if (!wasControlledEval)
            {
                e.Controller.Continue(false);
            }
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
            var typeToken = (int)classType.Token;

            // Look for get_PropertyName method
            var getterName = $"get_{propertyName}";

            // Enumerate methods on the type
            var methodEnum = metaImport.EnumMethods(typeToken);
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

            // Check base types if not found
            // This would require walking the type hierarchy, which is complex
            // For now, just return null if not found in the immediate type
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

                // Find the field
                var fields = metaImport.EnumFields((int)objClass.Token).ToList();
                foreach (var fieldToken in fields)
                {
                    try
                    {
                        var fieldProps = metaImport.GetFieldProps(fieldToken);
                        if (fieldProps.szField == fieldName)
                        {
                            // Use the Raw property to get the ICorDebugClass interface
                            return objValue.GetFieldValue(objClass.Raw, (int)fieldToken);
                        }
                    }
                    catch
                    {
                        // Continue to next field
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
}
