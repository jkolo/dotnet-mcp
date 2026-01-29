using DebugMcp.E2E.Support;
using DebugMcp.Models.Breakpoints;
using DebugMcp.Tests.Helpers;

namespace DebugMcp.E2E.StepDefinitions;

[Binding]
public sealed class BreakpointSteps
{
    private readonly DebuggerContext _ctx;
    private ExceptionBreakpoint? _lastExceptionBreakpoint;
    private IReadOnlyList<Breakpoint>? _lastBreakpointList;

    public BreakpointSteps(DebuggerContext ctx)
    {
        _ctx = ctx;
    }

    [Given(@"a breakpoint on ""(.*)"" line (\d+)")]
    public async Task GivenABreakpointOnLine(string file, int line)
    {
        var sourceFile = TestTargetProcess.GetSourceFilePath(file);
        var bp = await _ctx.BreakpointManager.SetBreakpointAsync(
            sourceFile, line, column: null, condition: null, CancellationToken.None);
        _ctx.LastSetBreakpoint = bp;
        _ctx.SetBreakpoints.Add(bp);
    }

    [Given(@"a conditional breakpoint on ""(.*)"" line (\d+) with condition ""(.*)""")]
    public async Task GivenAConditionalBreakpointOnLineWithCondition(string file, int line, string condition)
    {
        var sourceFile = TestTargetProcess.GetSourceFilePath(file);
        var bp = await _ctx.BreakpointManager.SetBreakpointAsync(
            sourceFile, line, column: null, condition: condition, CancellationToken.None);
        _ctx.LastSetBreakpoint = bp;
        _ctx.SetBreakpoints.Add(bp);
    }

    [When(@"the test target executes the ""(.*)"" command")]
    public async Task WhenTheTestTargetExecutesTheCommand(string command)
    {
        await _ctx.TargetProcess!.SendCommandAsync(command);
    }

    [When("I wait for a breakpoint hit")]
    public async Task WhenIWaitForABreakpointHit()
    {
        _ctx.LastBreakpointHit = await _ctx.BreakpointManager.WaitForBreakpointAsync(
            TimeSpan.FromSeconds(10), CancellationToken.None);
        _ctx.LastBreakpointHit.Should().NotBeNull("breakpoint should have been hit");
    }

    [When("I remove the breakpoint")]
    public async Task WhenIRemoveTheBreakpoint()
    {
        _ctx.LastSetBreakpoint.Should().NotBeNull();
        await _ctx.BreakpointManager.RemoveBreakpointAsync(
            _ctx.LastSetBreakpoint!.Id, CancellationToken.None);
    }

    [Then(@"the debugger should pause at ""(.*)"" line (\d+)")]
    public void ThenTheDebuggerShouldPauseAtLine(string file, int line)
    {
        _ctx.LastBreakpointHit.Should().NotBeNull();
        // Verify the hit location matches the expected file and line
        _ctx.LastBreakpointHit!.Location.Should().NotBeNull();
        _ctx.LastBreakpointHit.Location.File.Should().Contain(file);
        _ctx.LastBreakpointHit.Location.Line.Should().Be(line);
    }

    [Then(@"the breakpoint hit count should be (\d+)")]
    public void ThenTheBreakpointHitCountShouldBe(int expectedCount)
    {
        _ctx.LastBreakpointHit.Should().NotBeNull();
        _ctx.LastBreakpointHit!.HitCount.Should().Be(expectedCount);
    }

    [Then(@"the debugger should not pause within (\d+) seconds")]
    public async Task ThenTheDebuggerShouldNotPauseWithinSeconds(int seconds)
    {
        var hit = await _ctx.BreakpointManager.WaitForBreakpointAsync(
            TimeSpan.FromSeconds(seconds), CancellationToken.None);
        hit.Should().BeNull("no breakpoint should have been hit");
    }

    // --- Exception Breakpoints ---

    [When(@"I set an exception breakpoint for ""(.*)""")]
    public async Task WhenISetAnExceptionBreakpointFor(string exceptionType)
    {
        _lastExceptionBreakpoint = await _ctx.BreakpointManager.SetExceptionBreakpointAsync(
            exceptionType,
            breakOnFirstChance: true,
            breakOnSecondChance: true,
            includeSubtypes: true,
            CancellationToken.None);
    }

