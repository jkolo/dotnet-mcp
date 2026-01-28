using DotnetMcp.E2E.Support;

namespace DotnetMcp.E2E.StepDefinitions;

[Binding]
public sealed class ModuleSteps
{
    private readonly DebuggerContext _ctx;

    public ModuleSteps(DebuggerContext ctx)
    {
        _ctx = ctx;
    }

    [When("I list all modules without system filter")]
    public async Task WhenIListAllModulesWithoutSystemFilter()
    {
        _ctx.LastModules = await _ctx.ProcessDebugger.GetModulesAsync(
            includeSystem: false);
    }

    [Then(@"the module list should contain ""(.*)""")]
    public void ThenTheModuleListShouldContain(string moduleName)
    {
        _ctx.LastModules.Should().NotBeNull();
        _ctx.LastModules.Should().Contain(
            m => m.Name.Contains(moduleName, StringComparison.OrdinalIgnoreCase),
            $"module list should contain '{moduleName}'");
    }
}
