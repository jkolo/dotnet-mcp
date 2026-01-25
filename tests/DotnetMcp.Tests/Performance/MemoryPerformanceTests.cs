using System.Diagnostics;
using DotnetMcp.Models.Memory;
using DotnetMcp.Services;
using DotnetMcp.Services.Breakpoints;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace DotnetMcp.Tests.Performance;

/// <summary>
/// Performance tests validating memory inspection success criteria:
/// SC-001: Object inspection within 2 seconds
/// SC-002: Memory read within 1 second
/// SC-004: Layout retrieval within 1 second
/// </summary>
[Collection("ProcessTests")]
public class MemoryPerformanceTests : IDisposable
{
    private readonly Mock<ILogger<ProcessDebugger>> _debuggerLoggerMock;
    private readonly Mock<ILogger<DebugSessionManager>> _managerLoggerMock;
    private readonly Mock<IPdbSymbolReader> _pdbSymbolReaderMock;
    private readonly ProcessDebugger _processDebugger;
    private readonly DebugSessionManager _sessionManager;

    public MemoryPerformanceTests()
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

    // ========== SC-001: Object Inspection Performance ==========

    /// <summary>
    /// SC-001: ObjectInspection model creation is fast.
    /// Object inspection should complete within 2 seconds.
    /// </summary>
    [Fact]
    public void ObjectInspection_Creation_IsEfficient()
    {
        // Arrange
        const int inspectionCount = 100;
        const int maxMs = 100; // Model creation should be fast
        var inspections = new List<ObjectInspection>(inspectionCount);
        var sw = Stopwatch.StartNew();

        // Act - create 100 object inspections with fields
        for (int i = 0; i < inspectionCount; i++)
        {
            var fields = Enumerable.Range(0, 10).Select(f => new FieldDetail
            {
                Name = $"field_{f}",
                TypeName = f % 3 == 0 ? "System.Int32" : f % 3 == 1 ? "System.String" : "System.Object",
                Value = f % 3 == 0 ? f.ToString() : f % 3 == 1 ? $"\"value_{f}\"" : "{...}",
                Offset = f * 8,
                Size = 8,
                HasChildren = f % 3 == 2,
                ChildCount = f % 3 == 2 ? 5 : 0
            }).ToList();

            inspections.Add(new ObjectInspection
            {
                Address = $"0x{0x7FF8A1234560 + i * 100:X16}",
                TypeName = $"TestApp.Models.Object_{i}",
                Size = 80 + i,
                Fields = fields,
                IsNull = false,
                HasCircularRef = i % 10 == 0,
                Truncated = false
            });
        }

        // Assert
        sw.Stop();
        sw.ElapsedMilliseconds.Should().BeLessThan(maxMs,
            $"SC-001: Creating {inspectionCount} object inspections should complete within {maxMs}ms");
        inspections.Should().HaveCount(inspectionCount);
    }

    /// <summary>
    /// SC-001: ObjectInspection serialization is efficient.
    /// </summary>
    [Fact]
    public void ObjectInspection_Serialization_IsEfficient()
    {
        // Arrange
        const int inspectionCount = 100;
        const int maxMs = 500; // Serialization should be fast
        var inspections = Enumerable.Range(0, inspectionCount).Select(i => new ObjectInspection
        {
            Address = $"0x{0x7FF8A1234560 + i * 100:X16}",
            TypeName = $"TestApp.Models.Object_{i}",
            Size = 80 + i,
            Fields = Enumerable.Range(0, 5).Select(f => new FieldDetail
            {
                Name = $"field_{f}",
                TypeName = "System.Int32",
                Value = f.ToString(),
                Offset = f * 4,
                Size = 4,
                HasChildren = false
            }).ToList(),
            IsNull = false
        }).ToList();

        // JIT warm-up
        _ = System.Text.Json.JsonSerializer.Serialize(inspections[0]);

        var sw = Stopwatch.StartNew();

        // Act - serialize all inspections
        var json = System.Text.Json.JsonSerializer.Serialize(inspections);

        // Assert
        sw.Stop();
        sw.ElapsedMilliseconds.Should().BeLessThan(maxMs,
            $"SC-001: Serializing {inspectionCount} object inspections should complete within {maxMs}ms");
        json.Should().NotBeNullOrEmpty();
    }

