using System.Text.RegularExpressions;

namespace QLHV.Infrastructure.Sync;

internal static partial class QlhvImportConstraintEvaluator
{
    public static SourceProfileConstraintEvaluation Evaluate(
        IEnumerable<string?> definitions,
        string sourceProfileCode)
    {
        var definitionRows = definitions.ToArray();
        if (definitionRows.Length == 0)
        {
            return new SourceProfileConstraintEvaluation(Exists: false, AllowsSourceProfile: true);
        }

        if (definitionRows.Any(string.IsNullOrWhiteSpace))
        {
            return new SourceProfileConstraintEvaluation(Exists: true, AllowsSourceProfile: false);
        }

        var activeDefinitions = definitionRows.Select(definition => definition!).ToArray();

        var allowedByEveryConstraint = activeDefinitions.All(definition =>
            IsRecognizedPositiveAllowList(definition) &&
            StringLiteralRegex()
                .Matches(definition)
                .Select(match => match.Groups[1].Value.Replace("''", "'", StringComparison.Ordinal))
                .Any(value => string.Equals(value, sourceProfileCode, StringComparison.Ordinal)));

        return new SourceProfileConstraintEvaluation(
            Exists: true,
            AllowsSourceProfile: allowedByEveryConstraint);
    }

    private static bool IsRecognizedPositiveAllowList(string definition)
    {
        var normalized = ConstraintFormattingRegex()
            .Replace(definition, string.Empty)
            .ToUpperInvariant();
        return PositiveInAllowListShapeRegex().IsMatch(normalized) ||
               PositiveEqualityAllowListShapeRegex().IsMatch(normalized);
    }

    [GeneratedRegex("N?'((?:''|[^'])*)'", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StringLiteralRegex();

    [GeneratedRegex(@"[\s\[\]\(\)]", RegexOptions.CultureInvariant)]
    private static partial Regex ConstraintFormattingRegex();

    [GeneratedRegex(
        @"^(?:SOURCEPROFILECODEISNULLOR)?SOURCEPROFILECODEINN?'(?:''|[^'])*'(?:,N?'(?:''|[^'])*')*$",
        RegexOptions.CultureInvariant)]
    private static partial Regex PositiveInAllowListShapeRegex();

    [GeneratedRegex(
        @"^(?:(?:SOURCEPROFILECODEISNULL)|(?:SOURCEPROFILECODE=N?'(?:''|[^'])*'))(?:OR(?:(?:SOURCEPROFILECODEISNULL)|(?:SOURCEPROFILECODE=N?'(?:''|[^'])*')))*$",
        RegexOptions.CultureInvariant)]
    private static partial Regex PositiveEqualityAllowListShapeRegex();
}

internal sealed record SourceProfileConstraintEvaluation(
    bool Exists,
    bool AllowsSourceProfile);
