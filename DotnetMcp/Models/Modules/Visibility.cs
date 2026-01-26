namespace DotnetMcp.Models.Modules;

/// <summary>
/// Accessibility level for types and members.
/// </summary>
public enum Visibility
{
    /// <summary>Public - accessible from anywhere.</summary>
    Public,

    /// <summary>Internal - accessible within the assembly.</summary>
    Internal,

    /// <summary>Private - accessible only within the declaring type.</summary>
    Private,

    /// <summary>Protected - accessible within the type and derived types.</summary>
    Protected,

    /// <summary>Protected internal - accessible within the assembly or derived types.</summary>
    ProtectedInternal,

    /// <summary>Private protected - accessible within the type or derived types in the same assembly.</summary>
    PrivateProtected
}
