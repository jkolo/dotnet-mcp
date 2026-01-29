using DebugMcp.E2E.Support;
using DebugMcp.Models.Inspection;
using DebugMcp.Models.Memory;

namespace DebugMcp.E2E.StepDefinitions;

[Binding]
public sealed class InspectionSteps
{
    private readonly DebuggerContext _ctx;
    private ObjectInspection? _lastInspection;
    private MemoryRegion? _lastMemoryResult;
    private TypeLayout? _lastTypeLayout;
    private ReferencesResult? _lastReferences;

    public InspectionSteps(DebuggerContext ctx)
    {
        _ctx = ctx;
    }

    // --- When: Variables ---

    [When("I inspect local variables")]
    public void WhenIInspectLocalVariables()
    {
        _ctx.LastVariables = _ctx.SessionManager.GetVariables().ToArray();
    }

    // --- When: Object Inspection ---

    [When(@"I inspect the object ""(.*)""")]
    public async Task WhenIInspectTheObject(string expression)
    {
        _lastInspection = await _ctx.SessionManager.InspectObjectAsync(expression);
    }

    [When(@"I inspect the object ""(.*)"" with depth (\d+)")]
    public async Task WhenIInspectTheObjectWithDepth(string expression, int depth)
    {
        _lastInspection = await _ctx.SessionManager.InspectObjectAsync(expression, depth: depth);
    }

    // --- When: Memory ---

    [When(@"I read memory at the address of ""(.*)"" for (\d+) bytes")]
    public async Task WhenIReadMemoryAtTheAddressOfForBytes(string expression, int size)
    {
        var inspection = await _ctx.SessionManager.InspectObjectAsync(expression);
        inspection.IsNull.Should().BeFalse();
        _lastMemoryResult = await _ctx.SessionManager.ReadMemoryAsync(inspection.Address, size);
    }

    // --- When: Type Layout ---

    [When(@"I get the type layout for ""(.*)""")]
    public async Task WhenIGetTheTypeLayoutFor(string typeName)
    {
        _lastTypeLayout = await _ctx.SessionManager.GetTypeLayoutAsync(typeName);
    }

    [When(@"I get the type layout for ""(.*)"" including inherited fields")]
    public async Task WhenIGetTheTypeLayoutForIncludingInheritedFields(string typeName)
    {
        _lastTypeLayout = await _ctx.SessionManager.GetTypeLayoutAsync(typeName, includeInherited: true);
    }

    // --- When: References ---

    [When(@"I get outbound references for ""(.*)""")]
    public async Task WhenIGetOutboundReferencesFor(string expression)
    {
        _lastReferences = await _ctx.SessionManager.GetOutboundReferencesAsync(expression);
    }

    [When(@"I get outbound references for ""(.*)"" with max (\d+) results")]
    public async Task WhenIGetOutboundReferencesForWithMaxResults(string expression, int maxResults)
    {
        _lastReferences = await _ctx.SessionManager.GetOutboundReferencesAsync(expression, maxResults: maxResults);
    }

    // --- Then: Variables ---

    [Then("the variables should not be empty")]
    public void ThenTheVariablesShouldNotBeEmpty()
    {
        _ctx.LastVariables.Should().NotBeNullOrEmpty();
    }

    // --- Then: Object Inspection ---

    [Then("the object should not be null")]
    public void ThenTheObjectShouldNotBeNull()
    {
        _lastInspection.Should().NotBeNull();
        _lastInspection!.IsNull.Should().BeFalse();
    }

    [Then("the object should be null")]
    public void ThenTheObjectShouldBeNull()
    {
        _lastInspection.Should().NotBeNull();
        _lastInspection!.IsNull.Should().BeTrue();
    }

    [Then(@"the object type should contain ""(.*)""")]
    public void ThenTheObjectTypeShouldContain(string typeName)
    {
        _lastInspection!.TypeName.Should().Contain(typeName);
    }

    [Then("the object should have fields")]
    public void ThenTheObjectShouldHaveFields()
    {
        _lastInspection!.Fields.Should().NotBeEmpty();
    }

    // --- Then: Memory ---

    [Then("the memory result should have bytes")]
    public void ThenTheMemoryResultShouldHaveBytes()
    {
        _lastMemoryResult.Should().NotBeNull();
        _lastMemoryResult!.Bytes.Should().NotBeNullOrEmpty();
    }

    [Then(@"the memory result should have the requested size (\d+)")]
    public void ThenTheMemoryResultShouldHaveTheRequestedSize(int size)
    {
        _lastMemoryResult!.RequestedSize.Should().Be(size);
    }

    [Then(@"the memory result actual size should be at most (\d+)")]
    public void ThenTheMemoryResultActualSizeShouldBeAtMost(int maxSize)
    {
        _lastMemoryResult!.ActualSize.Should().BeLessThanOrEqualTo(maxSize);
    }

    // --- Then: Type Layout ---

    [Then(@"the type layout should have name containing ""(.*)""")]
    public void ThenTheTypeLayoutShouldHaveNameContaining(string name)
    {
        _lastTypeLayout.Should().NotBeNull();
        _lastTypeLayout!.TypeName.Should().Contain(name);
    }

    [Then(@"the type total size should be greater than (\d+)")]
    public void ThenTheTypeTotalSizeShouldBeGreaterThan(int size)
    {
        _lastTypeLayout!.TotalSize.Should().BeGreaterThan(size);
    }

    [Then("the type should not be a value type")]
    public void ThenTheTypeShouldNotBeAValueType()
    {
        _lastTypeLayout!.IsValueType.Should().BeFalse();
    }

    [Then("the type should be a value type")]
    public void ThenTheTypeShouldBeAValueType()
    {
        _lastTypeLayout!.IsValueType.Should().BeTrue();
    }

    [Then(@"the type header size should be (\d+)")]
    public void ThenTheTypeHeaderSizeShouldBe(int size)
    {
        _lastTypeLayout!.HeaderSize.Should().Be(size);
    }

    [Then(@"the type layout should have at least (\d+) fields")]
    public void ThenTheTypeLayoutShouldHaveAtLeastFields(int count)
    {
        _lastTypeLayout!.Fields.Count.Should().BeGreaterThanOrEqualTo(count);
    }

    // --- Then: References ---

    [Then(@"the reference result target type should contain ""(.*)""")]
    public void ThenTheReferenceResultTargetTypeShouldContain(string typeName)
    {
        _lastReferences.Should().NotBeNull();
        _lastReferences!.TargetType.Should().Contain(typeName);
    }

    [Then(@"the outbound reference count should be at most (\d+)")]
    public void ThenTheOutboundReferenceCountShouldBeAtMost(int maxCount)
    {
        _lastReferences!.Outbound.Count.Should().BeLessThanOrEqualTo(maxCount);
    }
}
