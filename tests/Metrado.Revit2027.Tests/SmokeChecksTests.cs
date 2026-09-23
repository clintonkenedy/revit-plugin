namespace Metrado.Revit2027.Tests;

/// <summary>
/// Pins how each host smoke check judges what it saw in Revit. The command
/// gathers the facts; these rules decide pass, fail or skip, and say why, so
/// a smoke run's verdict never rests on a reading of Revit that no test pins.
/// </summary>
public sealed class SmokeChecksTests
{
    private const string Folder = @"C:\Users\ana\AppData\Roaming\Autodesk\Revit\Addins\2027\Metrado";

    [Fact]
    public void AnAddInLoadedInAContextOfItsOwnIsIsolated()
    {
        SmokeCheck check = SmokeChecks.Isolation(contextName: "Metrado.Revit2027", isDefaultContext: false);

        Assert.Equal(SmokeStatus.Pass, check.Status);
        Assert.Contains("Metrado.Revit2027", check.Detail);
    }

    [Fact]
    public void AnAddInLoadedInTheDefaultContextIsNotIsolated()
    {
        Assert.Equal(SmokeStatus.Fail, SmokeChecks.Isolation(contextName: "Default", isDefaultContext: true).Status);
    }

    [Fact]
    public void EveryRequiredAssemblyResolvedFromTheAddInFolderCompletesTheClosure()
    {
        SmokeCheck check = SmokeChecks.Closure(Folder, Resolved());

        Assert.Equal(SmokeStatus.Pass, check.Status);
        Assert.Contains("11", check.Detail);
    }

    /// <summary>
    /// Revit carries assemblies of the same names. One resolved from Revit's
    /// folder means isolation did not hold for it, whatever the files on disk.
    /// </summary>
    [Fact]
    public void AnAssemblyResolvedFromElsewhereBreaksTheClosureAndIsNamed()
    {
        Dictionary<string, string?> resolved = Resolved();
        resolved["DocumentFormat.OpenXml"] = @"D:\Autodesk\Revit 2027\DocumentFormat.OpenXml.dll";

        SmokeCheck check = SmokeChecks.Closure(Folder, resolved);

        Assert.Equal(SmokeStatus.Fail, check.Status);
        Assert.Contains(@"DocumentFormat.OpenXml from D:\Autodesk\Revit 2027\DocumentFormat.OpenXml.dll", check.Detail);
    }

    [Fact]
    public void AnAssemblyThatCouldNotBeResolvedBreaksTheClosureAndIsNamed()
    {
        Dictionary<string, string?> resolved = Resolved();
        resolved["ExcelNumberFormat"] = null;

        SmokeCheck check = SmokeChecks.Closure(Folder, resolved);

        Assert.Equal(SmokeStatus.Fail, check.Status);
        Assert.Contains("ExcelNumberFormat could not be loaded", check.Detail);
    }

    /// <summary>
    /// Under a Spanish UI the parameter shows as "Código de montaje"; read by
    /// its built-in identity, the code comes back all the same.
    /// </summary>
    [Fact]
    public void ACodeReadByItsBuiltInParameterIsRecordedWithTheNameTheUIShows()
    {
        SmokeCheck check = SmokeChecks.AssemblyCode("Spanish", "Código de montaje", "Muro básico: Genérico - 200 mm", "B2010");

        Assert.Equal(SmokeStatus.Pass, check.Status);
        Assert.Contains("Spanish", check.Detail);
        Assert.Contains("Código de montaje", check.Detail);
        Assert.Contains("B2010", check.Detail);
    }

    [Fact]
    public void AModelWithNoCodedTypeSkipsTheCodeCheck()
    {
        Assert.Equal(SmokeStatus.Skip, SmokeChecks.AssemblyCode("Spanish", "Código de montaje", typeName: null, code: null).Status);
    }

    [Fact]
    public void AWallWithADoorAndTwoWindowsHasEachListedOnItsOwn()
    {
        SmokeCheck check = SmokeChecks.Openings("wall-1", inserts: ["door", "window-1", "window-2"], reported: ["window-2", "door", "window-1"]);

        Assert.Equal(SmokeStatus.Pass, check.Status);
        Assert.Contains("wall-1", check.Detail);
    }

    /// <summary>A reading that merged two windows into one would still carry a total; it must not pass.</summary>
    [Fact]
    public void AnInsertTheReadingDidNotListFailsNamingIt()
    {
        SmokeCheck check = SmokeChecks.Openings("wall-1", inserts: ["door", "window-1", "window-2"], reported: ["door", "window-1"]);

        Assert.Equal(SmokeStatus.Fail, check.Status);
        Assert.Contains("window-2", check.Detail);
    }

    [Fact]
    public void AnOpeningReportedTwiceFails()
    {
        SmokeCheck check = SmokeChecks.Openings("wall-1", inserts: ["door", "window-1", "window-2"], reported: ["door", "door", "window-1", "window-2"]);

        Assert.Equal(SmokeStatus.Fail, check.Status);
    }

    [Fact]
    public void AModelWithNoSuchWallSkipsTheOpeningCheck()
    {
        Assert.Equal(SmokeStatus.Skip, SmokeChecks.Openings(wallUniqueId: null, inserts: [], reported: []).Status);
    }

    [Theory]
    [InlineData(false, false, SmokeStatus.Pass)]
    [InlineData(false, true, SmokeStatus.Fail)]
    [InlineData(true, true, SmokeStatus.Skip)]
    public void TheDocumentMustComeOutAsUnmodifiedAsItWentIn(bool before, bool after, SmokeStatus expected)
    {
        Assert.Equal(expected, SmokeChecks.Unmodified(before, after).Status);
    }

    private static Dictionary<string, string?> Resolved() =>
        DependencyClosure.RequiredAssemblies.ToDictionary(name => name, name => (string?)Path.Combine(Folder, name + ".dll"));
}
