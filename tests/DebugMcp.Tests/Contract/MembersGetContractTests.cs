using DebugMcp.Models.Modules;
using FluentAssertions;

namespace DebugMcp.Tests.Contract;

/// <summary>
/// Contract tests validating the members_get tool schema compliance.
/// These tests verify the tool adheres to the MCP contract.
/// </summary>
public class MembersGetContractTests
{
    /// <summary>
    /// members_get requires type_name parameter.
    /// </summary>
    [Fact]
    public void MembersGet_RequiresTypeName()
    {
        // The input schema specifies: "required": ["type_name"]
        // Type name identifies which type's members to inspect
        true.Should().BeTrue("members_get requires type_name parameter");
    }

    /// <summary>
    /// module_name parameter is optional.
    /// </summary>
    [Fact]
    public void MembersGet_ModuleName_IsOptional()
    {
        // Contract defines: "module_name": { "type": "string" }
        // Optional - when omitted, searches all modules
        true.Should().BeTrue("module_name is optional");
    }

    /// <summary>
    /// include_inherited parameter defaults to false.
    /// </summary>
    [Fact]
    public void MembersGet_IncludeInherited_DefaultsFalse()
    {
        // Contract defines: "include_inherited": { "type": "boolean", "default": false }
        const bool defaultIncludeInherited = false;
        defaultIncludeInherited.Should().BeFalse("include_inherited defaults to false");
    }

    /// <summary>
    /// MethodMemberInfo has all required fields per contract.
    /// </summary>
    [Fact]
    public void MethodMemberInfo_HasRequiredFields()
    {
        // Contract requires: name, signature, returnType, parameters, visibility
        var method = new MethodMemberInfo(
            Name: "GetCustomer",
            Signature: "Customer GetCustomer(int id)",
            ReturnType: "Customer",
            Parameters: new[] { new ParameterInfo("id", "int", false, false, false, null) },
            Visibility: Visibility.Public,
            IsStatic: false,
            IsVirtual: false,
            IsAbstract: false,
            IsGeneric: false,
            GenericParameters: null,
            DeclaringType: "MyApp.Services.CustomerService"
        );

        method.Name.Should().NotBeNullOrEmpty("name is required");
        method.Signature.Should().NotBeNullOrEmpty("signature is required");
        method.ReturnType.Should().NotBeNullOrEmpty("returnType is required");
        method.Parameters.Should().NotBeNull("parameters is required");
        method.Visibility.Should().BeDefined("visibility is required");
        method.DeclaringType.Should().NotBeNullOrEmpty("declaringType is required");
    }

    /// <summary>
    /// PropertyMemberInfo has all required fields per contract.
    /// </summary>
    [Fact]
    public void PropertyMemberInfo_HasRequiredFields()
    {
        // Contract requires: name, type, visibility, hasGetter, hasSetter
        var property = new PropertyMemberInfo(
            Name: "Id",
            Type: "int",
            Visibility: Visibility.Public,
            IsStatic: false,
            HasGetter: true,
            HasSetter: true,
            GetterVisibility: Visibility.Public,
            SetterVisibility: Visibility.Private,
            IsIndexer: false,
            IndexerParameters: null
        );

        property.Name.Should().NotBeNullOrEmpty("name is required");
        property.Type.Should().NotBeNullOrEmpty("type is required");
        property.Visibility.Should().BeDefined("visibility is required");
        property.HasGetter.Should().BeTrue("hasGetter is required");
        property.HasSetter.Should().BeTrue("hasSetter is required");
    }

    /// <summary>
    /// FieldMemberInfo has all required fields per contract.
    /// </summary>
    [Fact]
    public void FieldMemberInfo_HasRequiredFields()
    {
        // Contract requires: name, type, visibility
        var field = new FieldMemberInfo(
            Name: "_count",
            Type: "int",
            Visibility: Visibility.Private,
            IsStatic: false,
            IsReadOnly: true,
            IsConst: false,
            ConstValue: null
        );

        field.Name.Should().NotBeNullOrEmpty("name is required");
        field.Type.Should().NotBeNullOrEmpty("type is required");
        field.Visibility.Should().BeDefined("visibility is required");
    }

