using QLHV.Infrastructure.Sync;

namespace QLHV.Tests.Sync;

public sealed class QlhvImportConstraintEvaluatorTests
{
    [Fact]
    public void No_constraint_allows_the_profile()
    {
        var result = QlhvImportConstraintEvaluator.Evaluate(Array.Empty<string>(), "CSDT_OTO");

        Assert.False(result.Exists);
        Assert.True(result.AllowsSourceProfile);
    }

    [Fact]
    public void Constraint_with_unreadable_definition_is_blocked_conservatively()
    {
        var result = QlhvImportConstraintEvaluator.Evaluate([null], "CSDT_OTO");

        Assert.True(result.Exists);
        Assert.False(result.AllowsSourceProfile);
    }

    [Fact]
    public void Existing_constraint_without_oto_literal_blocks_oto()
    {
        var result = QlhvImportConstraintEvaluator.Evaluate(
            ["([SourceProfileCode] IS NULL OR [SourceProfileCode] IN (N'DATA_V1', N'DATA_V2'))"],
            "CSDT_OTO");

        Assert.True(result.Exists);
        Assert.False(result.AllowsSourceProfile);
    }

    [Fact]
    public void Existing_constraint_with_oto_literal_allows_oto()
    {
        var result = QlhvImportConstraintEvaluator.Evaluate(
            ["([SourceProfileCode] IS NULL OR [SourceProfileCode] IN (N'DATA_V1', N'DATA_V2', N'CSDT_MOTO', N'CSDT_OTO'))"],
            "CSDT_OTO");

        Assert.True(result.Exists);
        Assert.True(result.AllowsSourceProfile);
    }

    [Fact]
    public void Lowercase_literal_does_not_assume_a_case_insensitive_database_collation()
    {
        var result = QlhvImportConstraintEvaluator.Evaluate(
            ["([SourceProfileCode] IS NULL OR [SourceProfileCode] IN (N'csdt_oto'))"],
            "CSDT_OTO");

        Assert.True(result.Exists);
        Assert.False(result.AllowsSourceProfile);
    }

    [Fact]
    public void Sql_server_or_equality_metadata_allows_oto_regardless_of_term_order()
    {
        var result = QlhvImportConstraintEvaluator.Evaluate(
            ["([SourceProfileCode]=N'CSDT_OTO' OR [SourceProfileCode] IS NULL OR [SourceProfileCode]=N'DATA_V2' OR [SourceProfileCode]=N'CSDT_MOTO' OR [SourceProfileCode]=N'DATA_V1')"],
            "CSDT_OTO");

        Assert.True(result.Exists);
        Assert.True(result.AllowsSourceProfile);
    }

    [Fact]
    public void Negative_constraint_with_oto_literal_does_not_falsely_allow_oto()
    {
        var result = QlhvImportConstraintEvaluator.Evaluate(
            ["([SourceProfileCode] <> N'CSDT_OTO')"],
            "CSDT_OTO");

        Assert.True(result.Exists);
        Assert.False(result.AllowsSourceProfile);
    }

    [Fact]
    public void Unrecognized_extra_predicate_is_blocked_conservatively()
    {
        var result = QlhvImportConstraintEvaluator.Evaluate(
            ["(LEN([SourceProfileCode]) > 0 OR [SourceProfileCode] IN (N'CSDT_OTO'))"],
            "CSDT_OTO");

        Assert.True(result.Exists);
        Assert.False(result.AllowsSourceProfile);
    }
}
