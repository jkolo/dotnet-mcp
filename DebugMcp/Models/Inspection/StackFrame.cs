namespace DebugMcp.Models.Inspection;

/// <summary>
/// Represents a single frame in the call stack.
/// </summary>
/// <param name="Index">Frame index (0 = top of stack).</param>
/// <param name="Function">Full method name (Namespace.Class.Method).</param>
/// <param name="Module">Assembly name (e.g., "MyApp.dll").</param>
/// <param name="IsExternal">True if no source available (framework code).</param>
/// <param name="Location">Source file/line if symbols available.</param>
/// <param name="Arguments">Method arguments with values.</param>
public sealed record StackFrame(
    int Index,
    string Function,
    string Module,
    bool IsExternal,
    SourceLocation? Location = null,
    IReadOnlyList<Variable>? Arguments = null);
