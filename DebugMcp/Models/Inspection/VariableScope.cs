namespace DebugMcp.Models.Inspection;

/// <summary>
/// Indicates where a variable value comes from in the debugging context.
/// </summary>
public enum VariableScope
{
    /// <summary>Local variable declared in method body.</summary>
    Local,

    /// <summary>Method parameter (argument).</summary>
    Argument,

    /// <summary>The 'this' reference (instance methods only).</summary>
    This,

    /// <summary>Instance or static field of an object.</summary>
    Field,

    /// <summary>Property value (requires getter call).</summary>
    Property,

    /// <summary>Array or collection element.</summary>
    Element
}
