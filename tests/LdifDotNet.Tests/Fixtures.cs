namespace LdifDotNet.Tests;

internal static class Fixtures
{
    public static string Root { get; } = Path.Combine(AppContext.BaseDirectory, "fixtures");

    public static string PathOf(params string[] parts) =>
        Path.Combine([Root, .. parts]);

    public static IEnumerable<string> AllLdifFiles() =>
        Directory.EnumerateFiles(Root, "*.ldif", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(Root, p))
            .OrderBy(p => p, StringComparer.Ordinal);
}
