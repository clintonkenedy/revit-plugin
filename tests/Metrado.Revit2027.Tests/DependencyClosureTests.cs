using System.Text.Json;

namespace Metrado.Revit2027.Tests;

/// <summary>
/// Pins the startup check that the add-in's third-party closure was deployed.
///
/// In an isolated load context nothing another add-in loaded can stand in for
/// a missing assembly, so an incomplete deployment fails on the first click
/// that touches the workbook writer, far from its cause. The check moves that
/// failure to startup and names what is missing.
/// </summary>
public sealed class DependencyClosureTests : IDisposable
{
    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("metrado-closure-");

    public void Dispose() => _folder.Delete(recursive: true);

    /// <summary>
    /// The list is compared with what the build actually resolves, not with
    /// the design's list: that list named <c>RBush.Signed</c>, which is the
    /// package, while the assembly it ships is <c>RBush.dll</c>. Checking the
    /// package name would have failed every startup of a correct deployment.
    /// </summary>
    [Fact]
    public void TheRequiredAssembliesAreExactlyTheAddInsThirdPartyRuntimeClosure()
    {
        Assert.Equal(ThirdPartyClosureFromDepsFile(), DependencyClosure.RequiredAssemblies.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ACompleteDeploymentIsMissingNothing()
    {
        Deploy(DependencyClosure.RequiredAssemblies);

        Assert.Empty(DependencyClosure.MissingFrom(_folder.FullName));
        DependencyClosure.Verify(_folder.FullName);
    }

    public static TheoryData<string> EachRequiredAssembly() => [.. DependencyClosure.RequiredAssemblies];

    [Theory]
    [MemberData(nameof(EachRequiredAssembly))]
    public void EachAbsentAssemblyIsReportedByName(string absent)
    {
        Deploy(DependencyClosure.RequiredAssemblies.Where(name => name != absent));

        Assert.Equal([absent], DependencyClosure.MissingFrom(_folder.FullName));
    }

    /// <summary>
    /// The failure names every missing assembly and the folder it looked in,
    /// and nothing that is present, so the message is the whole diagnosis.
    /// </summary>
    [Fact]
    public void VerifyFailsNamingEveryMissingAssemblyAndTheFolder()
    {
        string[] absent = ["ClosedXML", "RBush"];
        Deploy(DependencyClosure.RequiredAssemblies.Except(absent));

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(
            () => DependencyClosure.Verify(_folder.FullName));

        Assert.Contains(_folder.FullName, failure.Message);
        Assert.Contains("ClosedXML.dll", failure.Message);
        Assert.Contains("RBush.dll", failure.Message);
        Assert.DoesNotContain("SixLabors.Fonts.dll", failure.Message);
    }

    /// <summary>
    /// A file that merely contains the name is not the assembly: the check is
    /// by exact file name, so <c>ClosedXML.Parser.dll</c> cannot stand in for
    /// <c>ClosedXML.dll</c>.
    /// </summary>
    [Fact]
    public void AnAssemblyIsPresentOnlyUnderItsExactFileName()
    {
        Deploy(DependencyClosure.RequiredAssemblies.Where(name => name != "ClosedXML"));
        File.WriteAllBytes(Path.Combine(_folder.FullName, "ClosedXML.dll.bak"), []);

        Assert.Equal(["ClosedXML"], DependencyClosure.MissingFrom(_folder.FullName));
    }

    private void Deploy(IEnumerable<string> assemblies)
    {
        foreach (string name in assemblies)
        {
            File.WriteAllBytes(Path.Combine(_folder.FullName, name + ".dll"), []);
        }
    }

    /// <summary>
    /// Walks <c>Metrado.Revit2027.deps.json</c> from the add-in's own entry and
    /// collects the runtime assemblies of every package it reaches. Project
    /// references are Metrado's own; the Revit API contributes none, since it
    /// is referenced for compilation only.
    /// </summary>
    private static string[] ThirdPartyClosureFromDepsFile()
    {
        using JsonDocument deps = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "AddIn", "Metrado.Revit2027.deps.json")));
        JsonElement target = deps.RootElement.GetProperty("targets").EnumerateObject().Single().Value;
        JsonElement libraries = deps.RootElement.GetProperty("libraries");

        Dictionary<string, JsonElement> byName = target.EnumerateObject()
            .ToDictionary(library => library.Name.Split('/')[0], library => library.Value);
        Dictionary<string, string> kinds = libraries.EnumerateObject()
            .ToDictionary(library => library.Name.Split('/')[0], library => library.Value.GetProperty("type").GetString()!);

        HashSet<string> assemblies = [];
        HashSet<string> visited = [];
        Stack<string> pending = new(["Metrado.Revit2027"]);

        while (pending.TryPop(out string? name))
        {
            if (!visited.Add(name))
            {
                continue;
            }

            JsonElement library = byName[name];

            if (kinds[name] == "package" && library.TryGetProperty("runtime", out JsonElement runtime))
            {
                assemblies.UnionWith(runtime.EnumerateObject().Select(asset => Path.GetFileNameWithoutExtension(asset.Name)));
            }

            if (library.TryGetProperty("dependencies", out JsonElement dependencies))
            {
                foreach (JsonProperty dependency in dependencies.EnumerateObject())
                {
                    pending.Push(dependency.Name);
                }
            }
        }

        return [.. assemblies.Order(StringComparer.Ordinal)];
    }
}
