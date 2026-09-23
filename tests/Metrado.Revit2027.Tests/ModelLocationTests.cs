namespace Metrado.Revit2027.Tests;

/// <summary>
/// Pins which model the export works beside, and what it says when there is
/// none. It is the file the estimator opened, a workshared local copy
/// included: the central's path is recorded inside the file and travels with
/// every copy, so it can name a folder the estimator never chose, one that
/// cannot be reached offline, or one every copy's export would race for.
/// </summary>
public sealed class ModelLocationTests
{
    [Theory]
    [InlineData(@"C:\Projects\Office\Office Building.rvt")]
    [InlineData(@"C:\Users\ana\Documents\Office Building_ana.rvt")]
    [InlineData(@"\\server\projects\Office\Office Building.rvt")]
    public void TheModelIsTheFileTheEstimatorOpened(string pathName)
    {
        ModelLocation location = ModelLocation.Of(pathName, isInCloud: false);

        Assert.Equal(pathName, location.Path);
        Assert.Null(location.Refusal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AModelNeverSavedIsAskedToBeSaved(string? pathName)
    {
        ModelLocation location = ModelLocation.Of(pathName, isInCloud: false);

        Assert.Null(location.Path);
        Assert.StartsWith("Save the model first.", location.Refusal);
    }

    /// <summary>
    /// A cloud model is already saved; saving again never changes the answer.
    /// A copy in a folder does, and a workshared one is opened detached first.
    /// </summary>
    [Fact]
    public void ACloudModelIsToldToCopyItToAFolderNotToSaveIt()
    {
        ModelLocation location = ModelLocation.Of("Autodesk Docs://Project/Office Building.rvt", isInCloud: true);

        Assert.Null(location.Path);
        Assert.Contains("cloud", location.Refusal);
        Assert.Contains("copy", location.Refusal);
        Assert.Contains("detached", location.Refusal);
        Assert.DoesNotContain("Save the model first", location.Refusal);
    }

    [Fact]
    public void APathWithNoFolderOnDiskIsNamedAndNothingIsWritten()
    {
        ModelLocation location = ModelLocation.Of("BIM 360://Project/Office Building.rvt", isInCloud: false);

        Assert.Null(location.Path);
        Assert.Contains("BIM 360://Project/Office Building.rvt", location.Refusal);
        Assert.Contains("copy", location.Refusal);
    }
}
