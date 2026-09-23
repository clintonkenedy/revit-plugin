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
    /// The read-only requirement at the one place Revit enforces it: a command
    /// declared <c>ReadOnly</c> cannot open a write transaction at all.
    /// </summary>
    [Fact]
    public void TheExportCommandDeclaresAReadOnlyTransaction()
    {
        using MetadataLoadContext context = OpenContext();

        Type command = AddInType(context, ExportCommandClassName);
        CustomAttributeData transaction = Assert.Single(
            command.GetCustomAttributesData(),
            attribute => attribute.AttributeType.FullName == "Autodesk.Revit.Attributes.TransactionAttribute");

        Type mode = context.LoadFromAssemblyName("RevitAPI").GetType("Autodesk.Revit.Attributes.TransactionMode", throwOnError: true)!;
        object readOnly = mode.GetField("ReadOnly")!.GetRawConstantValue()!;

        Assert.Equal(readOnly, Assert.Single(transaction.ConstructorArguments).Value);
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
