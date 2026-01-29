namespace DebugMcp.Models.Memory;

/// <summary>
/// Type of object reference relationship.
/// </summary>
public enum ReferenceType
{
    /// <summary>Regular instance field reference.</summary>
    Field,

    /// <summary>Array element reference.</summary>
    ArrayElement,

    /// <summary>Static field reference.</summary>
    StaticField,

    /// <summary>Weak reference (may be garbage collected).</summary>
    WeakReference
}
