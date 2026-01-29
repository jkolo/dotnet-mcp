using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using Microsoft.Extensions.Logging;

namespace DebugMcp.Services.Breakpoints;

/// <summary>
/// Reads PDB symbol information for source-to-IL mapping using System.Reflection.Metadata.
/// </summary>
public sealed class PdbSymbolReader : IPdbSymbolReader
{
    private readonly PdbSymbolCache _cache;
    private readonly ILogger<PdbSymbolReader> _logger;

    public PdbSymbolReader(PdbSymbolCache cache, ILogger<PdbSymbolReader> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<ILOffsetResult?> FindILOffsetAsync(
        string assemblyPath,
        string sourceFile,
        int line,
        int? column = null,
        CancellationToken cancellationToken = default)
    {
        var reader = _cache.GetOrCreateReader(assemblyPath);
        if (reader == null)
        {
            _logger.LogDebug("No PDB available for {AssemblyPath}", assemblyPath);
            return Task.FromResult<ILOffsetResult?>(null);
        }

        var normalizedSourceFile = NormalizePath(sourceFile);

        // Find the document handle for the source file
        var documentHandle = FindDocument(reader, normalizedSourceFile);
        if (documentHandle.IsNil)
        {
            _logger.LogDebug("Source file {SourceFile} not found in PDB", sourceFile);
            return Task.FromResult<ILOffsetResult?>(null);
        }

        // Iterate through all methods to find sequence points matching the line
        foreach (var methodDebugInfoHandle in reader.MethodDebugInformation)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var debugInfo = reader.GetMethodDebugInformation(methodDebugInfoHandle);
            if (debugInfo.Document.IsNil)
            {
                continue;
            }

            var sequencePoint = FindSequencePoint(reader, debugInfo, documentHandle, line, column);
            if (sequencePoint.HasValue)
            {
                var sp = sequencePoint.Value;
                var methodToken = MetadataTokens.GetToken(methodDebugInfoHandle.ToDefinitionHandle());

                _logger.LogDebug(
                    "Found IL offset {Offset} for {File}:{Line} in method {Token:X8}",
                    sp.Offset, sourceFile, line, methodToken);

                return Task.FromResult<ILOffsetResult?>(new ILOffsetResult(
                    sp.Offset,
                    methodToken,
                    sp.StartLine,
                    sp.StartColumn,
                    sp.EndLine,
                    sp.EndColumn));
            }
        }

        _logger.LogDebug("No sequence point found for {SourceFile}:{Line}", sourceFile, line);
        return Task.FromResult<ILOffsetResult?>(null);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SequencePointInfo>> GetSequencePointsOnLineAsync(
        string assemblyPath,
        string sourceFile,
        int line,
        CancellationToken cancellationToken = default)
    {
        var reader = _cache.GetOrCreateReader(assemblyPath);
        if (reader == null)
        {
            return Task.FromResult<IReadOnlyList<SequencePointInfo>>(Array.Empty<SequencePointInfo>());
        }

        var normalizedSourceFile = NormalizePath(sourceFile);
        var documentHandle = FindDocument(reader, normalizedSourceFile);
        if (documentHandle.IsNil)
        {
            return Task.FromResult<IReadOnlyList<SequencePointInfo>>(Array.Empty<SequencePointInfo>());
        }

        var results = new List<SequencePointInfo>();

        foreach (var methodDebugInfoHandle in reader.MethodDebugInformation)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var debugInfo = reader.GetMethodDebugInformation(methodDebugInfoHandle);
            foreach (var sp in debugInfo.GetSequencePoints())
            {
                if (sp.IsHidden || sp.Document != documentHandle)
                {
                    continue;
                }

                if (sp.StartLine == line)
                {
                    results.Add(new SequencePointInfo(
                        sp.Offset,
                        sp.StartLine,
                        sp.StartColumn,
                        sp.EndLine,
                        sp.EndColumn,
                        sp.IsHidden));
                }
            }
        }

        // Sort by column for predictable ordering
        results.Sort((a, b) => a.StartColumn.CompareTo(b.StartColumn));

        return Task.FromResult<IReadOnlyList<SequencePointInfo>>(results);
    }

    /// <inheritdoc />
    public Task<int?> FindNearestValidLineAsync(
        string assemblyPath,
        string sourceFile,
        int line,
        int searchRange = 10,
        CancellationToken cancellationToken = default)
    {
        var reader = _cache.GetOrCreateReader(assemblyPath);
        if (reader == null)
        {
            return Task.FromResult<int?>(null);
        }

        var normalizedSourceFile = NormalizePath(sourceFile);
        var documentHandle = FindDocument(reader, normalizedSourceFile);
        if (documentHandle.IsNil)
        {
            return Task.FromResult<int?>(null);
        }

        int? nearestLine = null;
        var minDistance = int.MaxValue;

        foreach (var methodDebugInfoHandle in reader.MethodDebugInformation)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var debugInfo = reader.GetMethodDebugInformation(methodDebugInfoHandle);
            foreach (var sp in debugInfo.GetSequencePoints())
            {
                if (sp.IsHidden || sp.Document != documentHandle)
                {
                    continue;
                }

                var distance = Math.Abs(sp.StartLine - line);
                if (distance <= searchRange && distance < minDistance)
                {
                    minDistance = distance;
                    nearestLine = sp.StartLine;

                    // Exact match - return immediately
                    if (distance == 0)
                    {
                        return Task.FromResult<int?>(nearestLine);
                    }
                }
            }
        }

