using System.IO.Compression;
using System.Text;
using Metrado.Domain;

namespace Metrado.Excel.Tests;

/// <summary>
/// Takes an <c>.xlsx</c> apart into the Office Open XML parts it is a ZIP of.
/// </summary>
/// <remarks>
/// The comparison unit for a workbook is the part, not the file. Two writes of
/// identical data do NOT produce identical bytes, and the reasons have nothing to
/// do with the spreadsheet: <c>System.IO.Packaging</c> mints a fresh GUID for the
/// core-properties part name on every save, stamps that part with the wall clock,
/// and generates fresh relationship identifiers in the package root relationships.
/// The ZIP entry headers also carry the current time.
/// <para>
/// Comparing raw file bytes would therefore fail for reasons no change to this
/// codebase could ever cause or fix. Comparing parts is the strongest guarantee
/// that is actually true: every part carrying spreadsheet content is byte-for-byte
/// reproducible, and <see cref="IsPackagingMetadata"/> names the only two that are
/// not, so a future library upgrade that destabilised a third part would surface as
/// a failure rather than be quietly absorbed.
/// </para>
/// </remarks>
internal static class WorkbookPackage
{
    /// <summary>The package root relationships, which carry freshly minted identifiers.</summary>
    internal const string RootRelationships = "_rels/.rels";

    /// <summary>The bytes the writer produces for <paramref name="result"/>.</summary>
    internal static byte[] Written(TakeoffResult result)
    {
        MemoryStream stream = new();
        TakeoffWorkbook.Write(result, stream);

        return stream.ToArray();
    }

    /// <summary>
    /// Whether a part is packaging bookkeeping rather than spreadsheet content.
    /// </summary>
    /// <remarks>
    /// Deliberately an exhaustive list of two rather than a pattern that could grow
    /// to cover a part that ought to have been stable. Both are produced by
    /// <c>System.IO.Packaging</c> and neither is reachable from this writer:
    /// the core-properties part is named after a fresh GUID and holds
    /// <c>dcterms:created</c> and <c>dcterms:modified</c> timestamps, and the root
    /// relationships part names that GUID alongside random relationship ids.
    /// </remarks>
    internal static bool IsPackagingMetadata(string partName) =>
        partName == RootRelationships
        || partName.EndsWith(".psmdcp", StringComparison.Ordinal);

    /// <summary>
    /// Every part that carries spreadsheet content, keyed by its part name.
    /// </summary>
    internal static IReadOnlyDictionary<string, string> Payload(byte[] workbook) =>
        Parts(workbook)
            .Where(part => !IsPackagingMetadata(part.Key))
            .ToDictionary(part => part.Key, part => part.Value, StringComparer.Ordinal);

    /// <summary>The names of the parts excluded from <see cref="Payload"/>.</summary>
    internal static IReadOnlyList<string> PackagingMetadataNames(byte[] workbook) =>
        [.. Parts(workbook).Keys.Where(IsPackagingMetadata).Order(StringComparer.Ordinal)];

    /// <summary>
    /// The relationship types declared in the package root relationships.
    /// </summary>
    /// <remarks>
    /// The root relationships part is excluded from the payload because its
    /// identifiers and its core-properties target are random, but its
    /// <em>shape</em> is not. Reading the types back keeps the excluded part from
    /// being completely unexamined: a workbook that stopped declaring its
    /// office-document relationship would still be a valid ZIP and would still pass
    /// a payload comparison.
    /// </remarks>
    internal static IReadOnlyList<string> RootRelationshipTypes(byte[] workbook) =>
        [
            .. Parts(workbook)[RootRelationships]
                .Split('"')
                .Where(fragment => fragment.StartsWith("http", StringComparison.Ordinal))
                .Where(fragment => fragment.Contains("/relationships/", StringComparison.Ordinal))
                .Order(StringComparer.Ordinal),
        ];

    private static IReadOnlyDictionary<string, string> Parts(byte[] workbook)
    {
        using ZipArchive archive = new(new MemoryStream(workbook), ZipArchiveMode.Read);

        Dictionary<string, string> parts = new(StringComparer.Ordinal);

        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            using StreamReader reader = new(entry.Open(), Encoding.UTF8);
            parts[entry.FullName] = reader.ReadToEnd();
        }

        return parts;
    }
}
