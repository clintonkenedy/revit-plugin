using System.Reflection;
using System.Runtime.Loader;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace Metrado.Revit2027;

/// <summary>
/// The host smoke run (task 1.25): gathers, inside Revit, the facts only the
/// host can give, and lets <see cref="SmokeChecks"/> judge them. It lives in
/// the add-in's own assembly so that it runs in the add-in's own load
/// context, which is the first thing it checks. Its button is added only for
/// a development session (<see cref="MetradoApplication"/>); estimators never
/// see it. <c>ReadOnly</c>, like the export: it reads the model and writes
/// only journal comments.
/// </summary>
[Transaction(TransactionMode.ReadOnly)]
public sealed class SmokeCommand : IExternalCommand
{
    private const string Title = "Metrado smoke";

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        Document? document = commandData.Application.ActiveUIDocument?.Document;
        if (document is null)
        {
            TaskDialog.Show(Title, "Open a model first: there is no active document to read.");
            return Result.Cancelled;
        }

        bool modifiedBefore = document.IsModified;
        Autodesk.Revit.ApplicationServices.Application application = commandData.Application.Application;
        AssemblyLoadContext? context = AssemblyLoadContext.GetLoadContext(typeof(SmokeCommand).Assembly);

        List<SmokeCheck> checks =
        [
            SmokeChecks.Isolation(context?.Name, context is null || context == AssemblyLoadContext.Default, Foreign(context)),
            Closure(context),
            AssemblyCode(application, document),
            Openings(document),
        ];
        checks.Add(SmokeChecks.Unmodified(modifiedBefore, document.IsModified));

        string verdict = SmokeReport.Verdict(checks);
        IReadOnlyList<string> lines = SmokeReport.Lines(checks);
        application.WriteJournalComment($"Metrado smoke: {verdict}", true);
        foreach (string line in lines)
        {
            application.WriteJournalComment($"Metrado smoke: {line}", false);
        }

        TaskDialog dialog = new(Title)
        {
            MainInstruction = verdict,
            MainContent = string.Join(Environment.NewLine, lines),
        };
        dialog.Show();
        return Result.Succeeded;
    }

    /// <summary>
    /// Each required assembly as the add-in's own context resolves it by
    /// name, which is how the add-in's code reaches it: the location shows
    /// whether it came from the add-in's folder or from Revit's copy.
    /// </summary>
    private static SmokeCheck Closure(AssemblyLoadContext? context)
    {
        string folder = Path.GetDirectoryName(typeof(SmokeCommand).Assembly.Location)!;
        AssemblyLoadContext resolver = context ?? AssemblyLoadContext.Default;

        Dictionary<string, string?> resolved = [];
        foreach (string name in DependencyClosure.RequiredAssemblies)
        {
            try
            {
                resolved[name] = resolver.LoadFromAssemblyName(new AssemblyName(name)).Location;
            }
            catch (Exception ex) when (ex is FileNotFoundException or FileLoadException or BadImageFormatException)
            {
                resolved[name] = null;
            }
        }

        return SmokeChecks.Closure(folder, resolved);
    }

    /// <summary>
    /// Assemblies the add-in's context holds from outside the add-in's folder:
    /// what another add-in declaring the same context name brings into it.
    /// </summary>
    private static List<string> Foreign(AssemblyLoadContext? context)
    {
        string folder = Path.GetDirectoryName(typeof(SmokeCommand).Assembly.Location)!;
        return context is null
            ? []
            : [.. context.Assemblies
                .Where(assembly => !assembly.IsDynamic && assembly.Location.Length > 0)
                .Select(assembly => assembly.Location)
                .Where(location => !string.Equals(Path.GetDirectoryName(location), folder, StringComparison.OrdinalIgnoreCase))];
    }

    /// <summary>
    /// The code as extraction reads it, beside the name the running UI gives
    /// its parameter, and how many of the read walls' types carry a code,
    /// counted apart from extraction, so a broken reading fails the check.
    /// </summary>
    private static SmokeCheck AssemblyCode(Autodesk.Revit.ApplicationServices.Application application, Document document)
    {
        List<Wall> walls = [.. WallReader.Walls(document)];
        int codedTypes = walls
            .Select(wall => wall.WallType)
            .DistinctBy(type => type.UniqueId)
            .Count(type => !string.IsNullOrWhiteSpace(type.get_Parameter(BuiltInParameter.ASSEMBLY_CODE)?.AsString()));
        HostReading? coded = walls
            .Select(wall => WallReader.Read(document, wall))
            .FirstOrDefault(reading => !string.IsNullOrWhiteSpace(reading.AssemblyCode));

        return SmokeChecks.AssemblyCode(
            application.Language.ToString(),
            LabelUtils.GetLabelFor(BuiltInParameter.ASSEMBLY_CODE),
            codedTypes,
            coded?.TypeName,
            coded?.AssemblyCode);
    }

    /// <summary>
    /// The first wall that one door and two windows cut, and what extraction
    /// reports for it. A hosted insert that generates none of the wall's faces
    /// cut nothing, so a wall with one is passed over rather than failed.
    /// </summary>
    private static SmokeCheck Openings(Document document)
    {
        foreach (Wall wall in WallReader.Walls(document))
        {
            List<Element> inserts = [.. wall.FindInserts(false, false, false, false).Select(document.GetElement).OfType<Element>()];
            int doors = inserts.Count(insert => insert.Category?.BuiltInCategory == BuiltInCategory.OST_Doors);
            int windows = inserts.Count(insert => insert.Category?.BuiltInCategory == BuiltInCategory.OST_Windows);
            if (inserts.Count != 3 || doors != 1 || windows != 2)
            {
                continue;
            }

            Dictionary<ElementId, List<XYZ>> cutters = WallReader.FaceGenerators(wall);
            if (!inserts.All(insert => cutters.ContainsKey(insert.Id)))
            {
                continue;
            }

            HostReading reading = WallReader.Read(document, wall);
            return SmokeChecks.Openings(
                wall.UniqueId,
                [.. inserts.Select(insert => insert.UniqueId)],
                [.. reading.Openings.Select(opening => opening.UniqueId), .. reading.Unmeasured.Select(opening => opening.UniqueId)]);
        }

        return SmokeChecks.Openings(null, [], []);
    }
}
