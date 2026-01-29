using DebugMcp.Models;
using DebugMcp.Models.Breakpoints;
using DebugMcp.Models.Inspection;
using DebugMcp.Models.Memory;
using DebugMcp.Models.Modules;
using DebugMcp.Services;
using DebugMcp.Services.Breakpoints;
using DebugMcp.Tests.Helpers;
using Microsoft.Extensions.Logging;
using Moq;

namespace DebugMcp.E2E.Support;

/// <summary>
/// Shared scenario-scoped context for Reqnroll E2E tests.
/// Holds debugger services and scenario state (last hit, variables, etc.).
/// Injected into all step definition classes via Reqnroll context injection.
/// </summary>
public sealed class DebuggerContext : IDisposable
{
    // Loggers (mocked for test isolation)
    private readonly Mock<ILogger<ProcessDebugger>> _debuggerLoggerMock = new();
    private readonly Mock<ILogger<DebugSessionManager>> _managerLoggerMock = new();
    private readonly Mock<ILogger<BreakpointRegistry>> _registryLoggerMock = new();
    private readonly Mock<ILogger<BreakpointManager>> _bpManagerLoggerMock = new();
    private readonly Mock<ILogger<PdbSymbolReader>> _pdbLoggerMock = new();
    private readonly Mock<ILogger<PdbSymbolCache>> _pdbCacheLoggerMock = new();

    // Core services
    public PdbSymbolCache PdbCache { get; }
    public PdbSymbolReader PdbReader { get; }
    public ProcessDebugger ProcessDebugger { get; }
    public DebugSessionManager SessionManager { get; }
    public BreakpointRegistry BreakpointRegistry { get; }
    public SimpleConditionEvaluator ConditionEvaluator { get; }
    public BreakpointManager BreakpointManager { get; }

    // Process management
    public TestTargetProcess? TargetProcess { get; set; }

    // Scenario state
    public SessionState? CurrentState { get; set; }
    public BreakpointHit? LastBreakpointHit { get; set; }
    public Breakpoint? LastSetBreakpoint { get; set; }
    public List<Breakpoint> SetBreakpoints { get; } = [];
    public StackFrame[]? LastStackTrace { get; set; }
    public Variable[]? LastVariables { get; set; }
    public string? LastEvalResultValue { get; set; }
    public string? LastEvalResultType { get; set; }
    public IReadOnlyList<ModuleInfo>? LastModules { get; set; }

    // Thread inspection state
    public IReadOnlyList<ThreadInfo>? LastThreads { get; set; }

    // Expression evaluation error state
    public string? LastExpressionError { get; set; }

    // Debug state info
    public string? LastDebugState { get; set; }

    // Module/type search state
    public SearchResult? LastSearchResult { get; set; }
    public TypesResult? LastTypesResult { get; set; }
    public TypeMembersResult? LastMembersResult { get; set; }

    // Memory/object inspection state
    public ObjectInspection? LastObjectInspection { get; set; }
    public TypeLayout? LastTypeLayout { get; set; }
    public ReferencesResult? LastReferencesResult { get; set; }
    public byte[]? LastMemoryBytes { get; set; }

    // Error tracking for scenarios testing error paths
    public string? LastOperationError { get; set; }

    public DebuggerContext()
    {
        PdbCache = new PdbSymbolCache(_pdbCacheLoggerMock.Object);
        PdbReader = new PdbSymbolReader(PdbCache, _pdbLoggerMock.Object);
        ProcessDebugger = new ProcessDebugger(_debuggerLoggerMock.Object, PdbReader);
        SessionManager = new DebugSessionManager(ProcessDebugger, _managerLoggerMock.Object);

        BreakpointRegistry = new BreakpointRegistry(_registryLoggerMock.Object);
        ConditionEvaluator = new SimpleConditionEvaluator();
        BreakpointManager = new BreakpointManager(
            BreakpointRegistry,
            PdbReader,
            ProcessDebugger,
            ConditionEvaluator,
            _bpManagerLoggerMock.Object);
    }

    public async Task CleanupAsync()
    {
        try { await BreakpointManager.ClearAllBreakpointsAsync(CancellationToken.None); } catch { }
        try { await SessionManager.DisconnectAsync(terminateProcess: true); } catch { }

        if (TargetProcess != null)
        {
            TargetProcess.Dispose();
            TargetProcess = null;
        }
    }

    public void Dispose()
    {
        ProcessDebugger.Dispose();
    }
}
