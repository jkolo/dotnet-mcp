using DebugMcp.E2E.Support;

namespace DebugMcp.E2E.Hooks;

[Binding]
public sealed class DebuggerHooks
{
    private readonly DebuggerContext _context;

    public DebuggerHooks(DebuggerContext context)
    {
        _context = context;
    }

    [AfterScenario]
    public async Task AfterScenario()
    {
        await _context.CleanupAsync();
        // Allow ICorDebug resources to fully release before next scenario
        await Task.Delay(100);
    }
}
