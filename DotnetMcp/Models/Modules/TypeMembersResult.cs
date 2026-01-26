namespace DotnetMcp.Models.Modules;

/// <summary>
/// Aggregated result for type member inspection.
/// </summary>
/// <param name="TypeName">Full type name.</param>
/// <param name="Methods">Method members.</param>
/// <param name="Properties">Property members.</param>
/// <param name="Fields">Field members.</param>
/// <param name="Events">Event members.</param>
/// <param name="IncludesInherited">True if base class members included.</param>
/// <param name="MethodCount">Total method count.</param>
/// <param name="PropertyCount">Total property count.</param>
/// <param name="FieldCount">Total field count.</param>
/// <param name="EventCount">Total event count.</param>
public sealed record TypeMembersResult(
    string TypeName,
    MethodMemberInfo[] Methods,
    PropertyMemberInfo[] Properties,
    FieldMemberInfo[] Fields,
    EventMemberInfo[] Events,
    bool IncludesInherited,
    int MethodCount,
    int PropertyCount,
    int FieldCount,
    int EventCount);