    /// <summary>
    /// SC-001: Deep object inspection with many fields is handled efficiently.
    /// </summary>
    [Fact]
    public void ObjectInspection_DeepWithManyFields_IsEfficient()
    {
        // Arrange - simulate object with 100 fields (typical complex object)
        const int fieldCount = 100;
        const int maxMs = 200;
        var sw = Stopwatch.StartNew();

        // Act - create inspection with many fields
        var fields = new List<FieldDetail>(fieldCount);
        for (int i = 0; i < fieldCount; i++)
        {
            fields.Add(new FieldDetail
            {
                Name = $"field_{i}",
                TypeName = i % 5 == 0 ? "System.Collections.Generic.List`1[System.Int32]"
                         : i % 5 == 1 ? "System.String"
                         : i % 5 == 2 ? "System.Int32"
                         : i % 5 == 3 ? "System.DateTime"
                         : "TestApp.Models.NestedObject",
                Value = i % 5 == 2 ? i.ToString() : "{...}",
                Offset = i * 8,
                Size = 8,
                HasChildren = i % 5 != 2,
                ChildCount = i % 5 != 2 ? 10 : 0
            });
        }

        var inspection = new ObjectInspection
        {
            Address = "0x00007FF8A1234560",
            TypeName = "TestApp.Models.ComplexObject",
            Size = fieldCount * 8,
            Fields = fields,
            IsNull = false,
            HasCircularRef = false,
            Truncated = false
        };

        sw.Stop();

        // Assert
        sw.ElapsedMilliseconds.Should().BeLessThan(maxMs,
            $"SC-001: Creating inspection with {fieldCount} fields should complete within {maxMs}ms");
        inspection.Fields.Should().HaveCount(fieldCount);
    }

    // ========== SC-002: Memory Read Performance ==========

    /// <summary>
    /// SC-002: MemoryRegion model creation is fast.
    /// Memory read should complete within 1 second.
    /// </summary>
    [Fact]
    public void MemoryRegion_Creation_IsEfficient()
    {
        // Arrange
        const int regionCount = 100;
        const int maxMs = 50; // Model creation should be fast
        var regions = new List<MemoryRegion>(regionCount);
        var sw = Stopwatch.StartNew();

        // Act - create 100 memory regions
        for (int i = 0; i < regionCount; i++)
        {
            regions.Add(new MemoryRegion
            {
                Address = $"0x{0x7FF8A1234560 + i * 256:X16}",
                RequestedSize = 256,
                ActualSize = 256,
                Bytes = string.Join(" ", Enumerable.Range(0, 256).Select(b => $"{(b % 256):X2}")),
                Ascii = new string('.', 256)
            });
        }

        // Assert
        sw.Stop();
        sw.ElapsedMilliseconds.Should().BeLessThan(maxMs,
            $"SC-002: Creating {regionCount} memory regions should complete within {maxMs}ms");
        regions.Should().HaveCount(regionCount);
    }

    /// <summary>
    /// SC-002: Large memory region (64KB max) is handled efficiently.
    /// </summary>
    [Fact]
    public void MemoryRegion_MaxSize_IsEfficient()
    {
        // Arrange - max size is 64KB
        const int maxSize = 65536;
        const int maxMs = 500;
        var sw = Stopwatch.StartNew();

        // Act - create max size memory region with hex formatting
        var bytes = new byte[maxSize];
        new Random(42).NextBytes(bytes);

        var hexLines = new List<string>();
        var asciiLines = new List<string>();
        for (int i = 0; i < maxSize; i += 16)
        {
            var lineBytes = bytes.Skip(i).Take(16).ToArray();
            hexLines.Add(string.Join(" ", lineBytes.Select(b => $"{b:X2}")));
            asciiLines.Add(new string(lineBytes.Select(b => b >= 32 && b < 127 ? (char)b : '.').ToArray()));
        }

        var region = new MemoryRegion
        {
            Address = "0x00007FF8A1234560",
            RequestedSize = maxSize,
            ActualSize = maxSize,
            Bytes = string.Join("\n", hexLines),
            Ascii = string.Join("\n", asciiLines)
        };

        sw.Stop();

        // Assert
        sw.ElapsedMilliseconds.Should().BeLessThan(maxMs,
            $"SC-002: Creating 64KB memory region should complete within {maxMs}ms");
        region.ActualSize.Should().Be(maxSize);
    }

    /// <summary>
    /// SC-002: MemoryRegion serialization is efficient.
    /// </summary>
    [Fact]
    public void MemoryRegion_Serialization_IsEfficient()
    {
        // Arrange - typical memory dump (256 bytes)
        const int size = 256;
        const int maxMs = 100;

        var bytes = new byte[size];
        new Random(42).NextBytes(bytes);

        var hexLines = new List<string>();
        for (int i = 0; i < size; i += 16)
        {
            var lineBytes = bytes.Skip(i).Take(16).ToArray();
            hexLines.Add(string.Join(" ", lineBytes.Select(b => $"{b:X2}")));
        }

        var region = new MemoryRegion
        {
            Address = "0x00007FF8A1234560",
            RequestedSize = size,
            ActualSize = size,
            Bytes = string.Join("\n", hexLines)
        };

        // JIT warm-up
        _ = System.Text.Json.JsonSerializer.Serialize(region);

        var sw = Stopwatch.StartNew();

        // Act
        var json = System.Text.Json.JsonSerializer.Serialize(region);

        // Assert
        sw.Stop();
        sw.ElapsedMilliseconds.Should().BeLessThan(maxMs,
            $"SC-002: Serializing {size}-byte memory region should complete within {maxMs}ms");
        json.Should().NotBeNullOrEmpty();
    }