        return Task.FromResult(nearestLine);
    }

    private static DocumentHandle FindDocument(MetadataReader reader, string normalizedPath)
    {
        foreach (var documentHandle in reader.Documents)
        {
            var document = reader.GetDocument(documentHandle);
            var documentName = reader.GetString(document.Name);
            var normalizedDocumentName = NormalizePath(documentName);

            if (normalizedDocumentName.Equals(normalizedPath, StringComparison.OrdinalIgnoreCase))
            {
                return documentHandle;
            }
        }

        return default;
    }

    private static SequencePoint? FindSequencePoint(
        MetadataReader reader,
        MethodDebugInformation debugInfo,
        DocumentHandle documentHandle,
        int targetLine,
        int? targetColumn)
    {
        var candidates = new List<SequencePoint>();

        foreach (var sp in debugInfo.GetSequencePoints())
        {
            if (sp.IsHidden)
            {
                continue;
            }

            // Check if sequence point is in the target document
            var spDocument = sp.Document;
            if (spDocument.IsNil)
            {
                // Use method's document
                spDocument = debugInfo.Document;
            }

            if (spDocument != documentHandle)
            {
                continue;
            }

            if (sp.StartLine == targetLine)
            {
                candidates.Add(sp);
            }
        }

        if (candidates.Count == 0)
        {
            return null;
        }

        // Sort by column
        candidates.Sort((a, b) => a.StartColumn.CompareTo(b.StartColumn));

        // If no column specified, return the first sequence point on the line
        if (targetColumn == null)
        {
            return candidates[0];
        }

        // Find sequence point containing the column
        foreach (var sp in candidates)
        {
            if (sp.StartColumn <= targetColumn && targetColumn <= sp.EndColumn)
            {
                return sp;
            }
        }

        // Find nearest sequence point by column
        return candidates
            .OrderBy(sp => Math.Abs(sp.StartColumn - targetColumn.Value))
            .First();
    }

    /// <inheritdoc />
    public Task<SourceLocationResult?> FindSourceLocationAsync(
        string assemblyPath,
        int methodToken,
        int ilOffset,
        CancellationToken cancellationToken = default)
    {
        var reader = _cache.GetOrCreateReader(assemblyPath);
        if (reader == null)
        {
            _logger.LogDebug("No PDB available for {AssemblyPath}", assemblyPath);
            return Task.FromResult<SourceLocationResult?>(null);
        }

        // Convert method token to MethodDebugInformationHandle
        var methodDefHandle = MetadataTokens.MethodDefinitionHandle(methodToken);
        var methodDebugInfoHandle = methodDefHandle.ToDebugInformationHandle();

        try
        {
            var debugInfo = reader.GetMethodDebugInformation(methodDebugInfoHandle);
            if (debugInfo.Document.IsNil)
            {
                _logger.LogDebug("No debug info for method token {Token:X8}", methodToken);
                return Task.FromResult<SourceLocationResult?>(null);
            }

            // Get the document path
            var document = reader.GetDocument(debugInfo.Document);
            var documentPath = reader.GetString(document.Name);

            // Find the sequence point at or before the IL offset
            SequencePoint? matchingSequencePoint = null;
            string? functionName = null;

            foreach (var sp in debugInfo.GetSequencePoints())
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (sp.IsHidden)
                {
                    continue;
                }

                // Find the sequence point with largest offset <= ilOffset
                if (sp.Offset <= ilOffset)
                {
                    if (matchingSequencePoint == null || sp.Offset > matchingSequencePoint.Value.Offset)
                    {
                        matchingSequencePoint = sp;
                    }
                }
            }

            if (matchingSequencePoint == null)
            {
                _logger.LogDebug("No sequence point found for IL offset {Offset} in method {Token:X8}",
                    ilOffset, methodToken);
                return Task.FromResult<SourceLocationResult?>(null);
            }

            // Try to get the method name from metadata
            try
            {
                var methodDef = reader.GetMethodDefinition(methodDefHandle);
                functionName = reader.GetString(methodDef.Name);
            }
            catch
            {
                // Method name lookup failed, use token as fallback
                functionName = $"0x{methodToken:X8}";
            }

            var sp2 = matchingSequencePoint.Value;
            var result = new SourceLocationResult(
                FilePath: documentPath,
                Line: sp2.StartLine,
                Column: sp2.StartColumn,
                EndLine: sp2.EndLine,
                EndColumn: sp2.EndColumn,
                FunctionName: functionName);

            _logger.LogDebug(
                "Found source location {File}:{Line}:{Column} for IL offset {Offset} in method {Function}",
                documentPath, sp2.StartLine, sp2.StartColumn, ilOffset, functionName);

            return Task.FromResult<SourceLocationResult?>(result);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to find source location for method {Token:X8} at IL {Offset}",
                methodToken, ilOffset);
            return Task.FromResult<SourceLocationResult?>(null);
        }
    }

    /// <inheritdoc />
    public Task<bool> ContainsSourceFileAsync(
        string assemblyPath,
        string sourceFile,
        CancellationToken cancellationToken = default)
    {
        var reader = _cache.GetOrCreateReader(assemblyPath);
        if (reader == null)
        {
            return Task.FromResult(false);
        }

        var normalizedSourceFile = NormalizePath(sourceFile);
        var documentHandle = FindDocument(reader, normalizedSourceFile);
        return Task.FromResult(!documentHandle.IsNil);
    }

    private static string NormalizePath(string path)
    {
        // Normalize path separators and resolve relative paths
        return Path.GetFullPath(path).Replace('\\', '/');
    }
}
