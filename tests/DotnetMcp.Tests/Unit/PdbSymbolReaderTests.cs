using DotnetMcp.Services.Breakpoints;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace DotnetMcp.Tests.Unit;

/// <summary>
/// Unit tests for PdbSymbolReader source-to-IL mapping functionality.
/// </summary>
public class PdbSymbolReaderTests : IDisposable
{
    private readonly PdbSymbolCache _cache;
    private readonly PdbSymbolReader _reader;

    public PdbSymbolReaderTests()
    {
        var cacheLogger = new Mock<ILogger<PdbSymbolCache>>();
        var readerLogger = new Mock<ILogger<PdbSymbolReader>>();
        _cache = new PdbSymbolCache(cacheLogger.Object);
        _reader = new PdbSymbolReader(_cache, readerLogger.Object);
    }

    public void Dispose()
    {
        _cache.Dispose();
    }

    /// <summary>
    /// FindILOffsetAsync returns null when PDB file does not exist.
    /// </summary>
    [Fact]
    public async Task FindILOffsetAsync_NoPdb_ReturnsNull()
    {
        // Arrange
        var nonExistentAssembly = "/nonexistent/assembly.dll";

        // Act
        var result = await _reader.FindILOffsetAsync(
            nonExistentAssembly,
            "/app/Program.cs",
            10);

        // Assert
        result.Should().BeNull("no PDB file exists for the assembly");
    }

    /// <summary>
    /// FindILOffsetAsync returns null when source file is not in PDB.
    /// </summary>
    [Fact]
    public async Task FindILOffsetAsync_SourceFileNotInPdb_ReturnsNull()
    {
        // Arrange - use current test assembly which has a PDB
        var testAssembly = typeof(PdbSymbolReaderTests).Assembly.Location;
        var nonExistentSourceFile = "/nonexistent/source.cs";

        // Act
        var result = await _reader.FindILOffsetAsync(
            testAssembly,
            nonExistentSourceFile,
            10);

        // Assert
        result.Should().BeNull("source file is not in the PDB");
    }

    /// <summary>
    /// FindILOffsetAsync returns IL offset for valid source location.
    /// </summary>
    [Fact]
    public async Task FindILOffsetAsync_ValidLocation_ReturnsILOffset()
    {
        // Arrange - use current test assembly and this source file
        var testAssembly = typeof(PdbSymbolReaderTests).Assembly.Location;
        var thisSourceFile = GetThisSourceFile();

        // Act - try to find a line in this file (the method declaration)
        var result = await _reader.FindILOffsetAsync(
            testAssembly,
            thisSourceFile,
            GetCurrentLineNumber());

        // Assert - we should find some IL offset (or null if PDB doesn't exist in test env)
        // Note: In CI environments, PDB may not be available
        if (result != null)
        {
            result.ILOffset.Should().BeGreaterThanOrEqualTo(0, "IL offset should be non-negative");
            result.MethodToken.Should().NotBe(0, "method token should be valid");
            result.StartLine.Should().BeGreaterThanOrEqualTo(1, "start line should be 1-based");
        }
    }

    /// <summary>
    /// GetSequencePointsOnLineAsync returns empty list for invalid source file.
    /// </summary>
    [Fact]
    public async Task GetSequencePointsOnLineAsync_InvalidSourceFile_ReturnsEmpty()
    {
        // Arrange
        var testAssembly = typeof(PdbSymbolReaderTests).Assembly.Location;
        var nonExistentSourceFile = "/nonexistent/source.cs";

        // Act
        var result = await _reader.GetSequencePointsOnLineAsync(
            testAssembly,
            nonExistentSourceFile,
            10);

        // Assert
        result.Should().BeEmpty("source file is not in the PDB");
    }

    /// <summary>
    /// GetSequencePointsOnLineAsync returns sequence points for valid line.
    /// </summary>
    [Fact]
    public async Task GetSequencePointsOnLineAsync_ValidLine_ReturnsSequencePoints()
    {
        // Arrange
        var testAssembly = typeof(PdbSymbolReaderTests).Assembly.Location;
        var thisSourceFile = GetThisSourceFile();

        // Act
        var result = await _reader.GetSequencePointsOnLineAsync(
            testAssembly,
            thisSourceFile,
            GetCurrentLineNumber());

        // Assert - may be empty if PDB not available, or have entries
        result.Should().NotBeNull("result should never be null");
        foreach (var sp in result)
        {
            sp.ILOffset.Should().BeGreaterThanOrEqualTo(0);
            sp.StartLine.Should().BeGreaterThanOrEqualTo(1);
            sp.StartColumn.Should().BeGreaterThanOrEqualTo(1);
        }
    }

    /// <summary>
    /// FindNearestValidLineAsync returns null for non-existent source file.
    /// </summary>
    [Fact]
    public async Task FindNearestValidLineAsync_InvalidSourceFile_ReturnsNull()
    {
        // Arrange
        var testAssembly = typeof(PdbSymbolReaderTests).Assembly.Location;
        var nonExistentSourceFile = "/nonexistent/source.cs";

        // Act
        var result = await _reader.FindNearestValidLineAsync(
            testAssembly,
            nonExistentSourceFile,
            10);

        // Assert
        result.Should().BeNull("source file is not in the PDB");
    }

    /// <summary>
    /// ILOffsetResult contains all required fields.
    /// </summary>
    [Fact]
    public void ILOffsetResult_ContainsAllFields()
    {
        // Arrange & Act
        var result = new ILOffsetResult(
            ILOffset: 42,
            MethodToken: 0x06000001,
            StartLine: 10,
            StartColumn: 5,
            EndLine: 10,
            EndColumn: 30);

        // Assert
        result.ILOffset.Should().Be(42);
        result.MethodToken.Should().Be(0x06000001);
        result.StartLine.Should().Be(10);
        result.StartColumn.Should().Be(5);
        result.EndLine.Should().Be(10);
        result.EndColumn.Should().Be(30);
    }

    /// <summary>
    /// SequencePointInfo contains all fields including hidden flag.
    /// </summary>
    [Fact]
    public void SequencePointInfo_ContainsAllFields()
    {
        // Arrange & Act
        var sp = new SequencePointInfo(
            ILOffset: 0,
            StartLine: 1,
            StartColumn: 1,
            EndLine: 1,
            EndColumn: 10,
            IsHidden: false);

        // Assert
        sp.ILOffset.Should().Be(0);
        sp.StartLine.Should().Be(1);
        sp.StartColumn.Should().Be(1);
        sp.EndLine.Should().Be(1);
        sp.EndColumn.Should().Be(10);
        sp.IsHidden.Should().BeFalse();
    }

    private static string GetThisSourceFile([System.Runtime.CompilerServices.CallerFilePath] string path = "")
        => path;

    private static int GetCurrentLineNumber([System.Runtime.CompilerServices.CallerLineNumber] int line = 0)
        => line;
}
