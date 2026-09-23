namespace Metrado.Revit2027.Tests;

/// <summary>
/// Pins which model the export works beside, and what it says when there is
/// none. A workshared local copy is per user: a criteria file kept with the
/// project sits beside the central model, and a budget priced beside one
/// estimator's copy would differ from everyone else's.
/// </summary>
public sealed class ModelLocationTests
{
    private const string Local = @"C:\Users\ana\Documents\Office Building_ana.rvt";

    private const string Central = @"\\server\projects\Office\Office Building.rvt";

    [Fact]
    public void ASavedModelIsWorkedBesideItself()
    {
        ModelLocation location = ModelLocation.Of(@"C:\Projects\Office\Office Building.rvt", centralPath: null, isInCloud: false);

        Assert.Equal(@"C:\Projects\Office\Office Building.rvt", location.Path);
        Assert.Null(location.Refusal);
    }

    [Fact]
    public void AWorksharedLocalCopyIsWorkedBesideItsCentral()
    {
        ModelLocation location = ModelLocation.Of(Local, Central, isInCloud: false);

        Assert.Equal(Central, location.Path);
        Assert.Null(location.Refusal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AModelNeverSavedIsAskedToBeSaved(string? pathName)
    {
        ModelLocation location = ModelLocation.Of(pathName, centralPath: null, isInCloud: false);

        Assert.Null(location.Path);
        Assert.StartsWith("Save the model first.", location.Refusal);
    }

    /// <summary>A cloud model is already saved; saving again never changes the answer, a copy in a folder does.</summary>
    [Fact]
    public void ACloudModelIsToldToCopyItToAFolderNotToSaveIt()
    {
        ModelLocation location = ModelLocation.Of("Autodesk Docs://Project/Office Building.rvt", "Autodesk Docs://Project/Office Building.rvt", isInCloud: true);

        Assert.Null(location.Path);
        Assert.Contains("cloud", location.Refusal);
        Assert.Contains("copy", location.Refusal);
        Assert.DoesNotContain("Save the model first", location.Refusal);
    }

    /// <summary>
    /// A central on Revit Server has no folder beside it. The export names it
    /// and stops, rather than fall back to the local copy the team never sees.
    /// </summary>
    [Theory]
    [InlineData(Local, "RSN://server/Office Building.rvt", "RSN://server/Office Building.rvt")]
    [InlineData("BIM 360://Project/Office Building.rvt", null, "BIM 360://Project/Office Building.rvt")]
    public void APathWithNoFolderOnDiskIsNamedAndNothingIsWritten(string pathName, string? centralPath, string named)
    {
        ModelLocation location = ModelLocation.Of(pathName, centralPath, isInCloud: false);

        Assert.Null(location.Path);
        Assert.Contains(named, location.Refusal);
        Assert.Contains("copy", location.Refusal);
    }
}
