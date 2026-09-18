using Metrado.Domain;

namespace Metrado.Excel.Tests;

/// <summary>
/// <c>excel-budget-export</c> "Deterministic Output Ordering" requires that "for
/// identical input the writer SHALL produce identical row ordering", and its second
/// scenario asks for something stronger — "GIVEN a stored reference workbook for a
/// fixture data set ... THEN the produced content matches the reference".
/// <para>
/// A stored reference is only viable if writing the same data twice produces the
/// same thing. It does, but not at the level of the file: two writes differ in
/// their raw bytes, always, for reasons this codebase neither causes nor can fix.
/// What IS reproducible is every part that carries spreadsheet content, and that is
/// what these tests pin — together with the reason the exclusion is necessary, so
/// nobody later "tightens" this to a raw byte comparison and gets a test that fails
/// on the clock.
/// </para>
/// </summary>
public sealed class WorkbookStabilityTests
{
    /// <summary>Two capitulos, three partidas, an uncoded element and both boundary modes.</summary>
    private static TakeoffResult Sample() =>
        TakeoffFixture.ResultOf(
            TakeoffFixture.Line("wall-b", "C1010", metrado: 4.25),
            TakeoffFixture.Line("wall-a", "C1010", metrado: 10.5),
            TakeoffFixture.Line(
                "wall-c",
                "C2020",
                metrado: 3.25,
                appliedMode: BoundaryMode.Inclusive),
            TakeoffFixture.Line("floor-a", "C3030", metrado: 7.75, capitulo: "Floors"),
            TakeoffFixture.Line("slab-x", UnclassifiedResolver.Code, capitulo: "Floors"));

    [Fact]
    public void EveryPartCarryingSpreadsheetContentIsReproducedByteForByte()
    {
        byte[] first = WorkbookPackage.Written(Sample());
        Thread.Sleep(1100);
        byte[] second = WorkbookPackage.Written(Sample());

        IReadOnlyDictionary<string, string> left = WorkbookPackage.Payload(first);
        IReadOnlyDictionary<string, string> right = WorkbookPackage.Payload(second);

        // Named exhaustively rather than counted: an empty or truncated projection
        // would otherwise agree with itself and prove nothing. This is also the
        // closure assertion — a library upgrade that added a part, or moved one out
        // of the payload, fails here instead of silently widening the exclusion.
        Assert.Equal(
            [
                "[Content_Types].xml",
                "docProps/app.xml",
                "xl/_rels/workbook.xml.rels",
                "xl/sharedStrings.xml",
                "xl/styles.xml",
                "xl/theme/theme1.xml",
                "xl/workbook.xml",
                "xl/worksheets/sheet1.xml",
                "xl/worksheets/sheet2.xml",
            ],
            [.. left.Keys.Order(StringComparer.Ordinal)]);

        Assert.Equal([.. left.Keys.Order(StringComparer.Ordinal)], [.. right.Keys.Order(StringComparer.Ordinal)]);
        Assert.All(left, part => Assert.Equal(part.Value, right[part.Key]));
    }

    [Fact]
    public void TheCellValuesThemselvesAreAmongThePartsThatAreReproduced()
    {
        byte[] first = WorkbookPackage.Written(Sample());
        byte[] second = WorkbookPackage.Written(Sample());

        IReadOnlyDictionary<string, string> payload = WorkbookPackage.Payload(first);

        // Guards the comparison: parts that somehow arrived empty would still equal
        // their twins, and that agreement would say nothing. ClosedXML interns every
        // string in the shared string table and leaves the sheet holding indices, so
        // the text and the numbers have to be looked for in different parts.
        Assert.Contains("wall-a", payload["xl/sharedStrings.xml"], StringComparison.Ordinal);
        Assert.Contains(
            "Measurement lines exported",
            payload["xl/sharedStrings.xml"],
            StringComparison.Ordinal);
        Assert.Contains("10.5", payload["xl/worksheets/sheet1.xml"], StringComparison.Ordinal);

        IReadOnlyDictionary<string, string> again = WorkbookPackage.Payload(second);

        Assert.Equal(payload["xl/sharedStrings.xml"], again["xl/sharedStrings.xml"]);
        Assert.Equal(payload["xl/worksheets/sheet1.xml"], again["xl/worksheets/sheet1.xml"]);
    }

    [Fact]
    public void TheRawFileBytesAreNotReproducibleBecauseThePackagingMetadataIsMintedFresh()
    {
        byte[] first = WorkbookPackage.Written(Sample());
        Thread.Sleep(1100);
        byte[] second = WorkbookPackage.Written(Sample());

        // This is why the payload projection exists. Asserting it keeps the
        // exclusion honest: if a future library made the whole file reproducible,
        // this test fails and the exclusion can be deleted rather than carried
        // forward as folklore.
        Assert.NotEqual(first, second);
        Assert.NotEqual(
            WorkbookPackage.PackagingMetadataNames(first),
            WorkbookPackage.PackagingMetadataNames(second));
    }

    [Fact]
    public void TheExcludedRootRelationshipsStillDeclareTheSameRelationshipTypes()
    {
        byte[] first = WorkbookPackage.Written(Sample());
        byte[] second = WorkbookPackage.Written(Sample());

        IReadOnlyList<string> types = WorkbookPackage.RootRelationshipTypes(first);

        // The root relationships part is excluded from the payload because its ids
        // and its core-properties target are random. Its shape is not, and leaving
        // it entirely unexamined would let a workbook stop declaring the document
        // it is a package for while still passing every other test here.
        Assert.Equal(
            [
                "http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties",
                "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument",
                "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties",
            ],
            types);

        Assert.Equal(types, WorkbookPackage.RootRelationshipTypes(second));
    }
}