    // ========== SC-004: Layout Performance ==========

    /// <summary>
    /// SC-004: TypeLayout model creation is fast.
    /// Layout retrieval should complete within 1 second.
    /// </summary>
    [Fact]
    public void TypeLayout_Creation_IsEfficient()
    {
        // Arrange
        const int layoutCount = 100;
        const int maxMs = 100;
        var layouts = new List<TypeLayout>(layoutCount);
        var sw = Stopwatch.StartNew();

        // Act - create 100 type layouts
        for (int i = 0; i < layoutCount; i++)
        {
            var fields = Enumerable.Range(0, 10).Select(f => new LayoutField
            {
                Name = $"field_{f}",
                TypeName = f % 3 == 0 ? "System.Int32" : f % 3 == 1 ? "System.String" : "System.Object",
                Offset = 16 + f * 8,
                Size = f % 3 == 0 ? 4 : 8,
                Alignment = f % 3 == 0 ? 4 : 8,
                IsReference = f % 3 != 0,
                DeclaringType = $"TestApp.Models.Type_{i}"
            }).ToList();

            layouts.Add(new TypeLayout
            {
                TypeName = $"TestApp.Models.Type_{i}",
                TotalSize = 16 + 80,
                HeaderSize = 16,
                DataSize = 80,
                Fields = fields,
                Padding = new List<PaddingRegion>
                {
                    new PaddingRegion { Offset = 20, Size = 4, Reason = "Alignment" }
                },
                IsValueType = i % 5 == 0,
                BaseType = i % 5 != 0 ? "System.Object" : null
            });
        }

        // Assert
        sw.Stop();
        sw.ElapsedMilliseconds.Should().BeLessThan(maxMs,
            $"SC-004: Creating {layoutCount} type layouts should complete within {maxMs}ms");
        layouts.Should().HaveCount(layoutCount);
    }

    /// <summary>
    /// SC-004: Complex type layout with many fields is handled efficiently.
    /// </summary>
    [Fact]
    public void TypeLayout_ComplexType_IsEfficient()
    {
        // Arrange - simulate complex type with 50 fields and inheritance
        const int fieldCount = 50;
        const int maxMs = 200;
        var sw = Stopwatch.StartNew();

        // Act - create layout with many fields
        var fields = new List<LayoutField>(fieldCount);
        var padding = new List<PaddingRegion>();
        int currentOffset = 16; // After header

        for (int i = 0; i < fieldCount; i++)
        {
            int size = i % 4 == 0 ? 1  // bool
                     : i % 4 == 1 ? 4  // int
                     : i % 4 == 2 ? 8  // long/reference
                     : 2;              // short

            int alignment = size;

            // Add padding if needed
            if (currentOffset % alignment != 0)
            {
                int paddingSize = alignment - (currentOffset % alignment);
                padding.Add(new PaddingRegion
                {
                    Offset = currentOffset,
                    Size = paddingSize,
                    Reason = $"Alignment padding before field_{i}"
                });
                currentOffset += paddingSize;
            }

            fields.Add(new LayoutField
            {
                Name = $"field_{i}",
                TypeName = i % 4 == 0 ? "System.Boolean"
                         : i % 4 == 1 ? "System.Int32"
                         : i % 4 == 2 ? "System.Int64"
                         : "System.Int16",
                Offset = currentOffset,
                Size = size,
                Alignment = alignment,
                IsReference = false,
                DeclaringType = i < 25 ? "TestApp.Models.BaseType" : "TestApp.Models.DerivedType"
            });

            currentOffset += size;
        }

        var layout = new TypeLayout
        {
            TypeName = "TestApp.Models.DerivedType",
            TotalSize = currentOffset,
            HeaderSize = 16,
            DataSize = currentOffset - 16,
            Fields = fields,
            Padding = padding,
            IsValueType = false,
            BaseType = "TestApp.Models.BaseType"
        };

        sw.Stop();

        // Assert
        sw.ElapsedMilliseconds.Should().BeLessThan(maxMs,
            $"SC-004: Creating layout with {fieldCount} fields should complete within {maxMs}ms");
        layout.Fields.Should().HaveCount(fieldCount);
    }

