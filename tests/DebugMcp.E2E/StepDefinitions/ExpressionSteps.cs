using DebugMcp.E2E.Support;
using DebugMcp.Models.Inspection;

namespace DebugMcp.E2E.StepDefinitions;

[Binding]
public sealed class ExpressionSteps
{
    private readonly DebuggerContext _ctx;
    private EvaluationResult? _lastResult;

    public ExpressionSteps(DebuggerContext ctx)
    {
        _ctx = ctx;
    }

    // --- When: Expression Evaluation ---

    [When(@"I evaluate the expression ""(.*)""")]
    public async Task WhenIEvaluateTheExpression(string expression)
    {
        _lastResult = await _ctx.SessionManager.EvaluateAsync(expression);

        // Store in context for cross-step assertions
        if (_lastResult.Success)
        {
            _ctx.LastEvalResultValue = _lastResult.Value;
            _ctx.LastEvalResultType = _lastResult.Type;
            _ctx.LastExpressionError = null;
        }
        else
        {
            _ctx.LastEvalResultValue = null;
            _ctx.LastEvalResultType = null;
            _ctx.LastExpressionError = _lastResult.Error?.Message ?? "Unknown error";
        }
    }

    // --- Then: Success/Failure ---

    [Then("the evaluation should succeed")]
    public void ThenTheEvaluationShouldSucceed()
    {
        _lastResult.Should().NotBeNull("Evaluation was not performed");
        _lastResult!.Success.Should().BeTrue($"Evaluation failed: {_lastResult.Error?.Message}");
    }

    [Then("the evaluation should fail")]
    public void ThenTheEvaluationShouldFail()
    {
        _lastResult.Should().NotBeNull("Evaluation was not performed");
        _lastResult!.Success.Should().BeFalse("Expected evaluation to fail but it succeeded");
    }

    // --- Then: Value Assertions ---

    [Then(@"the evaluation result value should be ""(.*)""")]
    public void ThenTheEvaluationResultValueShouldBe(string expectedValue)
    {
        _lastResult!.Value.Should().Be(expectedValue);
    }

    [Then(@"the evaluation result value should contain ""(.*)""")]
    public void ThenTheEvaluationResultValueShouldContain(string expectedSubstring)
    {
        _lastResult!.Value.Should().Contain(expectedSubstring);
    }

    [Then("the evaluation result should be null")]
    public void ThenTheEvaluationResultShouldBeNull()
    {
        // A null result could mean Value is "null" string or actual null
        var isNull = _lastResult!.Value == null ||
                     _lastResult.Value.Equals("null", StringComparison.OrdinalIgnoreCase);
        isNull.Should().BeTrue($"Expected null but got: {_lastResult.Value}");
    }

    // --- Then: Error Assertions ---

    [Then(@"the evaluation error should contain ""([^""]+)"" or ""([^""]+)""")]
    public void ThenTheEvaluationErrorShouldContainOr(string option1, string option2)
    {
        _lastResult!.Error.Should().NotBeNull("Expected an error but none was present");
        var errorMessage = _lastResult.Error!.Message ?? "";
        var containsEither = errorMessage.Contains(option1, StringComparison.OrdinalIgnoreCase) ||
                             errorMessage.Contains(option2, StringComparison.OrdinalIgnoreCase);
        containsEither.Should().BeTrue($"Error '{errorMessage}' does not contain '{option1}' or '{option2}'");
    }

    [Then(@"the evaluation error should contain ""([^""]+)""$")]
    public void ThenTheEvaluationErrorShouldContain(string expectedSubstring)
    {
        _lastResult!.Error.Should().NotBeNull("Expected an error but none was present");
        _lastResult.Error!.Message.Should().Contain(expectedSubstring);
    }

    // --- Then: Type Assertions ---

    [Then(@"the evaluation result type should be ""(.*)""")]
    public void ThenTheEvaluationResultTypeShouldBe(string expectedType)
    {
        _lastResult!.Type.Should().Be(expectedType);
    }

    [Then(@"the evaluation result type should contain ""(.*)""")]
    public void ThenTheEvaluationResultTypeShouldContain(string expectedSubstring)
    {
        _lastResult!.Type.Should().Contain(expectedSubstring);
    }
}
