using DebugMcp.E2E.Support;
using DebugMcp.Models.Modules;

namespace DebugMcp.E2E.StepDefinitions;

[Binding]
public sealed class ModuleSteps
{
    private readonly DebuggerContext _ctx;
    private SearchResult? _lastSearchResult;
    private TypesResult? _lastTypesResult;
    private TypeMembersResult? _lastMembersResult;

    public ModuleSteps(DebuggerContext ctx)
    {
        _ctx = ctx;
    }

    // --- When: Module Listing ---

    [When("I list all modules without system filter")]
    public async Task WhenIListAllModulesWithoutSystemFilter()
    {
        _ctx.LastModules = await _ctx.ProcessDebugger.GetModulesAsync(
            includeSystem: false);
    }

    [When("I list all modules including system modules")]
    public async Task WhenIListAllModulesIncludingSystemModules()
    {
        _ctx.LastModules = await _ctx.ProcessDebugger.GetModulesAsync(
            includeSystem: true);
    }

    // --- When: Search ---

    [When(@"I search for types matching ""(.*)""")]
    public async Task WhenISearchForTypesMatching(string pattern)
    {
        _lastSearchResult = await _ctx.ProcessDebugger.SearchModulesAsync(
            pattern, searchType: SearchType.Types, maxResults: 50);
        _ctx.LastSearchResult = _lastSearchResult;
    }

    // --- When: Get Types ---

    [When(@"I get types in module ""([^""]+)""$")]
    public async Task WhenIGetTypesInModule(string moduleName)
    {
        _lastTypesResult = await _ctx.ProcessDebugger.GetTypesAsync(moduleName);
        _ctx.LastTypesResult = _lastTypesResult;
    }

    [When(@"I get types in module ""([^""]+)"" with namespace filter ""([^""]+)""")]
    public async Task WhenIGetTypesInModuleWithNamespaceFilter(string moduleName, string namespaceFilter)
    {
        _lastTypesResult = await _ctx.ProcessDebugger.GetTypesAsync(
            moduleName, namespaceFilter: namespaceFilter);
        _ctx.LastTypesResult = _lastTypesResult;
    }

    // --- When: Get Members ---

    [When(@"I get members of type ""(.*)""")]
    public async Task WhenIGetMembersOfType(string typeName)
    {
        _lastMembersResult = await _ctx.ProcessDebugger.GetMembersAsync(typeName);
        _ctx.LastMembersResult = _lastMembersResult;
    }

    [When(@"I get methods of type ""(.*)""")]
    public async Task WhenIGetMethodsOfType(string typeName)
    {
        _lastMembersResult = await _ctx.ProcessDebugger.GetMembersAsync(
            typeName, memberKinds: ["methods"]);
        _ctx.LastMembersResult = _lastMembersResult;
    }

    // --- Then: Module List ---

    [Then(@"the module list should contain ""(.*)""")]
    public void ThenTheModuleListShouldContain(string moduleName)
    {
        _ctx.LastModules.Should().NotBeNull();
        _ctx.LastModules.Should().Contain(
            m => m.Name.Contains(moduleName, StringComparison.OrdinalIgnoreCase),
            $"module list should contain '{moduleName}'");
    }

    // --- Then: Search Results ---

    [Then("the search result should not be empty")]
    public void ThenTheSearchResultShouldNotBeEmpty()
    {
        _lastSearchResult.Should().NotBeNull();
        (_lastSearchResult!.Types.Length + _lastSearchResult.Methods.Length).Should().BeGreaterThan(0);
    }

    [Then(@"the search result should contain type ""(.*)""")]
    public void ThenTheSearchResultShouldContainType(string typeName)
    {
        _lastSearchResult.Should().NotBeNull();
        _lastSearchResult!.Types.Should().Contain(
            t => t.Name.Contains(typeName, StringComparison.OrdinalIgnoreCase),
            $"search result should contain type '{typeName}'");
    }

    // --- Then: Types Results ---

    [Then("the types result should not be empty")]
    public void ThenTheTypesResultShouldNotBeEmpty()
    {
        _lastTypesResult.Should().NotBeNull();
        _lastTypesResult!.Types.Should().NotBeEmpty();
    }

    [Then(@"the types result should contain type ""(.*)""")]
    public void ThenTheTypesResultShouldContainType(string typeName)
    {
        _lastTypesResult.Should().NotBeNull();
        _lastTypesResult!.Types.Should().Contain(
            t => t.Name.Contains(typeName, StringComparison.OrdinalIgnoreCase),
            $"types result should contain '{typeName}'");
    }

    // --- Then: Members Results ---

    [Then("the members result should not be empty")]
    public void ThenTheMembersResultShouldNotBeEmpty()
    {
        _lastMembersResult.Should().NotBeNull();
        var totalMembers = _lastMembersResult!.Methods.Length +
                           _lastMembersResult.Properties.Length +
                           _lastMembersResult.Fields.Length +
                           _lastMembersResult.Events.Length;
        totalMembers.Should().BeGreaterThan(0);
    }

    [Then(@"the members result should contain member ""(.*)""")]
    public void ThenTheMembersResultShouldContainMember(string memberName)
    {
        _lastMembersResult.Should().NotBeNull();
        var allMemberNames = _lastMembersResult!.Methods.Select(m => m.Name)
            .Concat(_lastMembersResult.Properties.Select(p => p.Name))
            .Concat(_lastMembersResult.Fields.Select(f => f.Name))
            .Concat(_lastMembersResult.Events.Select(e => e.Name));
        allMemberNames.Should().Contain(memberName, $"members result should contain '{memberName}'");
    }

    [Then(@"the type should have at least (\d+) members")]
    public void ThenTheTypeShouldHaveAtLeastMembers(int minCount)
    {
        _lastMembersResult.Should().NotBeNull();
        var totalMembers = _lastMembersResult!.Methods.Length +
                           _lastMembersResult.Properties.Length +
                           _lastMembersResult.Fields.Length +
                           _lastMembersResult.Events.Length;
        totalMembers.Should().BeGreaterThanOrEqualTo(minCount);
    }
}
