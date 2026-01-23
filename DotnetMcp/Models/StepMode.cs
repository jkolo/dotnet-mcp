namespace DotnetMcp.Models;

/// <summary>
/// Mode for stepping through code during debugging.
/// </summary>
public enum StepMode
{
    /// <summary>
    /// Step into: Execute the next instruction, entering function calls.
    /// </summary>
    In,

    /// <summary>
    /// Step over: Execute the next instruction, stepping over function calls.
    /// </summary>
    Over,

    /// <summary>
    /// Step out: Continue execution until the current function returns.
    /// </summary>
    Out
}
