using System.Text.RegularExpressions;

namespace QLHV.Tests.Assignments;

internal static class AssignmentSourceTestHelper
{
    public static string Read(params string[] path) => File.ReadAllText(
        Path.Combine([RepositoryRoot(), .. path]));

    public static string ReadAll(string relativeDirectory, string searchPattern = "*.cs") =>
        string.Join(
            '\n',
            Directory.EnumerateFiles(
                    Path.Combine(RepositoryRoot(), relativeDirectory.Replace('/', Path.DirectorySeparatorChar)),
                    searchPattern,
                    SearchOption.AllDirectories)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .Select(File.ReadAllText));

    public static string Section(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"Missing section start: {start}");
        var endIndex = source.IndexOf(end, startIndex + start.Length, StringComparison.Ordinal);
        Assert.True(endIndex > startIndex, $"Missing section end: {end}");
        return source[startIndex..endIndex];
    }

    public static void AssertNoSqlMutation(string source, params string[] tables)
    {
        foreach (var table in tables)
        {
            Assert.DoesNotMatch(
                new Regex(
                    $@"(?is)\b(?:INSERT\s+(?:INTO\s+)?|UPDATE\s+|DELETE\s+FROM\s+)\[?dbo\]?\.\[?{Regex.Escape(table)}\]?\b",
                    RegexOptions.CultureInvariant,
                    TimeSpan.FromSeconds(1)),
                source);
        }
    }

    public static string RepositoryRoot() => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..",
        "..",
        "..",
        "..",
        ".."));
}
