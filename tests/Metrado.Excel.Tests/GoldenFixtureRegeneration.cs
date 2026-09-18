namespace Metrado.Excel.Tests;

/// <summary>
/// A maintenance tool, not a test. Rewrites the stored reference workbooks from
/// the current writer.
/// </summary>
/// <remarks>
/// It asserts nothing, and it does nothing at all unless
/// <see cref="Switch"/> is set to <c>1</c>, so a normal test run can never refresh
/// the references it is supposed to be checked against. A golden that the suite
/// regenerates on its way past agrees with whatever the writer currently does and
/// catches nothing.
/// <para>
/// Regenerating is meant to be a deliberate act with a reviewed diff:
/// <code>METRADO_REGENERATE_GOLDENS=1 dotnet test tests/Metrado.Excel.Tests</code>
/// then read what moved in the assertion failures it was meant to resolve, and
/// commit the new references alongside the change that justifies them.
/// </para>
/// </remarks>
public sealed class GoldenFixtureRegeneration
{
    /// <summary>The environment variable that arms the tool.</summary>
    internal const string Switch = "METRADO_REGENERATE_GOLDENS";

    [Fact]
    public void RewritesEveryReferenceWorkbookOnlyWhenExplicitlyAskedTo()
    {
        if (Environment.GetEnvironmentVariable(Switch) != "1")
        {
            return;
        }

        foreach ((string name, Domain.TakeoffResult result) in GoldenFixtures.All)
        {
            string path = GoldenWorkbook.SourcePath(name);
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, WorkbookPackage.Written(result));
        }
    }
}
