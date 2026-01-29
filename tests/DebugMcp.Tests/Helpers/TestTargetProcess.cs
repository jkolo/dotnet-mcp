using System.Diagnostics;
using System.Text;

namespace DebugMcp.Tests.Helpers;

/// <summary>
/// Helper to launch and manage the test target process for debugging tests.
/// </summary>
public sealed class TestTargetProcess : IDisposable
{
    private Process? _process;
    private bool _disposed;
    private readonly StringBuilder _outputBuffer = new();
    private readonly object _outputLock = new();

    /// <summary>
    /// Path to the test target DLL.
    /// </summary>
    public static string TestTargetDllPath
    {
        get
        {
            // Navigate from test assembly location to TestTargetApp output
            var testAssemblyDir = Path.GetDirectoryName(typeof(TestTargetProcess).Assembly.Location)!;
            // tests/DebugMcp.Tests/bin/Debug/net10.0 -> tests/TestTargetApp/bin/Debug/net10.0
            var testTargetPath = Path.GetFullPath(Path.Combine(
                testAssemblyDir, "..", "..", "..", "..", "TestTargetApp", "bin", "Debug", "net10.0", "TestTargetApp.dll"));
            return testTargetPath;
        }
    }

    /// <summary>
    /// Process ID of the running test target.
    /// </summary>
    public int ProcessId => _process?.Id ?? throw new InvalidOperationException("Process not started");

    /// <summary>
    /// Whether the process is running.
    /// </summary>
    public bool IsRunning => _process != null && !_process.HasExited;

    /// <summary>
    /// Starts the test target process and waits for it to be ready.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_process != null)
            throw new InvalidOperationException("Process already started");

        var dllPath = TestTargetDllPath;
        if (!File.Exists(dllPath))
            throw new FileNotFoundException($"Test target not found. Run 'dotnet build' on TestTargetApp first.", dllPath);

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"\"{dllPath}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true
        };

        _process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start test target process");

        // Wait for "READY" signal
        var readyTask = WaitForReadyAsync(cancellationToken);
        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);

        if (await Task.WhenAny(readyTask, timeoutTask) == timeoutTask)
        {
            Kill();
            throw new TimeoutException("Test target process did not become ready in time");
        }
    }

    private async Task WaitForReadyAsync(CancellationToken cancellationToken)
    {
        if (_process == null) return;

        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await _process.StandardOutput.ReadLineAsync(cancellationToken);
            if (line == "READY")
                return;
            if (_process.HasExited)
                throw new InvalidOperationException("Test target process exited unexpectedly");
        }
    }

    /// <summary>
    /// Kills the test target process.
    /// </summary>
    public void Kill()
    {
        if (_process == null || _process.HasExited) return;

        try
        {
            _process.Kill(entireProcessTree: true);
            _process.WaitForExit(1000);
        }
        catch
        {
            // Ignore errors during cleanup
        }
    }

    /// <summary>
    /// Sends a command to the test target process.
    /// </summary>
    public async Task SendCommandAsync(string command)
    {
        if (_process == null)
            throw new InvalidOperationException("Process not started");

        await _process.StandardInput.WriteLineAsync(command);
        await _process.StandardInput.FlushAsync();
    }

    /// <summary>
    /// Waits for specific output from the process.
    /// </summary>
    public async Task<string?> WaitForOutputAsync(string expectedPrefix, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (_process == null)
            throw new InvalidOperationException("Process not started");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        try
        {
            while (!cts.Token.IsCancellationRequested)
            {
                var line = await _process.StandardOutput.ReadLineAsync(cts.Token);
                if (line == null)
                    return null;

                lock (_outputLock)
                {
                    _outputBuffer.AppendLine(line);
                }

                if (line.StartsWith(expectedPrefix))
                    return line;
            }
        }
        catch (OperationCanceledException)
        {
            // Timeout
        }

        return null;
    }

    /// <summary>
    /// Reads any available output without blocking.
    /// </summary>
    public string GetBufferedOutput()
    {
        lock (_outputLock)
        {
            return _outputBuffer.ToString();
        }
    }

    /// <summary>
    /// Path to the TestTargetApp source directory.
    /// </summary>
    public static string TestTargetSourceDirectory
    {
        get
        {
            var testAssemblyDir = Path.GetDirectoryName(typeof(TestTargetProcess).Assembly.Location)!;
            return Path.GetFullPath(Path.Combine(
                testAssemblyDir, "..", "..", "..", "..", "TestTargetApp"));
        }
    }

    /// <summary>
    /// Gets the full path to a source file in TestTargetApp.
    /// </summary>
    public static string GetSourceFilePath(string fileName)
    {
        return Path.Combine(TestTargetSourceDirectory, fileName);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Kill();
        _process?.Dispose();
        _process = null;
    }
}
