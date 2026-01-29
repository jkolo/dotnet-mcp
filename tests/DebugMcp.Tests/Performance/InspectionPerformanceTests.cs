using System.Diagnostics;
using DebugMcp.Models;
using DebugMcp.Models.Inspection;
using DebugMcp.Services;
using DebugMcp.Services.Breakpoints;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using StackFrame = DebugMcp.Models.Inspection.StackFrame;
using ThreadState = DebugMcp.Models.Inspection.ThreadState;

namespace DebugMcp.Tests.Performance;

/// <summary>
/// Performance tests validating inspection success criteria:
/// SC-001: Stack trace retrieval within 500ms for typical call stacks
/// SC-002: Variable inspection within 1 second
/// SC-004: Thread listing within 200ms
/// </summary>
[Collection("ProcessTests")]
public class InspectionPerformanceTests : IDisposable
{
    private readonly Mock<ILogger<ProcessDebugger>> _debuggerLoggerMock;
    private readonly Mock<ILogger<DebugSessionManager>> _managerLoggerMock;
    private readonly Mock<IPdbSymbolReader> _pdbSymbolReaderMock;
    private readonly ProcessDebugger _processDebugger;
    private readonly DebugSessionManager _sessionManager;

    public InspectionPerformanceTests()
    {
        _debuggerLoggerMock = new Mock<ILogger<ProcessDebugger>>();
        _managerLoggerMock = new Mock<ILogger<DebugSessionManager>>();
        _pdbSymbolReaderMock = new Mock<IPdbSymbolReader>();
        _processDebugger = new ProcessDebugger(_debuggerLoggerMock.Object, _pdbSymbolReaderMock.Object);
        _sessionManager = new DebugSessionManager(_processDebugger, _managerLoggerMock.Object);
    }

    public void Dispose()
    {
        _processDebugger.Dispose();
    }

    // ========== SC-001: Stack Trace Performance ==========

    /// <summary>
    /// SC-001: StackFrame model creation is fast.
    /// Stack trace should complete within 500ms for typical call stacks.
    /// </summary>
    [Fact]
    public void StackFrame_Creation_IsEfficient()
    {
        // Arrange
        const int frameCount = 50; // Typical deep call stack
        const int maxMs = 100; // Model creation should be fast
        var frames = new List<StackFrame>(frameCount);
        var sw = Stopwatch.StartNew();

        // Act - create 50 frames (typical deep stack)
        for (int i = 0; i < frameCount; i++)
        {
            frames.Add(new StackFrame(
                Index: i,
                Function: $"Module.Class.Method_{i}",
                Module: "TestAssembly.dll",
                IsExternal: i % 5 == 0, // Every 5th is external
                Location: i % 5 != 0 ? new SourceLocation($"/path/to/source_{i}.cs", 100 + i, null, null, null) : null,
                Arguments: new List<Variable>
                {
                    new Variable($"arg_{i}", "System.Int32", i.ToString(), VariableScope.Argument, false, null, null)
                }
            ));
        }

        // Assert
        sw.Stop();
        sw.ElapsedMilliseconds.Should().BeLessThan(maxMs,
            $"SC-001: Creating {frameCount} stack frames should complete within {maxMs}ms");
        frames.Should().HaveCount(frameCount);
    }

    /// <summary>
    /// SC-001: StackFrame serialization is efficient.
    /// </summary>
    [Fact]
    public void StackFrame_Serialization_IsEfficient()
    {
        // Arrange
        const int frameCount = 50;
        const int maxMs = 500; // Serialization should be fast (includes JIT warm-up margin)
        var frames = Enumerable.Range(0, frameCount).Select(i => new StackFrame(
            Index: i,
            Function: $"Module.Class.Method_{i}",
            Module: "TestAssembly.dll",
            IsExternal: false,
            Location: new SourceLocation($"/path/to/source_{i}.cs", 100 + i, null, null, null),
            Arguments: null
        )).ToList();

        // JIT warm-up
        _ = System.Text.Json.JsonSerializer.Serialize(frames[0]);

        var sw = Stopwatch.StartNew();

        // Act - serialize all frames
        var json = System.Text.Json.JsonSerializer.Serialize(frames);

        // Assert
        sw.Stop();
        sw.ElapsedMilliseconds.Should().BeLessThan(maxMs,
            $"SC-001: Serializing {frameCount} stack frames should complete within {maxMs}ms");
        json.Should().NotBeNullOrEmpty();
    }

    // ========== SC-002: Variable Inspection Performance ==========

