using DebugMcp.E2E.Support;

namespace DebugMcp.E2E.StepDefinitions;

[Binding]
public sealed class StackTraceSteps
{
    private readonly DebuggerContext _ctx;

    public StackTraceSteps(DebuggerContext ctx)
    {
        _ctx = ctx;
    }

    [When("I request the stack trace")]
    public void WhenIRequestTheStackTrace()
    {
        var result = _ctx.SessionManager.GetStackFrames();
        _ctx.LastStackTrace = result.Frames.ToArray();
    }

    [Then(@"the stack trace should contain at least (\d+) frames")]
    public void ThenTheStackTraceShouldContainAtLeastFrames(int minFrames)
    {
        _ctx.LastStackTrace.Should().NotBeNull();
        _ctx.LastStackTrace!.Length.Should().BeGreaterThanOrEqualTo(minFrames);
    }

    [Then(@"the stack trace should contain method ""(.*)""")]
    public void ThenTheStackTraceShouldContainMethod(string methodName)
    {
        _ctx.LastStackTrace.Should().NotBeNull();
        _ctx.LastStackTrace!.Should().Contain(
            f => f.Function != null && f.Function.Contains(methodName),
            $"stack trace should contain method '{methodName}'");
    }

    [Then(@"the top frame should have source location containing ""(.*)""")]
    public void ThenTheTopFrameShouldHaveSourceLocationContaining(string fileName)
    {
        _ctx.LastStackTrace.Should().NotBeNull();
        _ctx.LastStackTrace!.Should().NotBeEmpty();
        _ctx.LastStackTrace[0].Location.Should().NotBeNull();
        _ctx.LastStackTrace[0].Location!.File.Should().Contain(fileName);
    }

    // --- Threads ---

    [When("I list all threads")]
    public void WhenIListAllThreads()
    {
        _ctx.LastThreads = _ctx.SessionManager.GetThreads();
    }

    [Then("the thread list should not be empty")]
    public void ThenTheThreadListShouldNotBeEmpty()
    {
        _ctx.LastThreads.Should().NotBeNullOrEmpty();
    }

    [Then("the thread list should have a current thread")]
    public void ThenTheThreadListShouldHaveACurrentThread()
    {
        _ctx.LastThreads.Should().NotBeNull();
        _ctx.LastThreads!.Should().Contain(t => t.IsCurrent, "at least one thread should be marked as current");
    }
}
