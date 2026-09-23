using System.Text.Json;
using System.Xml.Linq;

namespace Metrado.Domain.Tests;

/// <summary>
/// Guards the macOS build loop declared by <c>Metrado.CrossPlatform.slnf</c>.
///
/// A solution filter that silently resolves to the wrong project set still
/// builds green, so a passing build proves nothing about what the filter
/// actually selected. Asserting the resolved project list is the only
/// positive proof that the cross-platform loop covers what it claims to.
///
/// This lives in Metrado.Domain.Tests because Domain.Tests is the one suite
/// guaranteed to be inside every filter this repository will define. The
/// expected list below is maintained by hand on purpose: deriving it from the
/// solution would make the assertion agree with whatever the solution says,
/// which is the one thing it exists to check.
/// </summary>
public sealed class SolutionFilterTests
{
    private const string FilterFileName = "Metrado.CrossPlatform.slnf";
    private const string WindowsOnlyProject = "Metrado.Revit2027";

    private static readonly string[] ExpectedProjects =
    [
        "src/Metrado.Configuration/Metrado.Configuration.csproj",
        "src/Metrado.Domain/Metrado.Domain.csproj",
        "src/Metrado.Excel/Metrado.Excel.csproj",
        "tests/Metrado.Configuration.Tests/Metrado.Configuration.Tests.csproj",
        "tests/Metrado.Domain.Tests/Metrado.Domain.Tests.csproj",
        "tests/Metrado.Excel.Tests/Metrado.Excel.Tests.csproj",
        "tests/Metrado.Integration.Tests/Metrado.Integration.Tests.csproj",
    ];

    /// <summary>
    /// Everything the solution carries that the filter drops. Named, not
    /// counted: a count stays green when one Windows-only project is swapped
    /// for another.
    /// </summary>
    private static readonly string[] WindowsOnlyProjects =
    [
        "src/Metrado.Revit2027/Metrado.Revit2027.csproj",
        "tests/Metrado.Revit2027.Tests/Metrado.Revit2027.Tests.csproj",
    ];

    [Fact]
    public void FilterResolvesToExactlyTheSevenCrossPlatformProjects()
    {
        string[] resolved = ReadFilteredProjects(RepositoryRoot());

        Assert.Equal(ExpectedProjects, resolved);
    }

    [Fact]
    public void FilterExcludesTheWindowsOnlyRevitProject()
    {
        string[] resolved = ReadFilteredProjects(RepositoryRoot());

        Assert.DoesNotContain(resolved, path => path.Contains(WindowsOnlyProject, StringComparison.Ordinal));
    }

    [Fact]
    public void EveryFilteredProjectExistsOnDisk()
    {
        string root = RepositoryRoot();

        foreach (string project in ReadFilteredProjects(root))
        {
            Assert.True(File.Exists(Path.Combine(root, project)), $"Filtered project not found on disk: {project}");
        }
    }

    [Fact]
    public void EveryFilteredProjectIsDeclaredInTheSolution()
    {
        string root = RepositoryRoot();
        string[] declared = ReadSolutionProjects(root);

        foreach (string project in ReadFilteredProjects(root))
        {
            Assert.Contains(project, declared);
        }
    }

    /// <summary>
    /// The Revit projects must be present in the solution and absent from the
    /// filter. Without this, deleting them entirely would leave the exclusion
    /// assertions passing for the wrong reason.
    /// </summary>
    [Fact]
    public void SolutionIsExactlyTheFilteredProjectsPlusTheWindowsOnlyOnes()
    {
        string[] declared = ReadSolutionProjects(RepositoryRoot());
        string[] expected = [.. ExpectedProjects.Concat(WindowsOnlyProjects).Order(StringComparer.Ordinal)];

        Assert.Equal(expected, declared);
    }

    private static string[] ReadFilteredProjects(string root)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, FilterFileName)));
        JsonElement solution = document.RootElement.GetProperty("solution");

        Assert.Equal("Metrado.slnx", solution.GetProperty("path").GetString());

        return [.. solution.GetProperty("projects")
            .EnumerateArray()
            .Select(project => Normalize(project.GetString()!))
            .Order(StringComparer.Ordinal)];
    }

    private static string[] ReadSolutionProjects(string root)
    {
        XDocument solution = XDocument.Load(Path.Combine(root, "Metrado.slnx"));

        return [.. solution.Descendants("Project")
            .Select(project => Normalize((string)project.Attribute("Path")!))
            .Order(StringComparer.Ordinal)];
    }

    /// <summary>
    /// Solution and filter paths are authored in the canonical Visual Studio
    /// form with backslashes. The SDK accepts either separator, so the
    /// separator itself is never evidence: normalize, then compare names.
    /// </summary>
    private static string Normalize(string path) => path.Replace('\\', '/');

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, FilterFileName)))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory.FullName;
    }
}
