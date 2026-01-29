using System.Collections.Concurrent;
using System.Reflection.Metadata;
using Microsoft.Extensions.Logging;

namespace DebugMcp.Services.Breakpoints;

/// <summary>
/// Caches MetadataReaderProvider instances per assembly to avoid repeated file I/O.
/// Thread-safe and disposes cached readers on disposal.
/// </summary>
public sealed class PdbSymbolCache : IDisposable
{
    private readonly ILogger<PdbSymbolCache> _logger;
    private readonly ConcurrentDictionary<string, CachedPdbEntry> _cache = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public PdbSymbolCache(ILogger<PdbSymbolCache> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Gets or creates a cached MetadataReader for the specified assembly's PDB.
    /// </summary>
    /// <param name="assemblyPath">Path to the assembly (DLL or EXE).</param>
    /// <returns>The MetadataReader, or null if PDB not found.</returns>
    public MetadataReader? GetOrCreateReader(string assemblyPath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var normalizedPath = Path.GetFullPath(assemblyPath);

        if (_cache.TryGetValue(normalizedPath, out var cached))
        {
            return cached.Reader;
        }

        var entry = LoadPdb(normalizedPath);
        if (entry == null)
        {
            return null;
        }

        _cache[normalizedPath] = entry;
        return entry.Reader;
    }

    /// <summary>
    /// Invalidates a cached entry (e.g., when assembly is rebuilt).
    /// </summary>
    /// <param name="assemblyPath">Path to the assembly.</param>
    public void Invalidate(string assemblyPath)
    {
        var normalizedPath = Path.GetFullPath(assemblyPath);
        if (_cache.TryRemove(normalizedPath, out var entry))
        {
            entry.Dispose();
            _logger.LogDebug("Invalidated PDB cache for {AssemblyPath}", normalizedPath);
        }
    }

    /// <summary>
    /// Clears all cached entries.
    /// </summary>
    public void Clear()
    {
        foreach (var entry in _cache.Values)
        {
            entry.Dispose();
        }
        _cache.Clear();
        _logger.LogDebug("Cleared PDB symbol cache");
    }

    private CachedPdbEntry? LoadPdb(string assemblyPath)
    {
        // Try to find PDB file
        var pdbPath = FindPdbPath(assemblyPath);
        if (pdbPath == null)
        {
            _logger.LogDebug("No PDB found for {AssemblyPath}", assemblyPath);
            return null;
        }

        try
        {
            // Open PDB file as memory-mapped for efficiency
            var fileStream = File.OpenRead(pdbPath);
            var provider = MetadataReaderProvider.FromPortablePdbStream(fileStream);
            var reader = provider.GetMetadataReader();

            _logger.LogDebug("Loaded PDB from {PdbPath}", pdbPath);
            return new CachedPdbEntry(provider, reader, fileStream);
        }
        catch (BadImageFormatException ex)
        {
            _logger.LogWarning(ex, "PDB at {PdbPath} is not a valid portable PDB", pdbPath);
            return null;
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Failed to read PDB at {PdbPath}", pdbPath);
            return null;
        }
    }

    private static string? FindPdbPath(string assemblyPath)
    {
        // Standard location: same directory, same name with .pdb extension
        var directory = Path.GetDirectoryName(assemblyPath);
        var baseName = Path.GetFileNameWithoutExtension(assemblyPath);

        if (directory == null)
        {
            return null;
        }

        var pdbPath = Path.Combine(directory, baseName + ".pdb");
        if (File.Exists(pdbPath))
        {
            return pdbPath;
        }

        // Check for embedded PDB (would need different handling)
        // For now, only support external PDB files
        return null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Clear();
    }

    private sealed class CachedPdbEntry : IDisposable
    {
        public MetadataReaderProvider Provider { get; }
        public MetadataReader Reader { get; }
        private readonly FileStream _fileStream;

        public CachedPdbEntry(MetadataReaderProvider provider, MetadataReader reader, FileStream fileStream)
        {
            Provider = provider;
            Reader = reader;
            _fileStream = fileStream;
        }

        public void Dispose()
        {
            Provider.Dispose();
            _fileStream.Dispose();
        }
    }
}
