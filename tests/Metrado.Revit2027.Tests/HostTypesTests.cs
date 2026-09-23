using System.Reflection;
using System.Xml.Linq;

namespace Metrado.Revit2027.Tests;

/// <summary>
/// Proves the class names Revit is handed resolve to types Revit can use.
///
/// Revit instantiates the manifest's class and the ribbon button's class by
/// name, at startup and on click. A misspelt name, a missing interface or a
/// constructor Revit cannot call compiles clean and fails only inside Revit.
///
/// The add-in assembly is inspected through <see cref="MetadataLoadContext"/>
/// and never loaded for execution: its types implement Revit interfaces, and
/// the Revit API cannot load outside the Revit process.
/// </summary>
public sealed class HostTypesTests
{
    private const string AddInAssemblyName = "Metrado.Revit2027";
    private const string ExportCommandClassName = "Metrado.Revit2027.ExportTakeoffCommand";

    [Fact]
    public void TheManifestClassIsAnExternalApplicationRevitCanConstruct()
    {
        using MetadataLoadContext context = OpenContext();

        Type application = AddInType(context, ManifestFullClassName());

        AssertConstructibleByRevit(application);
        Assert.Contains(application.GetInterfaces(), type => type.FullName == "Autodesk.Revit.UI.IExternalApplication");
    }

    [Fact]
    public void TheExportCommandIsAnExternalCommandRevitCanConstruct()
    {
        using MetadataLoadContext context = OpenContext();

        Type command = AddInType(context, ExportCommandClassName);

        AssertConstructibleByRevit(command);
        Assert.Contains(command.GetInterfaces(), type => type.FullName == "Autodesk.Revit.UI.IExternalCommand");
    }

    /// <summary>
    /// The button names its availability class by type, and Revit constructs
    /// it by name: it must exist, be constructible and implement the interface.
    /// </summary>
    [Fact]
    public void TheAvailabilityClassIsOneRevitCanConstruct()
    {
        using MetadataLoadContext context = OpenContext();

        Type availability = AddInType(context, "Metrado.Revit2027.CommandAvailability");

        AssertConstructibleByRevit(availability);
        Assert.Contains(availability.GetInterfaces(), type => type.FullName == "Autodesk.Revit.UI.IExternalCommandAvailability");
    }

    /// <summary>
    /// The read-only requirement at the one place Revit enforces it: a command
    /// declared <c>ReadOnly</c> cannot open a write transaction at all. Every
    /// command in the add-in is checked, not one named here: the add-in never
    /// writes to the document, so no command it ships may be able to, and a
    /// button rewired to a new command cannot slip past a check of the old one.
    /// </summary>
    [Fact]
    public void EveryExternalCommandInTheAddInDeclaresAReadOnlyTransaction()
    {
        using MetadataLoadContext context = OpenContext();

        Type mode = context.LoadFromAssemblyName("RevitAPI").GetType("Autodesk.Revit.Attributes.TransactionMode", throwOnError: true)!;
        object readOnly = mode.GetField("ReadOnly")!.GetRawConstantValue()!;
        Type[] commands = [.. context.LoadFromAssemblyName(AddInAssemblyName).GetTypes()
            .Where(type => type.GetInterfaces().Any(contract => contract.FullName == "Autodesk.Revit.UI.IExternalCommand"))];

        Assert.Contains(commands, command => command.FullName == ExportCommandClassName);

        foreach (Type command in commands)
        {
            CustomAttributeData? transaction = command.GetCustomAttributesData()
                .SingleOrDefault(attribute => attribute.AttributeType.FullName == "Autodesk.Revit.Attributes.TransactionAttribute");

            Assert.True(transaction is not null, $"{command.FullName} declares no Transaction attribute.");
            Assert.True(
                readOnly.Equals(Assert.Single(transaction.ConstructorArguments).Value),
                $"{command.FullName} is not declared TransactionMode.ReadOnly.");
        }
    }

    private static void AssertConstructibleByRevit(Type type)
    {
        Assert.True(type.IsPublic, $"{type.FullName} is not public.");
        Assert.True(type.IsClass && !type.IsAbstract, $"{type.FullName} is not a concrete class.");
        Assert.NotNull(type.GetConstructor(Type.EmptyTypes));
    }

    private static Type AddInType(MetadataLoadContext context, string fullName)
    {
        Type? type = context.LoadFromAssemblyName(AddInAssemblyName).GetType(fullName);

        Assert.True(type is not null, $"{fullName} is not in {AddInAssemblyName}.dll.");
        return type;
    }

    private static string ManifestFullClassName()
    {
        XDocument manifest = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Metrado.addin"));

        return (string)manifest.Root!.Element("AddIn")!.Element("FullClassName")!;
    }

    /// <summary>
    /// Resolves from what this test process may load — the add-in, its
    /// project references and both runtimes the Revit API is built against —
    /// plus the Revit API reference assemblies. Anything else fails loudly.
    /// </summary>
    private static MetadataLoadContext OpenContext()
    {
        string trusted = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!;
        IEnumerable<string> paths = trusted.Split(Path.PathSeparator)
            .Concat(Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "RevitApi"), "*.dll"));

        return new MetadataLoadContext(new PathAssemblyResolver(paths));
    }
}
