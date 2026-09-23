using System.Xml.Linq;

namespace Metrado.Revit2027.Tests;

/// <summary>
/// Pins the <c>Metrado.addin</c> manifest Revit reads at startup.
///
/// The manifest is read from this suite's own output folder, not from the
/// source tree, so these tests also prove the build ships it next to the add-in
/// assembly. A manifest that is correct in the repository but never lands in
/// the deployment folder registers nothing.
/// </summary>
public sealed class ManifestTests
{
    private const string ManifestFileName = "Metrado.addin";

    [Fact]
    public void TheManifestShipsWithTheBuild()
    {
        Assert.True(File.Exists(ManifestPath()), $"{ManifestFileName} is not in the build output.");
    }

    /// <summary>
    /// The isolation requirement: Revit must load the add-in into its own
    /// context instead of the shared one, so no other add-in's copy of a
    /// dependency can be the one that resolves.
    /// </summary>
    [Fact]
    public void TheManifestOptsOutOfTheSharedRevitContext()
    {
        XElement settings = ManifestSettings();

        Assert.Equal("False", (string?)settings.Element("UseRevitContext"));
    }

    /// <summary>
    /// Opting out of the shared context without naming one leaves Revit to
    /// decide what the isolated context is called, which is the collision the
    /// design's open question is about. The name is pinned, not merely present.
    /// </summary>
    [Fact]
    public void TheManifestNamesItsOwnContext()
    {
        XElement settings = ManifestSettings();

        Assert.Equal("Metrado.Revit2027", (string?)settings.Element("ContextName"));
    }

    [Fact]
    public void TheManifestRegistersExactlyOneApplicationAndNothingElse()
    {
        XElement[] addIns = [.. Manifest().Root!.Elements("AddIn")];

        XElement addIn = Assert.Single(addIns);
        Assert.Equal("Application", (string?)addIn.Attribute("Type"));
    }

    /// <summary>
    /// Revit resolves a relative path from the manifest's own folder, which is
    /// the add-ins folder every add-in shares. The assembly and the dependency
    /// closure deployed beside it live in a subfolder of their own.
    /// </summary>
    [Fact]
    public void TheApplicationPointsAtTheAddInAssemblyInItsOwnSubfolder()
    {
        Assert.Equal(@"Metrado\Metrado.Revit2027.dll", (string?)Application().Element("Assembly"));
    }

    [Fact]
    public void TheApplicationCarriesAStableIdentity()
    {
        XElement application = Application();

        Assert.True(Guid.TryParse((string?)application.Element("AddInId"), out Guid id), "AddInId is not a GUID.");
        Assert.NotEqual(Guid.Empty, id);
        Assert.False(string.IsNullOrWhiteSpace((string?)application.Element("VendorId")), "VendorId is empty.");
    }

    private static XElement Application() =>
        Manifest().Root!.Elements("AddIn").Single(addIn => (string?)addIn.Attribute("Type") == "Application");

    /// <summary>
    /// <c>ManifestSettings</c> is a child of the root, beside the <c>AddIn</c>
    /// entries — not inside one. Revit ignores it anywhere else, so the path is
    /// asserted, not searched for.
    /// </summary>
    private static XElement ManifestSettings()
    {
        XElement? settings = Manifest().Root!.Element("ManifestSettings");

        Assert.NotNull(settings);
        return settings;
    }

    private static XDocument Manifest()
    {
        XDocument manifest = XDocument.Load(ManifestPath());

        Assert.Equal("RevitAddIns", manifest.Root!.Name.LocalName);
        return manifest;
    }

    private static string ManifestPath() => Path.Combine(AppContext.BaseDirectory, ManifestFileName);
}