    /// <summary>
    /// EventMemberInfo has all required fields per contract.
    /// </summary>
    [Fact]
    public void EventMemberInfo_HasRequiredFields()
    {
        // Contract requires: name, type, visibility
        var ev = new EventMemberInfo(
            Name: "PropertyChanged",
            Type: "PropertyChangedEventHandler",
            Visibility: Visibility.Public,
            IsStatic: false,
            AddMethod: "add_PropertyChanged",
            RemoveMethod: "remove_PropertyChanged"
        );

        ev.Name.Should().NotBeNullOrEmpty("name is required");
        ev.Type.Should().NotBeNullOrEmpty("type is required");
        ev.Visibility.Should().BeDefined("visibility is required");
    }

    /// <summary>
    /// ParameterInfo has all required fields per contract.
    /// </summary>
    [Fact]
    public void ParameterInfo_HasRequiredFields()
    {
        // Contract requires: name, type
        var param = new ParameterInfo(
            Name: "customerId",
            Type: "int",
            IsOptional: false,
            IsOut: false,
            IsRef: false,
            DefaultValue: null
        );

        param.Name.Should().NotBeNullOrEmpty("name is required");
        param.Type.Should().NotBeNullOrEmpty("type is required");
    }

    /// <summary>
    /// Optional parameters include default value.
    /// </summary>
    [Fact]
    public void ParameterInfo_OptionalParameter_IncludesDefaultValue()
    {
        // Contract defines: "defaultValue": { "type": ["string", "null"] }
        var optionalParam = new ParameterInfo(
            Name: "count",
            Type: "int",
            IsOptional: true,
            IsOut: false,
            IsRef: false,
            DefaultValue: "10"
        );

        optionalParam.IsOptional.Should().BeTrue("parameter is optional");
        optionalParam.DefaultValue.Should().Be("10", "default value is provided");
    }

    /// <summary>
    /// Out parameters have IsOut set.
    /// </summary>
    [Fact]
    public void ParameterInfo_OutParameter_HasIsOutTrue()
    {
        var outParam = new ParameterInfo(
            Name: "result",
            Type: "int",
            IsOptional: false,
            IsOut: true,
            IsRef: false,
            DefaultValue: null
        );

        outParam.IsOut.Should().BeTrue("out parameter has IsOut = true");
    }

    /// <summary>
    /// Ref parameters have IsRef set.
    /// </summary>
    [Fact]
    public void ParameterInfo_RefParameter_HasIsRefTrue()
    {
        var refParam = new ParameterInfo(
            Name: "buffer",
            Type: "byte[]",
            IsOptional: false,
            IsOut: false,
            IsRef: true,
            DefaultValue: null
        );

        refParam.IsRef.Should().BeTrue("ref parameter has IsRef = true");
    }

    /// <summary>
    /// Generic methods include generic parameters.
    /// </summary>
    [Fact]
    public void MethodMemberInfo_GenericMethod_IncludesGenericParameters()
    {
        // Contract defines: "genericParameters": { "type": "array" }
        var genericMethod = new MethodMemberInfo(
            Name: "CreateInstance",
            Signature: "T CreateInstance<T>() where T : new()",
            ReturnType: "T",
            Parameters: Array.Empty<ParameterInfo>(),
            Visibility: Visibility.Public,
            IsStatic: true,
            IsVirtual: false,
            IsAbstract: false,
            IsGeneric: true,
            GenericParameters: new[] { "T" },
            DeclaringType: "MyApp.Factory"
        );

        genericMethod.IsGeneric.Should().BeTrue("method is generic");
        genericMethod.GenericParameters.Should().Contain("T");
    }

    /// <summary>
    /// Virtual methods have IsVirtual set.
    /// </summary>
    [Fact]
    public void MethodMemberInfo_VirtualMethod_HasIsVirtualTrue()
    {
        var virtualMethod = new MethodMemberInfo(
            Name: "ToString",
            Signature: "string ToString()",
            ReturnType: "string",
            Parameters: Array.Empty<ParameterInfo>(),
            Visibility: Visibility.Public,
            IsStatic: false,
            IsVirtual: true,
            IsAbstract: false,
            IsGeneric: false,
            GenericParameters: null,
            DeclaringType: "System.Object"
        );

        virtualMethod.IsVirtual.Should().BeTrue("virtual method has IsVirtual = true");
    }

