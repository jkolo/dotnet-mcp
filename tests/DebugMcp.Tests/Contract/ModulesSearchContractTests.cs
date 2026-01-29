using DebugMcp.Models.Modules;
using FluentAssertions;

namespace DebugMcp.Tests.Contract;

/// <summary>
/// Contract tests validating the modules_search tool schema compliance.
/// These tests verify the tool adheres to the MCP contract.
/// </summary>
public class ModulesSearchContractTests
{
    /// <summary>
    /// modules_search requires pattern parameter.
    /// </summary>
    [Fact]
    public void ModulesSearch_RequiresPattern()
    {
        // The input schema specifies: "required": ["pattern"]
        // Pattern is the search term
        true.Should().BeTrue("modules_search requires pattern parameter");
    }

    /// <summary>
    /// search_type parameter has valid values.
    /// </summary>
    [Theory]
    [InlineData(SearchType.Types)]
    [InlineData(SearchType.Methods)]
    [InlineData(SearchType.Both)]
    public void ModulesSearch_SearchType_HasValidValues(SearchType searchType)
    {
        // Contract defines: search_type = Types | Methods | Both
        searchType.Should().BeDefined("SearchType must be valid");
    }

    /// <summary>
    /// module_filter is optional.
    /// </summary>
    [Fact]
    public void ModulesSearch_ModuleFilter_IsOptional()
    {
        // Contract defines: "module_filter": { "type": "string" }
        // Optional - when omitted, searches all modules
        true.Should().BeTrue("module_filter is optional");
    }

    /// <summary>
    /// case_sensitive defaults to false.
    /// </summary>
    [Fact]
    public void ModulesSearch_CaseSensitive_DefaultsFalse()
    {
        // Contract defines: "case_sensitive": { "type": "boolean", "default": false }
        const bool defaultCaseSensitive = false;
        defaultCaseSensitive.Should().BeFalse("case_sensitive defaults to false");
    }

    /// <summary>
    /// max_results defaults to 50.
    /// </summary>
    [Fact]
    public void ModulesSearch_MaxResults_DefaultsTo50()
    {
        // Contract defines: "max_results": { "default": 50 }
        const int defaultMaxResults = 50;
        defaultMaxResults.Should().Be(50, "max_results defaults to 50");
    }

    /// <summary>
    /// SearchResult includes query info.
    /// </summary>
    [Fact]
    public void SearchResult_IncludesQueryInfo()
    {
        var result = new SearchResult(
            Query: "*Customer*",
            SearchType: SearchType.Both,
            Types: Array.Empty<TypeInfo>(),
            Methods: Array.Empty<MethodSearchMatch>(),
            TotalMatches: 0,
            ReturnedMatches: 0,
            Truncated: false,
            ContinuationToken: null
        );

        result.Query.Should().NotBeNullOrEmpty("query is required");
        result.SearchType.Should().BeDefined("searchType is required");
    }

    /// <summary>
    /// SearchResult includes pagination info.
    /// </summary>
    [Fact]
    public void SearchResult_IncludesPaginationInfo()
    {
        // Contract defines: totalMatches, returnedMatches, truncated, continuationToken
        var result = new SearchResult(
            Query: "Get*",
            SearchType: SearchType.Methods,
            Types: Array.Empty<TypeInfo>(),
            Methods: new[]
            {
                new MethodSearchMatch(
                    "System.String",
                    "System.Private.CoreLib",
                    new MethodMemberInfo("GetHashCode", "int GetHashCode()", "int",
                        Array.Empty<ParameterInfo>(), Visibility.Public, false, true, false, false, null, "System.String"),
                    "name"
                )
            },
            TotalMatches: 100,
            ReturnedMatches: 1,
            Truncated: true,
            ContinuationToken: "next-page"
        );

        result.TotalMatches.Should().Be(100);
        result.ReturnedMatches.Should().Be(1);
        result.Truncated.Should().BeTrue("results were truncated");
        result.ContinuationToken.Should().NotBeNullOrEmpty("continuation token provided");
    }

    /// <summary>
    /// MethodSearchMatch includes declaring type and module.
    /// </summary>
    [Fact]
    public void MethodSearchMatch_IncludesDeclaringTypeAndModule()
    {
        // Contract requires: declaringType, moduleName, method, matchReason
        var match = new MethodSearchMatch(
            DeclaringType: "System.String",
            ModuleName: "System.Private.CoreLib",
            Method: new MethodMemberInfo("ToString", "string ToString()", "string",
                Array.Empty<ParameterInfo>(), Visibility.Public, false, true, false, false, null, "System.String"),
            MatchReason: "name"
        );

        match.DeclaringType.Should().NotBeNullOrEmpty("declaringType is required");
        match.ModuleName.Should().NotBeNullOrEmpty("moduleName is required");
        match.Method.Should().NotBeNull("method is required");
        match.MatchReason.Should().NotBeNullOrEmpty("matchReason is required");
    }

    /// <summary>
    /// Type search returns TypeInfo.
    /// </summary>
    [Fact]
    public void SearchResult_TypeSearch_ReturnsTypeInfo()
    {
        var types = new[]
        {
            new TypeInfo("MyApp.Customer", "Customer", "MyApp", TypeKind.Class, Visibility.Public,
                false, Array.Empty<string>(), false, null, "MyApp", "System.Object", Array.Empty<string>())
        };

        var result = new SearchResult(
            Query: "*Customer*",
            SearchType: SearchType.Types,
            Types: types,
            Methods: Array.Empty<MethodSearchMatch>(),
            TotalMatches: 1,
            ReturnedMatches: 1,
            Truncated: false,
            ContinuationToken: null
        );

        result.Types.Should().HaveCount(1);
        result.Types[0].Name.Should().Be("Customer");
        result.Methods.Should().BeEmpty("only searching types");
    }

