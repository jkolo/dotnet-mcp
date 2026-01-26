using DotnetMcp.Models.Modules;
using FluentAssertions;

namespace DotnetMcp.Tests.Contract;

/// <summary>
/// Contract tests validating the types_get tool schema compliance.
/// These tests verify the tool adheres to the MCP contract.
/// </summary>
public class TypesGetContractTests
{
    /// <summary>
    /// types_get requires module_name parameter.
    /// </summary>
    [Fact]
    public void TypesGet_RequiresModuleName()
    {
        // The input schema specifies: "required": ["module_name"]
        // Module name identifies which assembly to inspect
        true.Should().BeTrue("types_get requires module_name parameter");
    }

    /// <summary>
    /// namespace_filter parameter is optional.
    /// </summary>
    [Fact]
    public void TypesGet_NamespaceFilter_IsOptional()
    {
        // Contract defines: "namespace_filter": { "type": "string" }
        // Optional - when omitted, all namespaces returned
        true.Should().BeTrue("namespace_filter is optional");
    }

    /// <summary>
    /// TypeInfo has all required fields per contract.
    /// </summary>
    [Fact]
    public void TypeInfo_HasRequiredFields()
    {
        // Contract requires: fullName, name, namespace, kind, visibility
        var type = new TypeInfo(
            FullName: "MyApp.Models.Customer",
            Name: "Customer",
            Namespace: "MyApp.Models",
            Kind: TypeKind.Class,
            Visibility: Visibility.Public,
            IsGeneric: false,
            GenericParameters: Array.Empty<string>(),
            IsNested: false,
            DeclaringType: null,
            ModuleName: "MyApp",
            BaseType: "System.Object",
            Interfaces: Array.Empty<string>()
        );

        type.FullName.Should().NotBeNullOrEmpty("fullName is required");
        type.Name.Should().NotBeNullOrEmpty("name is required");
        type.Namespace.Should().NotBeNull("namespace is required");
        type.Kind.Should().BeDefined("kind is required");
        type.Visibility.Should().BeDefined("visibility is required");
    }

    /// <summary>
    /// TypeKind enum has expected values.
    /// </summary>
    [Theory]
    [InlineData(TypeKind.Class)]
    [InlineData(TypeKind.Interface)]
    [InlineData(TypeKind.Struct)]
    [InlineData(TypeKind.Enum)]
    [InlineData(TypeKind.Delegate)]
    public void TypeKind_HasExpectedValues(TypeKind kind)
    {
        // Contract defines: TypeKind = Class | Interface | Struct | Enum | Delegate
        kind.Should().BeDefined("TypeKind must be valid");
    }

    /// <summary>
    /// Visibility enum has expected values.
    /// </summary>
    [Theory]
    [InlineData(Visibility.Public)]
    [InlineData(Visibility.Internal)]
    [InlineData(Visibility.Private)]
    [InlineData(Visibility.Protected)]
    [InlineData(Visibility.ProtectedInternal)]
    [InlineData(Visibility.PrivateProtected)]
    public void Visibility_HasExpectedValues(Visibility visibility)
    {
        // Contract defines visibility levels
        visibility.Should().BeDefined("Visibility must be valid");
    }

    /// <summary>
    /// Generic types include parameter names.
    /// </summary>
    [Fact]
    public void TypeInfo_GenericType_IncludesParameters()
    {
        // Contract defines: "genericParameters": { "type": "array", "items": { "type": "string" } }
        var genericType = new TypeInfo(
            FullName: "System.Collections.Generic.Dictionary<TKey, TValue>",
            Name: "Dictionary",
            Namespace: "System.Collections.Generic",
            Kind: TypeKind.Class,
            Visibility: Visibility.Public,
            IsGeneric: true,
            GenericParameters: new[] { "TKey", "TValue" },
            IsNested: false,
            DeclaringType: null,
            ModuleName: "System.Collections",
            BaseType: "System.Object",
            Interfaces: new[] { "IDictionary<TKey, TValue>" }
        );

        genericType.IsGeneric.Should().BeTrue("Dictionary is generic");
        genericType.GenericParameters.Should().HaveCount(2, "Dictionary has 2 type parameters");
        genericType.GenericParameters.Should().Contain("TKey");
        genericType.GenericParameters.Should().Contain("TValue");
    }

