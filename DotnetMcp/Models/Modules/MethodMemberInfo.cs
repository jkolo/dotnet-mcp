namespace DotnetMcp.Models.Modules;

/// <summary>
/// Represents a method in a type.
/// </summary>
/// <param name="Name">Method name (e.g., "GetCustomer").</param>
/// <param name="Signature">Full signature (e.g., "Customer GetCustomer(int id)").</param>
/// <param name="ReturnType">Return type name.</param>
/// <param name="Parameters">Method parameters.</param>
/// <param name="Visibility">Access modifier.</param>
/// <param name="IsStatic">True for static methods.</param>
/// <param name="IsVirtual">True for virtual/override methods.</param>
/// <param name="IsAbstract">True for abstract methods.</param>
/// <param name="IsGeneric">True if generic method.</param>
/// <param name="GenericParameters">Generic type parameters.</param>
/// <param name="DeclaringType">Type that declares this method.</param>
public sealed record MethodMemberInfo(
    string Name,
    string Signature,
    string ReturnType,
    ParameterInfo[] Parameters,
    Visibility Visibility,
    bool IsStatic,
    bool IsVirtual,
    bool IsAbstract,
    bool IsGeneric,
    string[]? GenericParameters,
    string DeclaringType);