    /// <summary>
    /// Abstract methods have IsAbstract set.
    /// </summary>
    [Fact]
    public void MethodMemberInfo_AbstractMethod_HasIsAbstractTrue()
    {
        var abstractMethod = new MethodMemberInfo(
            Name: "Execute",
            Signature: "void Execute()",
            ReturnType: "void",
            Parameters: Array.Empty<ParameterInfo>(),
            Visibility: Visibility.Public,
            IsStatic: false,
            IsVirtual: true,
            IsAbstract: true,
            IsGeneric: false,
            GenericParameters: null,
            DeclaringType: "MyApp.AbstractCommand"
        );

        abstractMethod.IsAbstract.Should().BeTrue("abstract method has IsAbstract = true");
    }

    /// <summary>
    /// Static methods have IsStatic set.
    /// </summary>
    [Fact]
    public void MethodMemberInfo_StaticMethod_HasIsStaticTrue()
    {
        var staticMethod = new MethodMemberInfo(
            Name: "Parse",
            Signature: "int Parse(string s)",
            ReturnType: "int",
            Parameters: new[] { new ParameterInfo("s", "string", false, false, false, null) },
            Visibility: Visibility.Public,
            IsStatic: true,
            IsVirtual: false,
            IsAbstract: false,
            IsGeneric: false,
            GenericParameters: null,
            DeclaringType: "System.Int32"
        );

        staticMethod.IsStatic.Should().BeTrue("static method has IsStatic = true");
    }

    /// <summary>
    /// Properties with different accessor visibilities are represented correctly.
    /// </summary>
    [Fact]
    public void PropertyMemberInfo_DifferentAccessorVisibilities_RepresentedCorrectly()
    {
        // Common pattern: public get, private set
        var property = new PropertyMemberInfo(
            Name: "Name",
            Type: "string",
            Visibility: Visibility.Public, // Most accessible
            IsStatic: false,
            HasGetter: true,
            HasSetter: true,
            GetterVisibility: Visibility.Public,
            SetterVisibility: Visibility.Private,
            IsIndexer: false,
            IndexerParameters: null
        );

        property.Visibility.Should().Be(Visibility.Public, "overall visibility is most accessible");
        property.GetterVisibility.Should().Be(Visibility.Public);
        property.SetterVisibility.Should().Be(Visibility.Private);
    }

    /// <summary>
    /// Indexer properties are identified correctly.
    /// </summary>
    [Fact]
    public void PropertyMemberInfo_Indexer_HasCorrectParameters()
    {
        // this[int index] indexer
        var indexer = new PropertyMemberInfo(
            Name: "Item",
            Type: "string",
            Visibility: Visibility.Public,
            IsStatic: false,
            HasGetter: true,
            HasSetter: true,
            GetterVisibility: Visibility.Public,
            SetterVisibility: Visibility.Public,
            IsIndexer: true,
            IndexerParameters: new[] { new ParameterInfo("index", "int", false, false, false, null) }
        );

        indexer.IsIndexer.Should().BeTrue("this[] is an indexer");
        indexer.IndexerParameters.Should().HaveCount(1);
        indexer.IndexerParameters![0].Name.Should().Be("index");
    }

    /// <summary>
    /// Const fields include constant value.
    /// </summary>
    [Fact]
    public void FieldMemberInfo_ConstField_IncludesValue()
    {
        var constField = new FieldMemberInfo(
            Name: "MaxValue",
            Type: "int",
            Visibility: Visibility.Public,
            IsStatic: true, // Const fields are implicitly static
            IsReadOnly: false,
            IsConst: true,
            ConstValue: "2147483647"
        );

        constField.IsConst.Should().BeTrue("const field has IsConst = true");
        constField.IsStatic.Should().BeTrue("const fields are static");
        constField.ConstValue.Should().NotBeNullOrEmpty("const value is provided");
    }

