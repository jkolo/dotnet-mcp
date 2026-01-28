using DotnetMcp.Models;
using DotnetMcp.Models.Breakpoints;
using DotnetMcp.Models.Inspection;
using DotnetMcp.Models.Modules;
using DotnetMcp.Services;
using DotnetMcp.Services.Breakpoints;
using DotnetMcp.Tests.Helpers;
using Microsoft.Extensions.Logging;
using Moq;

namespace DotnetMcp.E2E.Support;

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