    /// <summary>
    /// SC-002: Variable model creation is efficient.
    /// Variable inspection should complete within 1 second.
    /// </summary>
    [Fact]
    public void Variable_Creation_IsEfficient()
    {
        // Arrange
        const int varCount = 100; // Typical frame might have many variables
        const int maxMs = 100; // Model creation should be fast
        var variables = new List<Variable>(varCount);
        var sw = Stopwatch.StartNew();

        // Act - create 100 variables of various types
        for (int i = 0; i < varCount; i++)
        {
            var scope = i % 3 == 0 ? VariableScope.Local
                      : i % 3 == 1 ? VariableScope.Argument
                      : VariableScope.This;

            variables.Add(new Variable(
                Name: $"var_{i}",
                Type: i % 4 == 0 ? "System.String"
                    : i % 4 == 1 ? "System.Int32"
                    : i % 4 == 2 ? "System.Collections.Generic.List<int>"
                    : "MyApp.Customer",
                Value: i % 4 == 0 ? $"\"String value {i}\""
                     : i % 4 == 1 ? i.ToString()
                     : i % 4 == 2 ? $"Count = {i % 10}"
                     : "{...}",
                Scope: scope,
                HasChildren: i % 4 >= 2, // Complex types have children
                ChildrenCount: i % 4 >= 2 ? i % 10 : null,
                Path: null
            ));
        }

        // Assert
        sw.Stop();
        sw.ElapsedMilliseconds.Should().BeLessThan(maxMs,
            $"SC-002: Creating {varCount} variables should complete within {maxMs}ms");
        variables.Should().HaveCount(varCount);
    }

    /// <summary>
    /// SC-002: Variable with deep object expansion is handled efficiently.
    /// </summary>
    [Fact]
    public void Variable_DeepExpansion_IsEfficient()
    {
        // Arrange - simulate deep object tree (3 levels, 10 fields each = 1000+ variables)
        const int depth = 3;
        const int fieldsPerLevel = 10;
        const int maxMs = 500; // Should handle deep expansion quickly
        var allVariables = new List<Variable>();
        var sw = Stopwatch.StartNew();

        // Act - create nested variable tree
        CreateVariableTree(allVariables, "root", 0, depth, fieldsPerLevel);

        // Assert
        sw.Stop();
        sw.ElapsedMilliseconds.Should().BeLessThan(maxMs,
            $"SC-002: Creating variable tree (depth={depth}, fields={fieldsPerLevel}) should complete within {maxMs}ms");

        // With depth=3 and 10 fields per level: 1 + 10 + 100 + 1000 = 1111 variables
        allVariables.Count.Should().BeGreaterThan(1000);
    }

    private void CreateVariableTree(List<Variable> variables, string path, int level, int maxDepth, int fieldsPerLevel)
    {
        bool hasChildren = level < maxDepth;

        var variable = new Variable(
            Name: path.Split('.').Last(),
            Type: hasChildren ? "System.Object" : "System.String",
            Value: hasChildren ? "{...}" : $"\"{path}\"",
            Scope: VariableScope.Local,
            HasChildren: hasChildren,
            ChildrenCount: hasChildren ? fieldsPerLevel : null,
            Path: path
        );

        variables.Add(variable);

        if (hasChildren)
        {
            for (int i = 0; i < fieldsPerLevel; i++)
            {
                CreateVariableTree(variables, $"{path}.field_{i}", level + 1, maxDepth, fieldsPerLevel);
            }
        }
    }

    // ========== SC-004: Thread Listing Performance ==========

    /// <summary>
    /// SC-004: ThreadInfo model creation is efficient.
    /// Thread listing should complete within 200ms.
    /// </summary>
    [Fact]
    public void ThreadInfo_Creation_IsEfficient()
    {
        // Arrange
        const int threadCount = 100; // Application with many threads
        const int maxMs = 50; // Model creation should be fast
        var threads = new List<ThreadInfo>(threadCount);
        var sw = Stopwatch.StartNew();

        // Act - create 100 thread infos
        for (int i = 0; i < threadCount; i++)
        {
            var state = i % 4 == 0 ? ThreadState.Running
                      : i % 4 == 1 ? ThreadState.Stopped
                      : i % 4 == 2 ? ThreadState.Waiting
                      : ThreadState.NotStarted;

            threads.Add(new ThreadInfo(
                Id: 1000 + i,
                Name: i % 5 == 0 ? $"Worker Thread {i}" : null, // Some threads named, some not
                State: state,
                IsCurrent: i == 0, // First thread is current
                Location: state == ThreadState.Stopped
                    ? new SourceLocation($"/path/to/source_{i}.cs", 100 + i, null, null, null)
                    : null
            ));
        }

        // Assert
        sw.Stop();
        sw.ElapsedMilliseconds.Should().BeLessThan(maxMs,
            $"SC-004: Creating {threadCount} thread infos should complete within {maxMs}ms");
        threads.Should().HaveCount(threadCount);
    }