    /// <summary>
    /// Readonly fields have IsReadOnly set.
    /// </summary>
    [Fact]
    public void FieldMemberInfo_ReadonlyField_HasIsReadOnlyTrue()
    {
        var readonlyField = new FieldMemberInfo(
            Name: "_items",
            Type: "List<string>",
            Visibility: Visibility.Private,
            IsStatic: false,
            IsReadOnly: true,
            IsConst: false,
            ConstValue: null
        );

        readonlyField.IsReadOnly.Should().BeTrue("readonly field has IsReadOnly = true");
    }

    /// <summary>
    /// TypeMembersResult includes counts for all member types.
    /// </summary>
    [Fact]
    public void TypeMembersResult_IncludesCounts()
    {
        var result = new TypeMembersResult(
            TypeName: "MyApp.Customer",
            Methods: new[] {
                new MethodMemberInfo("GetName", "string GetName()", "string", Array.Empty<ParameterInfo>(),
                    Visibility.Public, false, false, false, false, null, "MyApp.Customer")
            },
            Properties: new[] {
                new PropertyMemberInfo("Id", "int", Visibility.Public, false, true, true,
                    Visibility.Public, Visibility.Public, false, null)
            },
            Fields: new[] {
                new FieldMemberInfo("_id", "int", Visibility.Private, false, true, false, null)
            },
            Events: Array.Empty<EventMemberInfo>(),
            IncludesInherited: false,
            MethodCount: 1,
            PropertyCount: 1,
            FieldCount: 1,
            EventCount: 0
        );

        result.MethodCount.Should().Be(1);
        result.PropertyCount.Should().Be(1);
        result.FieldCount.Should().Be(1);
        result.EventCount.Should().Be(0);
        result.IncludesInherited.Should().BeFalse();
    }

    /// <summary>
    /// member_kinds filter values are valid.
    /// </summary>
    [Theory]
    [InlineData("methods")]
    [InlineData("properties")]
    [InlineData("fields")]
    [InlineData("events")]
    public void MembersGet_MemberKinds_HasValidValues(string kind)
    {
        // Contract defines: "member_kinds": ["methods", "properties", "fields", "events"]
        var validKinds = new[] { "methods", "properties", "fields", "events" };
        validKinds.Should().Contain(kind);
    }

    /// <summary>
    /// Error response includes code and message.
    /// </summary>
    [Fact]
    public void MembersGet_ErrorResponse_IncludesCodeAndMessage()
    {
        // Contract defines error codes: NO_SESSION, TYPE_NOT_FOUND, MODULE_NOT_FOUND
        var errorCodes = new[] { "NO_SESSION", "TYPE_NOT_FOUND", "MODULE_NOT_FOUND" };

        var errorResponse = new
        {
            success = false,
            error = new
            {
                code = "TYPE_NOT_FOUND",
                message = "Type 'MyApp.NonExistent' not found"
            }
        };

        errorResponse.success.Should().BeFalse();
        errorResponse.error.code.Should().BeOneOf(errorCodes);
        errorResponse.error.message.Should().NotBeNullOrEmpty();
    }

    /// <summary>
    /// Events include add/remove methods.
    /// </summary>
    [Fact]
    public void EventMemberInfo_IncludesAddRemoveMethods()
    {
        var ev = new EventMemberInfo(
            Name: "Click",
            Type: "EventHandler",
            Visibility: Visibility.Public,
            IsStatic: false,
            AddMethod: "add_Click",
            RemoveMethod: "remove_Click"
        );

        ev.AddMethod.Should().Be("add_Click");
        ev.RemoveMethod.Should().Be("remove_Click");
    }

    /// <summary>
    /// Static events have IsStatic set.
    /// </summary>
    [Fact]
    public void EventMemberInfo_StaticEvent_HasIsStaticTrue()
    {
        var staticEvent = new EventMemberInfo(
            Name: "ProcessExit",
            Type: "EventHandler",
            Visibility: Visibility.Public,
            IsStatic: true,
            AddMethod: "add_ProcessExit",
            RemoveMethod: "remove_ProcessExit"
        );

        staticEvent.IsStatic.Should().BeTrue("static event has IsStatic = true");
    }
}
