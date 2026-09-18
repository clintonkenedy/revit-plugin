using System.Runtime.CompilerServices;
using ClosedXML.Excel;
using Metrado.Domain;

namespace Metrado.Excel.Tests;

/// <summary>
/// Reads the stored reference workbooks and compares the writer's output against
/// them.
/// </summary>
/// <remarks>
/// This type only ever READS a golden. Nothing here writes one, and nothing here is
/// reachable from the regeneration tool's path: a reference the assertion could
/// refresh would agree with whatever the writer currently does, which is the one
/// thing a golden exists to stop.
/// </remarks>
internal static class GoldenWorkbook
{
    private const string Directory = "Goldens";

    /// <summary>The stored bytes of a reference workbook.</summary>
    internal static byte[] Bytes(string name)
    {
        string path = Path.Combine(AppContext.BaseDirectory, Directory, name);

        Assert.True(
            File.Exists(path),
            $"The reference workbook '{name}' is missing from '{path}'. It is stored "
                + "in source control next to the tests; regenerate it deliberately with "
                + $"{GoldenFixtureRegeneration.Switch}=1 and review the diff before committing.");

        return File.ReadAllBytes(path);
    }

    /// <summary>The stored reference workbook, opened for inspection.</summary>
    internal static XLWorkbook Opened(string name) => new(new MemoryStream(Bytes(name)));

    /// <summary>
    /// Asserts the writer reproduces the stored reference for
    /// <paramref name="result"/>.
    /// </summary>
    /// <remarks>
    /// Part by part rather than file against file. Two writes of identical data
    /// never produce identical bytes — see <see cref="WorkbookPackage"/> — so a raw
    /// comparison against a stored file would fail on the clock rather than on the
    /// content, and would have to be suppressed to stay green.
    /// </remarks>
    internal static void AssertReproduces(string name, TakeoffResult result)
    {
        IReadOnlyDictionary<string, string> stored = WorkbookPackage.Payload(Bytes(name));
        IReadOnlyDictionary<string, string> produced =
            WorkbookPackage.Payload(WorkbookPackage.Written(result));

        Assert.Equal(
            [.. stored.Keys.Order(StringComparer.Ordinal)],
            [.. produced.Keys.Order(StringComparer.Ordinal)]);

        foreach (string part in stored.Keys.Order(StringComparer.Ordinal))
        {
            Assert.True(
                stored[part] == produced[part],
                $"The writer no longer reproduces '{part}' of the reference workbook "
                    + $"'{name}'. If the change is intended, regenerate the reference "
                    + "deliberately and review what moved.");
        }
    }

    /// <summary>
    /// Where a reference workbook lives in source, for the regeneration tool.
    /// </summary>
    /// <remarks>
    /// Resolved from this file's own compile-time path rather than from the working
    /// directory, so regeneration cannot quietly write a copy somewhere that the
    /// build then fails to pick up.
    /// </remarks>
    internal static string SourcePath(string name, [CallerFilePath] string caller = "") =>
        Path.Combine(Path.GetDirectoryName(caller)!, Directory, name);
}