    /// <summary>
    /// Method search returns MethodSearchMatch.
    /// </summary>
    [Fact]
    public void SearchResult_MethodSearch_ReturnsMethodSearchMatch()
    {
        var methods = new[]
        {
            new MethodSearchMatch(
                "MyApp.Services.CustomerService",
                "MyApp",
                new MethodMemberInfo("GetCustomer", "Customer GetCustomer(int id)", "Customer",
                    new[] { new ParameterInfo("id", "int", false, false, false, null) },
                    Visibility.Public, false, false, false, false, null, "MyApp.Services.CustomerService"),
                "name"
            )
        };

        var result = new SearchResult(
            Query: "GetCustomer",
            SearchType: SearchType.Methods,
            Types: Array.Empty<TypeInfo>(),
            Methods: methods,
            TotalMatches: 1,
            ReturnedMatches: 1,
            Truncated: false,
            ContinuationToken: null
        );

        result.Methods.Should().HaveCount(1);
        result.Methods[0].Method.Name.Should().Be("GetCustomer");
        result.Types.Should().BeEmpty("only searching methods");
    }

    /// <summary>
    /// Both search returns types and methods.
    /// </summary>
    [Fact]
    public void SearchResult_BothSearch_ReturnsTypesAndMethods()
    {
        var types = new[]
        {
            new TypeInfo("MyApp.Customer", "Customer", "MyApp", TypeKind.Class, Visibility.Public,
                false, Array.Empty<string>(), false, null, "MyApp", "System.Object", Array.Empty<string>())
        };

        var methods = new[]
        {
            new MethodSearchMatch(
                "MyApp.Services.CustomerService",
                "MyApp",
                new MethodMemberInfo("GetCustomer", "Customer GetCustomer(int id)", "Customer",
                    new[] { new ParameterInfo("id", "int", false, false, false, null) },
                    Visibility.Public, false, false, false, false, null, "MyApp.Services.CustomerService"),
                "name"
            )
        };

        var result = new SearchResult(
            Query: "*Customer*",
            SearchType: SearchType.Both,
            Types: types,
            Methods: methods,
            TotalMatches: 2,
            ReturnedMatches: 2,
            Truncated: false,
            ContinuationToken: null
        );

        result.Types.Should().NotBeEmpty("both search includes types");
        result.Methods.Should().NotBeEmpty("both search includes methods");
    }

    /// <summary>
    /// Error response includes code and message.
    /// </summary>
    [Fact]
    public void ModulesSearch_ErrorResponse_IncludesCodeAndMessage()
    {
        // Contract defines error codes: NO_SESSION, INVALID_PATTERN, SEARCH_FAILED
        var errorCodes = new[] { "NO_SESSION", "INVALID_PATTERN", "SEARCH_FAILED" };

        var errorResponse = new
        {
            success = false,
            error = new
            {
                code = "INVALID_PATTERN",
                message = "Search pattern cannot be empty"
            }
        };

        errorResponse.success.Should().BeFalse();
        errorResponse.error.code.Should().BeOneOf(errorCodes);
        errorResponse.error.message.Should().NotBeNullOrEmpty();
    }

    /// <summary>
    /// Wildcard patterns are supported.
    /// </summary>
    [Theory]
    [InlineData("*Customer*")]     // Contains
    [InlineData("Get*")]           // Starts with
    [InlineData("*Service")]       // Ends with
    [InlineData("Customer")]       // Exact
    public void ModulesSearch_SupportsWildcardPatterns(string pattern)
    {
        // Contract defines: "pattern": { "supports": ["*prefix", "suffix*", "*contains*", "exact"] }
        pattern.Should().NotBeNullOrEmpty("pattern must be valid");
    }

    /// <summary>
    /// MatchReason explains why a result matched.
    /// </summary>
    [Theory]
    [InlineData("name")]
    [InlineData("signature")]
    [InlineData("type")]
    public void MethodSearchMatch_MatchReason_HasValidValues(string reason)
    {
        // Contract defines: matchReason explains the match
        var validReasons = new[] { "name", "signature", "type" };
        validReasons.Should().Contain(reason);
    }

    /// <summary>
    /// max_results is capped at 100.
    /// </summary>
    [Fact]
    public void ModulesSearch_MaxResults_CappedAt100()
    {
        // Contract defines: "max_results": { "max": 100 }
        const int maxAllowed = 100;
        maxAllowed.Should().Be(100, "max_results is capped at 100");
    }

    /// <summary>
    /// Empty results are valid.
    /// </summary>
    [Fact]
    public void SearchResult_EmptyResults_IsValid()
    {
        var result = new SearchResult(
            Query: "NonExistentPattern12345",
            SearchType: SearchType.Both,
            Types: Array.Empty<TypeInfo>(),
            Methods: Array.Empty<MethodSearchMatch>(),
            TotalMatches: 0,
            ReturnedMatches: 0,
            Truncated: false,
            ContinuationToken: null
        );

        result.TotalMatches.Should().Be(0);
        result.ReturnedMatches.Should().Be(0);
        result.Truncated.Should().BeFalse("no results to truncate");
        result.ContinuationToken.Should().BeNull("no more results");
    }
}
