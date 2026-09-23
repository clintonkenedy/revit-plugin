namespace Metrado.Revit2027;

/// <summary>
/// The third-party assemblies the add-in must find in its own folder.
///
/// With <c>UseRevitContext</c> off, nothing Revit or another add-in loaded can
/// stand in for one of these, so a deployment missing any of them fails on
/// the first click that reaches the workbook writer — far from its cause.
/// <see cref="Verify"/> runs at startup instead and names what is missing.
///
/// Names are assembly file names, not package names: the <c>RBush.Signed</c>
/// package ships <c>RBush.dll</c>. Uses no Revit type, so it runs anywhere.
/// </summary>
public static class DependencyClosure
{
    public static IReadOnlyList<string> RequiredAssemblies { get; } =
    [
        "ClosedXML",
        "ClosedXML.Parser",
        "DocumentFormat.OpenXml",
        "DocumentFormat.OpenXml.Framework",
        "ExcelNumberFormat",
        "RBush",
        "SixLabors.Fonts",
        "System.IO.Packaging",
    ];

    public static IReadOnlyList<string> MissingFrom(string folder) =>
        [.. RequiredAssemblies.Where(name => !File.Exists(Path.Combine(folder, name + ".dll")))];

    /// <summary>Throws, never returns a flag: a startup that swallows this is the failure it exists to prevent.</summary>
    public static void Verify(string folder)
    {
        IReadOnlyList<string> missing = MissingFrom(folder);

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"Metrado cannot start: {missing.Count} required assemblies are missing from '{folder}': "
                + string.Join(", ", missing.Select(name => name + ".dll"))
                + ". Redeploy the add-in from a complete 'dotnet publish' output.");
        }
    }
}