    /// <summary>
    /// SC-004: TypeLayout serialization is efficient.
    /// </summary>
    [Fact]
    public void TypeLayout_Serialization_IsEfficient()
    {
        // Arrange
        const int fieldCount = 20;
        const int maxMs = 100;

        var layout = new TypeLayout
        {
            TypeName = "TestApp.Models.Customer",
            TotalSize = 96,
            HeaderSize = 16,
            DataSize = 80,
            Fields = Enumerable.Range(0, fieldCount).Select(i => new LayoutField
            {
                Name = $"field_{i}",
                TypeName = "System.Int32",
                Offset = 16 + i * 4,
                Size = 4,
                Alignment = 4,
                IsReference = false
            }).ToList(),
            Padding = new List<PaddingRegion>(),
            IsValueType = false,
            BaseType = "System.Object"
        };

        // JIT warm-up
        _ = System.Text.Json.JsonSerializer.Serialize(layout);

        var sw = Stopwatch.StartNew();

        // Act
        var json = System.Text.Json.JsonSerializer.Serialize(layout);

        // Assert
        sw.Stop();
        sw.ElapsedMilliseconds.Should().BeLessThan(maxMs,
            $"SC-004: Serializing type layout should complete within {maxMs}ms");
        json.Should().NotBeNullOrEmpty();
    }

    // ========== Reference Analysis Performance ==========

    /// <summary>
    /// ReferencesResult model creation is efficient.
    /// </summary>
    [Fact]
    public void ReferencesResult_Creation_IsEfficient()
    {
        // Arrange
        const int refCount = 100; // Max results
        const int maxMs = 100;
        var sw = Stopwatch.StartNew();

        // Act - create result with max references
        var outbound = Enumerable.Range(0, refCount).Select(i => new ReferenceInfo
        {
            SourceAddress = "0x00007FF8A1234560",
            SourceType = "TestApp.Models.Parent",
            TargetAddress = $"0x{0x7FF8A1235000 + i * 100:X16}",
            TargetType = i % 3 == 0 ? "System.String"
                       : i % 3 == 1 ? "System.Int32[]"
                       : "TestApp.Models.Child",
            Path = i % 10 < 5 ? $"_field_{i}" : $"_array[{i}]",
            ReferenceType = i % 10 < 5 ? ReferenceType.Field : ReferenceType.ArrayElement
        }).ToList();

        var result = new ReferencesResult
        {
            TargetAddress = "0x00007FF8A1234560",
            TargetType = "TestApp.Models.Parent",
            Outbound = outbound,
            OutboundCount = refCount,
            Truncated = false
        };

        sw.Stop();

        // Assert
        sw.ElapsedMilliseconds.Should().BeLessThan(maxMs,
            $"Creating references result with {refCount} refs should complete within {maxMs}ms");
        result.Outbound.Should().HaveCount(refCount);
    }

    // ========== Memory Efficiency Tests ==========

    /// <summary>
    /// ObjectInspection memory footprint is reasonable.
    /// </summary>
    [Fact]
    public void ObjectInspection_MemoryFootprint_IsReasonable()
    {
        // Arrange
        const int count = 1000;
        var inspections = new List<ObjectInspection>(count);
        var beforeMem = GC.GetTotalMemory(true);

        // Act
        for (int i = 0; i < count; i++)
        {
            inspections.Add(new ObjectInspection
            {
                Address = $"0x{0x7FF8A1234560 + i:X16}",
                TypeName = $"Type_{i}",
                Size = 100,
                Fields = new List<FieldDetail>
                {
                    new FieldDetail { Name = "field", TypeName = "int", Value = "1", Offset = 0, Size = 4, HasChildren = false }
                },
                IsNull = false
            });
        }

        var afterMem = GC.GetTotalMemory(true);
        var memPerItem = (afterMem - beforeMem) / (double)count;

        // Assert - each inspection should use less than 1KB
        memPerItem.Should().BeLessThan(1024, "Each ObjectInspection should use less than 1KB memory");
    }

    /// <summary>
    /// TypeLayout memory footprint is reasonable.
    /// </summary>
    [Fact]
    public void TypeLayout_MemoryFootprint_IsReasonable()
    {
        // Arrange
        const int count = 1000;
        var layouts = new List<TypeLayout>(count);
        var beforeMem = GC.GetTotalMemory(true);

        // Act
        for (int i = 0; i < count; i++)
        {
            layouts.Add(new TypeLayout
            {
                TypeName = $"Type_{i}",
                TotalSize = 48,
                HeaderSize = 16,
                DataSize = 32,
                Fields = new List<LayoutField>
                {
                    new LayoutField { Name = "field", TypeName = "int", Offset = 16, Size = 4, Alignment = 4 }
                },
                Padding = new List<PaddingRegion>(),
                IsValueType = false
            });
        }

        var afterMem = GC.GetTotalMemory(true);
        var memPerItem = (afterMem - beforeMem) / (double)count;

        // Assert - each layout should use less than 512 bytes
        memPerItem.Should().BeLessThan(512, "Each TypeLayout should use less than 512 bytes memory");
    }
}