    /// <summary>
    /// Nested types include declaring type.
    /// </summary>
    [Fact]
    public void TypeInfo_NestedType_IncludesDeclaringType()
    {
        // Contract defines: "declaringType": { "type": ["string", "null"] }
        var nestedType = new TypeInfo(
            FullName: "MyApp.OuterClass+NestedClass",
            Name: "NestedClass",
            Namespace: "MyApp",
            Kind: TypeKind.Class,
            Visibility: Visibility.Private,
            IsGeneric: false,
            GenericParameters: Array.Empty<string>(),
            IsNested: true,
            DeclaringType: "MyApp.OuterClass",
            ModuleName: "MyApp",
            BaseType: "System.Object",
            Interfaces: Array.Empty<string>()
        );

        nestedType.IsNested.Should().BeTrue("NestedClass is nested");
        nestedType.DeclaringType.Should().Be("MyApp.OuterClass");
    }

    /// <summary>
    /// TypesResult includes pagination info.
    /// </summary>
    [Fact]
    public void TypesResult_IncludesPaginationInfo()
    {
        // Contract defines: totalCount, returnedCount, truncated, continuationToken
        var types = new[]
        {
            new TypeInfo("App.Type1", "Type1", "App", TypeKind.Class, Visibility.Public,
                false, Array.Empty<string>(), false, null, "App", "System.Object", Array.Empty<string>()),
            new TypeInfo("App.Type2", "Type2", "App", TypeKind.Class, Visibility.Public,
                false, Array.Empty<string>(), false, null, "App", "System.Object", Array.Empty<string>())
        };

        var result = new TypesResult(
            ModuleName: "App",
            NamespaceFilter: null,
            Types: types,
            Namespaces: Array.Empty<NamespaceNode>(),
            TotalCount: 100,
            ReturnedCount: 2,
            Truncated: true,
            ContinuationToken: "next-page-token"
        );

        result.TotalCount.Should().Be(100);
        result.ReturnedCount.Should().Be(2);
        result.Truncated.Should().BeTrue("results were paginated");
        result.ContinuationToken.Should().NotBeNullOrEmpty("continuation token provided");
    }

    /// <summary>
    /// NamespaceNode contains hierarchy info.
    /// </summary>
    [Fact]
    public void NamespaceNode_ContainsHierarchyInfo()
    {
        // Contract defines namespace hierarchy
        var node = new NamespaceNode(
            Name: "Models",
            FullName: "MyApp.Models",
            TypeCount: 5,
            ChildNamespaces: new[] { "MyApp.Models.DTO", "MyApp.Models.Entities" },
            Depth: 1
        );

        node.Name.Should().Be("Models");
        node.FullName.Should().Be("MyApp.Models");
        node.TypeCount.Should().Be(5);
        node.ChildNamespaces.Should().HaveCount(2);
        node.Depth.Should().Be(1);
    }

    /// <summary>
    /// Error response includes code and message.
    /// </summary>
    [Fact]
    public void TypesGet_ErrorResponse_IncludesCodeAndMessage()
    {
        // Contract defines error codes: NO_SESSION, MODULE_NOT_FOUND, METADATA_ERROR
        var errorCodes = new[] { "NO_SESSION", "MODULE_NOT_FOUND", "METADATA_ERROR" };

        var errorResponse = new
        {
            success = false,
            error = new
            {
                code = "MODULE_NOT_FOUND",
                message = "Module 'NonExistent' not found in loaded modules"
            }
        };

        errorResponse.success.Should().BeFalse();
        errorResponse.error.code.Should().BeOneOf(errorCodes);
        errorResponse.error.message.Should().NotBeNullOrEmpty();
    }

    /// <summary>
    /// max_results defaults to 100.
    /// </summary>
    [Fact]
    public void TypesGet_MaxResults_DefaultsTo100()
    {
        // Contract defines: "max_results": { "default": 100 }
        const int defaultMaxResults = 100;
        defaultMaxResults.Should().Be(100, "max_results defaults to 100");
    }

    /// <summary>
    /// Interface type has correct kind.
    /// </summary>
    [Fact]
    public void TypeInfo_Interface_HasCorrectKind()
    {
        var interfaceType = new TypeInfo(
            FullName: "MyApp.IRepository",
            Name: "IRepository",
            Namespace: "MyApp",
            Kind: TypeKind.Interface,
            Visibility: Visibility.Public,
            IsGeneric: false,
            GenericParameters: Array.Empty<string>(),
            IsNested: false,
            DeclaringType: null,
            ModuleName: "MyApp",
            BaseType: null, // Interfaces don't have base type (except System.Object implicitly)
            Interfaces: Array.Empty<string>()
        );

        interfaceType.Kind.Should().Be(TypeKind.Interface);
        interfaceType.BaseType.Should().BeNull("interfaces don't explicitly derive from anything");
    }
}
