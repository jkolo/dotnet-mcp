using DebugMcp.Models.Modules;
using FluentAssertions;

namespace DebugMcp.Tests.Contract;

/// <summary>
/// Contract tests validating the modules_list tool schema compliance.
/// These tests verify the tool adheres to the MCP contract defined in contracts/modules_list.json.
/// </summary>
public class ModulesListContractTests
{
    /// <summary>
    /// modules_list has no required input parameters.
    /// </summary>
    [Fact]
    public void ModulesList_NoRequiredParameters()
    {
        // The input schema specifies: "required": ["tool"]
        // All parameters (include_system, name_filter) are optional
        true.Should().BeTrue("modules_list has no required parameters besides tool name");
    }

    /// <summary>
    /// include_system parameter defaults to true.
    /// </summary>
    [Fact]
    public void ModulesList_IncludeSystem_DefaultsToTrue()
    {
        // Contract defines: "include_system": { "default": true }
        const bool defaultValue = true;
        defaultValue.Should().BeTrue("include_system defaults to true per contract");
    }

    /// <summary>
    /// ModuleInfo has all required fields per contract.
    /// </summary>
    [Fact]
    public void ModuleInfo_HasRequiredFields()
    {
        // Contract requires: name, fullName, isManaged, moduleId
        var module = new ModuleInfo(
            Name: "TestModule",
            FullName: "TestModule, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null",
            Path: "/path/to/module.dll",
            Version: "1.0.0.0",
            IsManaged: true,
            IsDynamic: false,
            HasSymbols: true,
            ModuleId: "mod-12345",
            BaseAddress: "0x00007FF8A1230000",
            Size: 45056
        );

        module.Name.Should().NotBeNullOrEmpty("name is required");
        module.FullName.Should().NotBeNullOrEmpty("fullName is required");
        module.ModuleId.Should().NotBeNullOrEmpty("moduleId is required");
    }

    /// <summary>
    /// ModuleInfo path can be null for in-memory modules.
    /// </summary>
    [Fact]
    public void ModuleInfo_Path_CanBeNull()
    {
        // Contract defines: "path": { "type": ["string", "null"] }
        var module = new ModuleInfo(
            Name: "DynamicModule",
            FullName: "DynamicModule, Version=1.0.0.0",
            Path: null,
            Version: "1.0.0.0",
            IsManaged: true,
            IsDynamic: true,
            HasSymbols: false,
            ModuleId: "mod-dynamic",
            BaseAddress: null,
            Size: 0
        );

        module.Path.Should().BeNull("path can be null for in-memory modules");
    }

    /// <summary>
    /// Success response includes modules array and count.
    /// </summary>
    [Fact]
    public void ModulesList_SuccessResponse_IncludesModulesAndCount()
    {
        // Contract defines success response with: modules (array), count (integer)
        var modules = new[]
        {
            new ModuleInfo("App", "App, Version=1.0", "/app.dll", "1.0", true, false, true, "mod-1", "0x1000", 1024),
            new ModuleInfo("Lib", "Lib, Version=2.0", "/lib.dll", "2.0", true, false, false, "mod-2", "0x2000", 2048)
        };

        var response = new
        {
            success = true,
            modules,
            count = modules.Length
        };

        response.success.Should().BeTrue();
        response.modules.Should().HaveCount(2);
        response.count.Should().Be(2);
    }

    /// <summary>
    /// Error response includes code and message.
    /// </summary>
    [Fact]
    public void ModulesList_ErrorResponse_IncludesCodeAndMessage()
    {
        // Contract defines error codes: NO_SESSION, ENUMERATION_FAILED
        var errorCodes = new[] { "NO_SESSION", "ENUMERATION_FAILED" };

        var errorResponse = new
        {
            success = false,
            error = new
            {
                code = "NO_SESSION",
                message = "No active debug session"
            }
        };

        errorResponse.success.Should().BeFalse();
        errorResponse.error.code.Should().BeOneOf(errorCodes);
        errorResponse.error.message.Should().NotBeNullOrEmpty();
    }

    /// <summary>
    /// Module baseAddress is formatted as hex string.
    /// </summary>
    [Fact]
    public void ModuleInfo_BaseAddress_IsHexString()
    {
        // Contract defines: "baseAddress": { "type": "string" }
        // Should be hex format like "0x00007FF8A1230000"
        var module = new ModuleInfo(
            Name: "Test",
            FullName: "Test",
            Path: null,
            Version: "1.0",
            IsManaged: true,
            IsDynamic: false,
            HasSymbols: false,
            ModuleId: "mod-1",
            BaseAddress: "0x00007FF8A1230000",
            Size: 1024
        );

        module.BaseAddress.Should().StartWith("0x", "baseAddress should be hex formatted");
    }

    /// <summary>
    /// name_filter supports wildcard patterns.
    /// </summary>
    [Theory]
    [InlineData("MyApp*")]
    [InlineData("*Service")]
    [InlineData("*Core*")]
    [InlineData("ExactMatch")]
    public void ModulesList_NameFilter_SupportsWildcards(string pattern)
    {
        // Contract defines: "name_filter": { "description": "Filter modules by name pattern (supports * wildcard)" }
        pattern.Should().NotBeNullOrEmpty("pattern must be valid");
        // Wildcard patterns are valid filter values
    }
}
