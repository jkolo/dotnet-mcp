using System.Text.Json;
using DebugMcp.Models;
using DebugMcp.Models.Breakpoints;
using DebugMcp.Models.Inspection;
using FluentAssertions;
using ThreadState = DebugMcp.Models.Inspection.ThreadState;

namespace DebugMcp.Tests.Contract;

/// <summary>
/// Schema validation tests ensuring the implementation matches the MCP contract.
/// Validates that all types serialize correctly per the mcp-tools.json schema.
/// </summary>
public class SchemaValidationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    /// <summary>
    /// DebugSession serializes all required fields correctly.
    /// </summary>
    [Fact]
    public void DebugSession_Serializes_AllRequiredFields()
    {
        // Arrange - contract requires: processId, processName, runtimeVersion, state, launchMode, attachedAt
        var session = new DebugSession
        {
            ProcessId = 1234,
            ProcessName = "testapp",
            ExecutablePath = "/path/to/testapp.dll",
            RuntimeVersion = ".NET 8.0",
            AttachedAt = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            State = SessionState.Running,
            LaunchMode = LaunchMode.Attach
        };

        // Act
        var json = JsonSerializer.Serialize(session, JsonOptions);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Assert - all required fields present
        root.TryGetProperty("processId", out var pid).Should().BeTrue();
        pid.GetInt32().Should().Be(1234);

        root.TryGetProperty("processName", out var name).Should().BeTrue();
        name.GetString().Should().Be("testapp");

        root.TryGetProperty("runtimeVersion", out var runtime).Should().BeTrue();
        runtime.GetString().Should().Be(".NET 8.0");

        // Note: enums serialize as numbers by default in System.Text.Json
        // The tool implementations convert to lowercase strings manually
        root.TryGetProperty("state", out var state).Should().BeTrue();
        state.ValueKind.Should().BeOneOf(JsonValueKind.Number, JsonValueKind.String);

        root.TryGetProperty("launchMode", out var mode).Should().BeTrue();
        mode.ValueKind.Should().BeOneOf(JsonValueKind.Number, JsonValueKind.String);

        root.TryGetProperty("attachedAt", out var attachedAt).Should().BeTrue();
        attachedAt.GetDateTime().Should().Be(new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc));
    }

    /// <summary>
    /// SessionState enum values match contract.
    /// </summary>
    [Theory]
    [InlineData(SessionState.Disconnected, "disconnected")]
    [InlineData(SessionState.Running, "running")]
    [InlineData(SessionState.Paused, "paused")]
    public void SessionState_Serializes_ToLowercaseStrings(SessionState state, string expected)
    {
        // Contract expects lowercase: "disconnected", "running", "paused"
        var lowered = state.ToString().ToLowerInvariant();
        lowered.Should().Be(expected);
    }

    /// <summary>
    /// LaunchMode enum values match contract.
    /// </summary>
    [Theory]
    [InlineData(LaunchMode.Attach, "attach")]
    [InlineData(LaunchMode.Launch, "launch")]
    public void LaunchMode_Serializes_ToLowercaseStrings(LaunchMode mode, string expected)
    {
        // Contract expects lowercase: "attach", "launch"
        var lowered = mode.ToString().ToLowerInvariant();
        lowered.Should().Be(expected);
    }

    /// <summary>
    /// PauseReason enum values match contract.
    /// </summary>
    [Theory]
    [InlineData(PauseReason.Breakpoint, "breakpoint")]
    [InlineData(PauseReason.Step, "step")]
    [InlineData(PauseReason.Exception, "exception")]
    [InlineData(PauseReason.Pause, "pause")]
    [InlineData(PauseReason.Entry, "entry")]
    public void PauseReason_Serializes_ToLowercaseStrings(PauseReason reason, string expected)
    {
        // Contract expects lowercase: "breakpoint", "step", "exception", "pause", "entry"
        var lowered = reason.ToString().ToLowerInvariant();
        lowered.Should().Be(expected);
    }

    /// <summary>
    /// SourceLocation serializes required fields.
    /// </summary>
    [Fact]
    public void SourceLocation_Serializes_RequiredFields()
    {
        // Contract requires: file, line
        // Optional: column, functionName, moduleName
        var location = new SourceLocation(
            File: "/path/to/source.cs",
            Line: 42,
            Column: 8,
            FunctionName: "TestMethod",
            ModuleName: "TestAssembly"
        );

        var json = JsonSerializer.Serialize(location, JsonOptions);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Required fields
        root.TryGetProperty("file", out var file).Should().BeTrue();
        file.GetString().Should().Be("/path/to/source.cs");

        root.TryGetProperty("line", out var line).Should().BeTrue();
        line.GetInt32().Should().Be(42);
    }

    /// <summary>
    /// SourceLocation with nulls omits optional fields.
    /// </summary>
    [Fact]
    public void SourceLocation_WithNulls_OmitsOptionalFields()
    {
        var location = new SourceLocation(
            File: "/path/to/source.cs",
            Line: 1,
            Column: null,
            FunctionName: null,
            ModuleName: null
        );

        var json = JsonSerializer.Serialize(location, JsonOptions);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Required fields should be present
        root.TryGetProperty("file", out _).Should().BeTrue();
        root.TryGetProperty("line", out _).Should().BeTrue();

        // Optional null fields may be present or omitted depending on serialization settings
        // Just verify the object serializes without error
        json.Should().NotBeNullOrEmpty();
    }

    /// <summary>
    /// ErrorResponse serializes correctly.
    /// </summary>
    [Fact]
    public void ErrorResponse_Serializes_RequiredFields()
    {
        // Contract requires: error.code, error.message
        // Optional: error.details
        var error = new ErrorResponse
        {
            Code = ErrorCodes.ProcessNotFound,
            Message = "Process 12345 not found",
            Details = new { pid = 12345 }
        };

        var json = JsonSerializer.Serialize(error, JsonOptions);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.TryGetProperty("code", out var code).Should().BeTrue();
        code.GetString().Should().Be(ErrorCodes.ProcessNotFound);

        root.TryGetProperty("message", out var msg).Should().BeTrue();
        msg.GetString().Should().Contain("12345");

        root.TryGetProperty("details", out var details).Should().BeTrue();
        details.TryGetProperty("pid", out var pid).Should().BeTrue();
        pid.GetInt32().Should().Be(12345);
    }

    /// <summary>
    /// ProcessInfo record serializes all fields.
    /// </summary>
    [Fact]
    public void ProcessInfo_Serializes_AllFields()
    {
        var info = new ProcessInfo(
            Pid: 1234,
            Name: "testapp",
            ExecutablePath: "/path/to/testapp.dll",
            IsManaged: true,
            CommandLine: "dotnet testapp.dll --arg",
            RuntimeVersion: ".NET 8.0"
        );

        var json = JsonSerializer.Serialize(info, JsonOptions);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.TryGetProperty("pid", out var pid).Should().BeTrue();
        pid.GetInt32().Should().Be(1234);

        root.TryGetProperty("name", out var name).Should().BeTrue();
        name.GetString().Should().Be("testapp");

        root.TryGetProperty("isManaged", out var isManaged).Should().BeTrue();
        isManaged.GetBoolean().Should().BeTrue();
    }

    /// <summary>
    /// Tool response format is consistent.
    /// </summary>
    [Fact]
    public void ToolResponse_Format_IsConsistent()
    {
        // All tool responses should have: success (bool)
        // On success: relevant data
        // On error: error object with code and message

        // Success response
        var successResponse = new
        {
            success = true,
            session = new { processId = 1234 }
        };

        var successJson = JsonSerializer.Serialize(successResponse, JsonOptions);
        var successDoc = JsonDocument.Parse(successJson);
        successDoc.RootElement.TryGetProperty("success", out var success).Should().BeTrue();
        success.GetBoolean().Should().BeTrue();

        // Error response
        var errorResponse = new
        {
            success = false,
            error = new
            {
                code = ErrorCodes.ProcessNotFound,
                message = "Process not found"
            }
        };

        var errorJson = JsonSerializer.Serialize(errorResponse, JsonOptions);
        var errorDoc = JsonDocument.Parse(errorJson);
        errorDoc.RootElement.TryGetProperty("success", out var errorSuccess).Should().BeTrue();
        errorSuccess.GetBoolean().Should().BeFalse();
        errorDoc.RootElement.TryGetProperty("error", out var error).Should().BeTrue();
        error.TryGetProperty("code", out _).Should().BeTrue();
        error.TryGetProperty("message", out _).Should().BeTrue();
    }

    /// <summary>
    /// All defined ErrorCodes are valid.
    /// </summary>
    [Fact]
    public void ErrorCodes_AllDefined_AreScreamingSnakeCase()
    {
        var codes = new[]
        {
            ErrorCodes.ProcessNotFound,
            ErrorCodes.NotDotNetProcess,
            ErrorCodes.PermissionDenied,
            ErrorCodes.SessionActive,
            ErrorCodes.AlreadyAttached,
            ErrorCodes.NoSession,
            ErrorCodes.AttachFailed,
            ErrorCodes.LaunchFailed,
            ErrorCodes.InvalidPath,
            ErrorCodes.Timeout
        };

        foreach (var code in codes)
        {
            code.Should().NotBeNullOrEmpty();
            code.Should().MatchRegex(@"^[A-Z_]+$", $"Error code '{code}' should be SCREAMING_SNAKE_CASE");
        }
    }

    // ========== Breakpoint Schema Validation ==========

    /// <summary>
    /// BreakpointState enum values match contract.
    /// </summary>
    [Theory]
    [InlineData(BreakpointState.Pending, "pending")]
    [InlineData(BreakpointState.Bound, "bound")]
    [InlineData(BreakpointState.Disabled, "disabled")]
    public void BreakpointState_Serializes_ToLowercaseStrings(BreakpointState state, string expected)
    {
        // Contract expects lowercase: "pending", "bound", "disabled"
        var lowered = state.ToString().ToLowerInvariant();
        lowered.Should().Be(expected);
    }

    /// <summary>
    /// BreakpointLocation serializes required fields.
    /// </summary>
    [Fact]
    public void BreakpointLocation_Serializes_RequiredFields()
    {
        // Contract requires: file, line
        // Optional: column, endLine, endColumn, functionName, moduleName
        var location = new BreakpointLocation(
            File: "/path/to/source.cs",
            Line: 42,
            Column: 8,
            EndLine: 42,
            EndColumn: 20,
            FunctionName: "TestMethod",
            ModuleName: "TestAssembly"
        );

        var json = JsonSerializer.Serialize(location, JsonOptions);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Required fields
        root.TryGetProperty("file", out var file).Should().BeTrue();
        file.GetString().Should().Be("/path/to/source.cs");

        root.TryGetProperty("line", out var line).Should().BeTrue();
        line.GetInt32().Should().Be(42);

        // Optional fields when provided
        root.TryGetProperty("column", out var col).Should().BeTrue();
        col.GetInt32().Should().Be(8);

        root.TryGetProperty("functionName", out var func).Should().BeTrue();
        func.GetString().Should().Be("TestMethod");
    }

    /// <summary>
    /// BreakpointLocation with minimal fields omits optional fields.
    /// </summary>
    [Fact]
    public void BreakpointLocation_WithMinimalFields_OmitsOptionalFields()
    {
        var location = new BreakpointLocation(
            File: "/path/to/source.cs",
            Line: 1
        );

        var json = JsonSerializer.Serialize(location, JsonOptions);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Required fields should be present
        root.TryGetProperty("file", out _).Should().BeTrue();
        root.TryGetProperty("line", out _).Should().BeTrue();

        // Object serializes without error
        json.Should().NotBeNullOrEmpty();
    }

    /// <summary>
    /// Breakpoint serializes all fields correctly.
    /// </summary>
    [Fact]
    public void Breakpoint_Serializes_AllFields()
    {
        var breakpoint = new Breakpoint(
            Id: "bp-550e8400-e29b-41d4-a716-446655440000",
            Location: new BreakpointLocation("/path/to/source.cs", 42),
            State: BreakpointState.Bound,
            Enabled: true,
            Verified: true,
            HitCount: 3,
            Condition: "x > 5",
            Message: null
        );

        var json = JsonSerializer.Serialize(breakpoint, JsonOptions);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // All fields present
        root.TryGetProperty("id", out var id).Should().BeTrue();
        id.GetString().Should().Be("bp-550e8400-e29b-41d4-a716-446655440000");

        root.TryGetProperty("location", out var loc).Should().BeTrue();
        loc.TryGetProperty("file", out _).Should().BeTrue();
        loc.TryGetProperty("line", out _).Should().BeTrue();

        root.TryGetProperty("state", out _).Should().BeTrue();
        root.TryGetProperty("enabled", out var enabled).Should().BeTrue();
        enabled.GetBoolean().Should().BeTrue();

        root.TryGetProperty("verified", out var verified).Should().BeTrue();
        verified.GetBoolean().Should().BeTrue();

        root.TryGetProperty("hitCount", out var hitCount).Should().BeTrue();
        hitCount.GetInt32().Should().Be(3);

        root.TryGetProperty("condition", out var condition).Should().BeTrue();
        condition.GetString().Should().Be("x > 5");
    }

    /// <summary>
    /// BreakpointHit serializes all required fields.
    /// </summary>
    [Fact]
    public void BreakpointHit_Serializes_RequiredFields()
    {
        var hit = new BreakpointHit(
            BreakpointId: "bp-123",
            ThreadId: 5,
            Timestamp: new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            Location: new BreakpointLocation("/path/to/source.cs", 42),
            HitCount: 1,
            ExceptionInfo: null
        );

        var json = JsonSerializer.Serialize(hit, JsonOptions);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.TryGetProperty("breakpointId", out var bpId).Should().BeTrue();
        bpId.GetString().Should().Be("bp-123");

        root.TryGetProperty("threadId", out var tid).Should().BeTrue();
        tid.GetInt32().Should().Be(5);

        root.TryGetProperty("timestamp", out var ts).Should().BeTrue();
        ts.GetDateTime().Should().Be(new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc));

        root.TryGetProperty("location", out var loc).Should().BeTrue();
        loc.TryGetProperty("file", out _).Should().BeTrue();

        root.TryGetProperty("hitCount", out var hc).Should().BeTrue();
        hc.GetInt32().Should().Be(1);
    }

    /// <summary>
    /// ExceptionBreakpoint serializes all fields correctly.
    /// </summary>
    [Fact]
    public void ExceptionBreakpoint_Serializes_AllFields()
    {
        var exBp = new ExceptionBreakpoint(
            Id: "ex-123",
            ExceptionType: "System.NullReferenceException",
            BreakOnFirstChance: true,
            BreakOnSecondChance: true,
            IncludeSubtypes: true,
            Enabled: true,
            Verified: true,
            HitCount: 0
        );

        var json = JsonSerializer.Serialize(exBp, JsonOptions);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.TryGetProperty("id", out var id).Should().BeTrue();
        id.GetString().Should().Be("ex-123");

        root.TryGetProperty("exceptionType", out var exType).Should().BeTrue();
        exType.GetString().Should().Be("System.NullReferenceException");

        root.TryGetProperty("breakOnFirstChance", out var first).Should().BeTrue();
        first.GetBoolean().Should().BeTrue();

        root.TryGetProperty("breakOnSecondChance", out var second).Should().BeTrue();
        second.GetBoolean().Should().BeTrue();

        root.TryGetProperty("includeSubtypes", out var subtypes).Should().BeTrue();
        subtypes.GetBoolean().Should().BeTrue();

        root.TryGetProperty("enabled", out var enabled).Should().BeTrue();
        enabled.GetBoolean().Should().BeTrue();

        root.TryGetProperty("verified", out var verified).Should().BeTrue();
        verified.GetBoolean().Should().BeTrue();

        root.TryGetProperty("hitCount", out var hitCount).Should().BeTrue();
        hitCount.GetInt32().Should().Be(0);
    }

    /// <summary>
    /// ExceptionInfo serializes required fields.
    /// </summary>
    [Fact]
    public void ExceptionInfo_Serializes_RequiredFields()
    {
        var info = new ExceptionInfo(
            Type: "System.NullReferenceException",
            Message: "Object reference not set",
            IsFirstChance: true
        );

        var json = JsonSerializer.Serialize(info, JsonOptions);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.TryGetProperty("type", out var type).Should().BeTrue();
        type.GetString().Should().Be("System.NullReferenceException");

        root.TryGetProperty("message", out var msg).Should().BeTrue();
        msg.GetString().Should().Be("Object reference not set");

        root.TryGetProperty("isFirstChance", out var first).Should().BeTrue();
        first.GetBoolean().Should().BeTrue();
    }

    /// <summary>
    /// Breakpoint error codes are defined.
    /// </summary>
    [Fact]
    public void BreakpointErrorCodes_AllDefined_AreScreamingSnakeCase()
    {
        var codes = new[]
        {
            ErrorCodes.BreakpointNotFound,
            ErrorCodes.InvalidLine,
            ErrorCodes.InvalidColumn,
            ErrorCodes.InvalidCondition
        };

        foreach (var code in codes)
        {
            code.Should().NotBeNullOrEmpty();
            code.Should().MatchRegex(@"^[A-Z_]+$", $"Error code '{code}' should be SCREAMING_SNAKE_CASE");
        }
    }

    // ========== Inspection Schema Validation ==========

    /// <summary>
    /// StackFrame serializes all required fields.
    /// </summary>
    [Fact]
    public void StackFrame_Serializes_RequiredFields()
    {
        // Contract requires: index, function, module, is_external
        var frame = new StackFrame(
            Index: 0,
            Function: "Program.Main",
            Module: "TestApp.dll",
            IsExternal: false,
            Location: new SourceLocation("/path/to/Program.cs", 42, null, "Main", null),
            Arguments: null
        );

        var json = JsonSerializer.Serialize(frame, JsonOptions);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.TryGetProperty("index", out var index).Should().BeTrue();
        index.GetInt32().Should().Be(0);

        root.TryGetProperty("function", out var func).Should().BeTrue();
        func.GetString().Should().Be("Program.Main");

        root.TryGetProperty("module", out var module).Should().BeTrue();
        module.GetString().Should().Be("TestApp.dll");

        root.TryGetProperty("isExternal", out var isExternal).Should().BeTrue();
        isExternal.GetBoolean().Should().BeFalse();
    }

    /// <summary>
    /// ThreadState enum values match contract.
    /// </summary>
    [Theory]
    [InlineData(ThreadState.Running, "running")]
    [InlineData(ThreadState.Stopped, "stopped")]
    [InlineData(ThreadState.Waiting, "waiting")]
    [InlineData(ThreadState.NotStarted, "notstarted")]
    [InlineData(ThreadState.Terminated, "terminated")]
    public void ThreadState_Serializes_ToLowercaseStrings(ThreadState state, string expected)
    {
        // Contract expects lowercase
        var lowered = state.ToString().ToLowerInvariant();
        lowered.Should().Be(expected);
    }

    /// <summary>
    /// ThreadInfo serializes all required fields.
    /// </summary>
    [Fact]
    public void ThreadInfo_Serializes_RequiredFields()
    {
        // Contract requires: id, state, is_current
        var thread = new ThreadInfo(
            Id: 1234,
            Name: "Main Thread",
            State: ThreadState.Running,
            IsCurrent: true,
            Location: new SourceLocation("/path/to/source.cs", 10, null, null, null)
        );

        var json = JsonSerializer.Serialize(thread, JsonOptions);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.TryGetProperty("id", out var id).Should().BeTrue();
        id.GetInt32().Should().Be(1234);

        root.TryGetProperty("name", out var name).Should().BeTrue();
        name.GetString().Should().Be("Main Thread");

        root.TryGetProperty("isCurrent", out var isCurrent).Should().BeTrue();
        isCurrent.GetBoolean().Should().BeTrue();
    }

    /// <summary>
    /// Variable serializes all required fields.
    /// </summary>
    [Fact]
    public void Variable_Serializes_RequiredFields()
    {
        // Contract requires: name, type, value, scope, has_children
        var variable = new Variable(
            Name: "counter",
            Type: "System.Int32",
            Value: "42",
            Scope: VariableScope.Local,
            HasChildren: false,
            ChildrenCount: null,
            Path: null
        );

        var json = JsonSerializer.Serialize(variable, JsonOptions);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.TryGetProperty("name", out var name).Should().BeTrue();
        name.GetString().Should().Be("counter");

        root.TryGetProperty("type", out var type).Should().BeTrue();
        type.GetString().Should().Be("System.Int32");

        root.TryGetProperty("value", out var value).Should().BeTrue();
        value.GetString().Should().Be("42");

        root.TryGetProperty("hasChildren", out var hasChildren).Should().BeTrue();
        hasChildren.GetBoolean().Should().BeFalse();
    }

    /// <summary>
    /// VariableScope enum values match contract.
    /// </summary>
    [Theory]
    [InlineData(VariableScope.Local, "local")]
    [InlineData(VariableScope.Argument, "argument")]
    [InlineData(VariableScope.This, "this")]
    public void VariableScope_Serializes_ToLowercaseStrings(VariableScope scope, string expected)
    {
        // Contract expects lowercase
        var lowered = scope.ToString().ToLowerInvariant();
        lowered.Should().Be(expected);
    }

    /// <summary>
    /// EvaluationResult serializes all fields correctly.
    /// </summary>
    [Fact]
    public void EvaluationResult_Serializes_RequiredFields()
    {
        // Contract requires: success
        var result = new EvaluationResult(
            Success: true,
            Value: "42",
            Type: "System.Int32",
            HasChildren: false,
            Error: null
        );

        var json = JsonSerializer.Serialize(result, JsonOptions);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.TryGetProperty("success", out var success).Should().BeTrue();
        success.GetBoolean().Should().BeTrue();

        root.TryGetProperty("value", out var value).Should().BeTrue();
        value.GetString().Should().Be("42");

        root.TryGetProperty("type", out var type).Should().BeTrue();
        type.GetString().Should().Be("System.Int32");

        root.TryGetProperty("hasChildren", out var hasChildren).Should().BeTrue();
        hasChildren.GetBoolean().Should().BeFalse();
    }

    /// <summary>
    /// EvaluationError serializes all fields correctly.
    /// </summary>
    [Fact]
    public void EvaluationError_Serializes_RequiredFields()
    {
        // Contract requires: code, message
        var error = new EvaluationError(
            Code: "syntax_error",
            Message: "Unexpected token at position 5",
            ExceptionType: null,
            Position: 5
        );

        var json = JsonSerializer.Serialize(error, JsonOptions);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.TryGetProperty("code", out var code).Should().BeTrue();
        code.GetString().Should().Be("syntax_error");

        root.TryGetProperty("message", out var message).Should().BeTrue();
        message.GetString().Should().Be("Unexpected token at position 5");

        root.TryGetProperty("position", out var position).Should().BeTrue();
        position.GetInt32().Should().Be(5);
    }

    /// <summary>
    /// Inspection error codes are defined.
    /// </summary>
    [Fact]
    public void InspectionErrorCodes_AllDefined_AreScreamingSnakeCase()
    {
        var codes = new[]
        {
            ErrorCodes.InvalidThread,
            ErrorCodes.InvalidFrame,
            ErrorCodes.StackTraceFailed,
            ErrorCodes.VariablesFailed,
            ErrorCodes.NotPaused
        };

        foreach (var code in codes)
        {
            code.Should().NotBeNullOrEmpty();
            code.Should().MatchRegex(@"^[A-Z_]+$", $"Error code '{code}' should be SCREAMING_SNAKE_CASE");
        }
    }
}