    [Then("the exception breakpoint should be set")]
    public void ThenTheExceptionBreakpointShouldBeSet()
    {
        _lastExceptionBreakpoint.Should().NotBeNull();
        _lastExceptionBreakpoint!.Id.Should().NotBeNullOrEmpty();
    }

    [Then(@"the exception breakpoint should be for type ""(.*)""")]
    public void ThenTheExceptionBreakpointShouldBeForType(string expectedType)
    {
        _lastExceptionBreakpoint.Should().NotBeNull();
        _lastExceptionBreakpoint!.ExceptionType.Should().Be(expectedType);
    }

    [Then("the last hit should be an exception breakpoint")]
    [Then("the breakpoint hit should be an exception")]
    public void ThenTheLastHitShouldBeAnExceptionBreakpoint()
    {
        _ctx.LastBreakpointHit.Should().NotBeNull();
        _ctx.LastBreakpointHit!.ExceptionInfo.Should().NotBeNull("Expected an exception breakpoint hit");
    }

    // --- Breakpoint Listing ---

    [When("I list all breakpoints")]
    public async Task WhenIListAllBreakpoints()
    {
        _lastBreakpointList = await _ctx.BreakpointManager.GetBreakpointsAsync(CancellationToken.None);
    }

    [Then(@"the breakpoint list should contain (\d+) breakpoints")]
    public void ThenTheBreakpointListShouldContainBreakpoints(int expectedCount)
    {
        _lastBreakpointList.Should().NotBeNull();
        _lastBreakpointList!.Count.Should().Be(expectedCount);
    }

    [Then(@"the breakpoint list should contain a breakpoint on ""(.*)""")]
    public void ThenTheBreakpointListShouldContainABreakpointOn(string file)
    {
        _lastBreakpointList.Should().NotBeNull();
        _lastBreakpointList!.Should().Contain(bp => bp.Location.File.Contains(file));
    }

    // --- Enable/Disable ---

    [When("I disable the last set breakpoint")]
    public async Task WhenIDisableTheLastSetBreakpoint()
    {
        _ctx.LastSetBreakpoint.Should().NotBeNull();
        await _ctx.BreakpointManager.SetBreakpointEnabledAsync(
            _ctx.LastSetBreakpoint!.Id, enabled: false, CancellationToken.None);
    }

    [When("I enable the last set breakpoint")]
    public async Task WhenIEnableTheLastSetBreakpoint()
    {
        _ctx.LastSetBreakpoint.Should().NotBeNull();
        await _ctx.BreakpointManager.SetBreakpointEnabledAsync(
            _ctx.LastSetBreakpoint!.Id, enabled: true, CancellationToken.None);
    }

    [Then("the last set breakpoint should be disabled")]
    public async Task ThenTheLastSetBreakpointShouldBeDisabled()
    {
        var breakpoints = await _ctx.BreakpointManager.GetBreakpointsAsync(CancellationToken.None);
        var bp = breakpoints.FirstOrDefault(b => b.Id == _ctx.LastSetBreakpoint!.Id);
        bp.Should().NotBeNull();
        bp!.Enabled.Should().BeFalse();
    }

    [Then("the last set breakpoint should be enabled")]
    public async Task ThenTheLastSetBreakpointShouldBeEnabled()
    {
        var breakpoints = await _ctx.BreakpointManager.GetBreakpointsAsync(CancellationToken.None);
        var bp = breakpoints.FirstOrDefault(b => b.Id == _ctx.LastSetBreakpoint!.Id);
        bp.Should().NotBeNull();
        bp!.Enabled.Should().BeTrue();
    }

    // --- Remove by ID ---

    [When("I remove the first breakpoint by ID")]
    public async Task WhenIRemoveTheFirstBreakpointByID()
    {
        _ctx.SetBreakpoints.Should().NotBeEmpty();
        var firstBp = _ctx.SetBreakpoints[0];
        await _ctx.BreakpointManager.RemoveBreakpointAsync(firstBp.Id, CancellationToken.None);
        _ctx.SetBreakpoints.RemoveAt(0);
    }
}
