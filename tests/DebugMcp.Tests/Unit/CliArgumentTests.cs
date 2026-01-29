using System.Diagnostics;

namespace DebugMcp.Tests.Unit;

public class CliArgumentTests
{
    private static readonly string ProjectPath = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "DebugMcp", "DebugMcp.csproj"));

    private static readonly string Configuration =
#if DEBUG
        "Debug";
#else
        "Release";
#endif

    private async Task<(int ExitCode, string StdOut, string StdErr)> RunToolAsync(params string[] args)
    {
        var psi = new ProcessStartInfo("dotnet", ["run", "--project", ProjectPath, "--no-build", "-c", Configuration, "--", .. args])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = Process.Start(psi)!;
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, stdout.Trim(), stderr.Trim());
    }

    [Fact]
    public async Task Version_Flag_Displays_Version_And_Exits_With_Zero()
    {
        var (exitCode, stdout, _) = await RunToolAsync("--version");

        exitCode.Should().Be(0);
        stdout.Should().MatchRegex(@"^\d+\.\d+\.\d+");
    }

    [Fact]
    public async Task Help_Flag_Displays_Usage_And_Exits_With_Zero()
    {
        var (exitCode, stdout, _) = await RunToolAsync("--help");

        exitCode.Should().Be(0);
        stdout.Should().Contain("MCP server for debugging .NET applications");
        stdout.Should().Contain("--help");
        stdout.Should().Contain("--version");
    }

    [Fact]
    public async Task Unknown_Argument_Exits_With_NonZero()
    {
        var (exitCode, _, stderr) = await RunToolAsync("--bogus");

        exitCode.Should().NotBe(0);
        stderr.Should().Contain("--bogus");
    }

    [Fact]
    public async Task No_Arguments_Starts_MCP_Server()
    {
        var psi = new ProcessStartInfo("dotnet", ["run", "--project", ProjectPath, "--no-build", "-c", Configuration])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = Process.Start(psi)!;

        // Give it a moment to start, then kill — we just need to confirm it doesn't exit immediately with error
        await Task.Delay(2000);

        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            // Process was still running = MCP server started successfully
            return;
        }

        // If it exited, check stderr for MCP transport messages (normal EOF exit when stdin closes)
        var stderr = await process.StandardError.ReadToEndAsync();
        stderr.Should().Contain("transport");
    }
}