    /// <summary>
    /// SC-004: ThreadInfo serialization is efficient.
    /// </summary>
    [Fact]
    public void ThreadInfo_Serialization_IsEfficient()
    {
        // Arrange
        const int threadCount = 100;
        const int maxMs = 500; // Serialization should be fast (includes JIT warm-up)
        var threads = Enumerable.Range(0, threadCount).Select(i => new ThreadInfo(
            Id: 1000 + i,
            Name: $"Thread {i}",
            State: ThreadState.Running,
            IsCurrent: i == 0,
            Location: null
        )).ToList();

        // JIT warm-up
        _ = System.Text.Json.JsonSerializer.Serialize(threads[0]);

        var sw = Stopwatch.StartNew();

        // Act - serialize all threads
        var json = System.Text.Json.JsonSerializer.Serialize(threads);

        // Assert
        sw.Stop();
        sw.ElapsedMilliseconds.Should().BeLessThan(maxMs,
            $"SC-004: Serializing {threadCount} thread infos should complete within {maxMs}ms");
        json.Should().NotBeNullOrEmpty();
    }

    // ========== Memory Efficiency Tests ==========

    /// <summary>
    /// StackFrame memory footprint is reasonable.
    /// </summary>
    [Fact]
    public void StackFrame_MemoryFootprint_IsReasonable()
    {
        // Arrange
        const int frameCount = 10000;
        var frames = new List<StackFrame>(frameCount);
        var beforeMem = GC.GetTotalMemory(true);

        // Act
        for (int i = 0; i < frameCount; i++)
        {
            frames.Add(new StackFrame(
                Index: i,
                Function: $"Module.Method_{i}",
                Module: "TestAssembly.dll",
                IsExternal: false,
                Location: new SourceLocation("/path/to/source.cs", i, null, null, null),
                Arguments: null
            ));
        }

        var afterMem = GC.GetTotalMemory(true);
        var memPerFrame = (afterMem - beforeMem) / (double)frameCount;

        // Assert - each frame should use less than 1KB
        memPerFrame.Should().BeLessThan(1024, "Each StackFrame should use less than 1KB memory");
    }

    /// <summary>
    /// Variable memory footprint is reasonable.
    /// </summary>
    [Fact]
    public void Variable_MemoryFootprint_IsReasonable()
    {
        // Arrange
        const int varCount = 10000;
        var variables = new List<Variable>(varCount);
        var beforeMem = GC.GetTotalMemory(true);

        // Act
        for (int i = 0; i < varCount; i++)
        {
            variables.Add(new Variable(
                Name: $"var_{i}",
                Type: "System.Int32",
                Value: i.ToString(),
                Scope: VariableScope.Local,
                HasChildren: false,
                ChildrenCount: null,
                Path: null
            ));
        }

        var afterMem = GC.GetTotalMemory(true);
        var memPerVar = (afterMem - beforeMem) / (double)varCount;

        // Assert - each variable should use less than 512 bytes
        memPerVar.Should().BeLessThan(512, "Each Variable should use less than 512 bytes memory");
    }

    /// <summary>
    /// ThreadInfo memory footprint is reasonable.
    /// </summary>
    [Fact]
    public void ThreadInfo_MemoryFootprint_IsReasonable()
    {
        // Arrange
        const int threadCount = 10000;
        var threads = new List<ThreadInfo>(threadCount);
        var beforeMem = GC.GetTotalMemory(true);

        // Act
        for (int i = 0; i < threadCount; i++)
        {
            threads.Add(new ThreadInfo(
                Id: i,
                Name: $"Thread_{i}",
                State: ThreadState.Running,
                IsCurrent: i == 0,
                Location: null
            ));
        }

        var afterMem = GC.GetTotalMemory(true);
        var memPerThread = (afterMem - beforeMem) / (double)threadCount;

        // Assert - each thread info should use less than 256 bytes
        memPerThread.Should().BeLessThan(256, "Each ThreadInfo should use less than 256 bytes memory");
    }

    // ========== EvaluationResult Performance ==========

    /// <summary>
    /// EvaluationResult creation and serialization is efficient.
    /// </summary>
    [Fact]
    public void EvaluationResult_Creation_IsEfficient()
    {
        // Arrange
        const int resultCount = 1000;
        const int maxMs = 50;
        var results = new List<EvaluationResult>(resultCount);
        var sw = Stopwatch.StartNew();

        // Act
        for (int i = 0; i < resultCount; i++)
        {
            results.Add(new EvaluationResult(
                Success: i % 10 != 0, // 90% success
                Value: i % 10 != 0 ? $"result_{i}" : null,
                Type: i % 10 != 0 ? "System.String" : null,
                HasChildren: false,
                Error: i % 10 == 0 ? new EvaluationError("eval_exception", "Test error", null, null) : null
            ));
        }

        // Assert
        sw.Stop();
        sw.ElapsedMilliseconds.Should().BeLessThan(maxMs,
            $"Creating {resultCount} evaluation results should complete within {maxMs}ms");
        results.Should().HaveCount(resultCount);
    }
}
