using DebugMcp.E2E.Support;
using DebugMcp.Models;

namespace DebugMcp.E2E.StepDefinitions;

[Binding]
public sealed class SteppingSteps
{
    private readonly DebuggerContext _ctx;

    public SteppingSteps(DebuggerContext ctx)
    {
        _ctx = ctx;
    }

    [When("I step over")]
    public async Task WhenIStepOver()
    {
        await _ctx.SessionManager.StepAsync(StepMode.Over);
    }

    [When("I step into")]
    public async Task WhenIStepInto()
    {
        await _ctx.SessionManager.StepAsync(StepMode.In);
    }

    [When("I step out")]
    public async Task WhenIStepOut()
    {
        await _ctx.SessionManager.StepAsync(StepMode.Out);
    }

    [Then(@"continuing execution should fail with ""(.*)""")]
    public async Task ThenContinuingExecutionShouldFailWith(string expectedMessage)
    {
        var act = async () => await _ctx.SessionManager.ContinueAsync();
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*{expectedMessage}*");
    }

    [Then(@"stepping over should fail with ""(.*)""")]
    public async Task ThenSteppingOverShouldFailWith(string expectedMessage)
    {
        var act = async () => await _ctx.SessionManager.StepAsync(StepMode.Over);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*{expectedMessage}*");
    }

    [Then(@"the current stack frame should be in method ""(.*)""")]
    public void ThenTheCurrentStackFrameShouldBeInMethod(string methodName)
    {
        var (frames, _) = _ctx.SessionManager.GetStackFrames();
        frames.Should().NotBeNullOrEmpty("Expected stack trace but got none");
        frames[0].Function.Should().Contain(methodName);
    }
}
