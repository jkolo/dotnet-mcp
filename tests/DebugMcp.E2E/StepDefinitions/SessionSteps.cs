using DebugMcp.E2E.Support;
using DebugMcp.Models;
using DebugMcp.Tests.Helpers;

namespace DebugMcp.E2E.StepDefinitions;

[Binding]
public sealed class SessionSteps
{
    private readonly DebuggerContext _ctx;

    public SessionSteps(DebuggerContext ctx)
    {
        _ctx = ctx;
    }

    [Given("a running test target process")]
    public async Task GivenARunningTestTargetProcess()
    {
        _ctx.TargetProcess = new TestTargetProcess();
        await _ctx.TargetProcess.StartAsync();
        _ctx.TargetProcess.IsRunning.Should().BeTrue("test target should be running");
    }

    [Given("the debugger is attached to the test target")]
    public async Task GivenTheDebuggerIsAttachedToTheTestTarget()
    {
        if (_ctx.TargetProcess == null)
            await GivenARunningTestTargetProcess();

        await _ctx.SessionManager.AttachAsync(
            _ctx.TargetProcess!.ProcessId,
            TimeSpan.FromSeconds(30));
    }

    [Given("a launched process paused at entry")]
    public async Task GivenALaunchedProcessPausedAtEntry()
    {
        var testDll = TestTargetProcess.TestTargetDllPath;
        await _ctx.SessionManager.LaunchAsync(testDll, stopAtEntry: true, timeout: TimeSpan.FromSeconds(30));
    }

    [When("I attach the debugger to the test target")]
    public async Task WhenIAttachTheDebuggerToTheTestTarget()
    {
        await _ctx.SessionManager.AttachAsync(
            _ctx.TargetProcess!.ProcessId,
            TimeSpan.FromSeconds(30));
    }

    [When("I detach the debugger")]
    public async Task WhenIDetachTheDebugger()
    {
        await _ctx.SessionManager.DisconnectAsync();
    }

    [When(@"I launch the test target with stop at entry")]
    public async Task WhenILaunchTheTestTargetWithStopAtEntry()
    {
        var testDll = TestTargetProcess.TestTargetDllPath;
        await _ctx.SessionManager.LaunchAsync(testDll, stopAtEntry: true, timeout: TimeSpan.FromSeconds(30));
    }

    [When("I continue execution")]
    public async Task WhenIContinueExecution()
    {
        await _ctx.SessionManager.ContinueAsync();
    }

    [Then(@"the session state should be ""(.*)""")]
    public void ThenTheSessionStateShouldBe(string expectedState)
    {
        var expected = Enum.Parse<SessionState>(expectedState, ignoreCase: true);
        var actual = _ctx.SessionManager.GetCurrentState();
        actual.Should().Be(expected);
    }

    [Then("the target process should still be running")]
    public void ThenTheTargetProcessShouldStillBeRunning()
    {
        _ctx.TargetProcess.Should().NotBeNull();
        _ctx.TargetProcess!.IsRunning.Should().BeTrue();
    }

    [Then(@"the session should have launch mode ""(.*)""")]
    public void ThenTheSessionShouldHaveLaunchMode(string expectedMode)
    {
        var expected = Enum.Parse<LaunchMode>(expectedMode, ignoreCase: true);
        var session = _ctx.SessionManager.CurrentSession;
        session.Should().NotBeNull();
        session!.LaunchMode.Should().Be(expected);
    }

    [Then(@"the session pause reason should be ""(.*)""")]
    public void ThenTheSessionPauseReasonShouldBe(string expectedReason)
    {
        var expected = Enum.Parse<PauseReason>(expectedReason, ignoreCase: true);
        var session = _ctx.SessionManager.CurrentSession;
        session.Should().NotBeNull();
        session!.PauseReason.Should().Be(expected);
    }

    [Then("the process ID should be positive")]
    public void ThenTheProcessIdShouldBePositive()
    {
        var session = _ctx.SessionManager.CurrentSession;
        session.Should().NotBeNull();
        session!.ProcessId.Should().BePositive();
    }

    [Then(@"getting stack trace should fail with ""(.*)""")]
    public void ThenGettingStackTraceShouldFailWith(string expectedMessage)
    {
        var act = () => _ctx.SessionManager.GetStackFrames();
        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{expectedMessage}*");
    }
}
