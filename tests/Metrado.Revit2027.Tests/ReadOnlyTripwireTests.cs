using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Metrado.Revit2027.Tests;

/// <summary>
/// Fails the build when the add-in's compiled code references a Revit API
/// member nobody has reviewed as read-only.
///
/// Revit enforces <c>ReadOnly</c> at run time, but only on the paths a run
/// takes: a write on a branch no host run exercises would surface in front of
/// an estimator. This reads every member reference in the built assembly
/// instead, so an unexercised branch cannot hide one.
///
/// It is an allowlist because a denylist cannot be complete: a first version
/// listed transactions, Regenerate, Delete, Save and every setter, and still
/// let <c>Wall.Flip()</c> through. Using a new member now means adding it
/// here on purpose. It is a tripwire, not a proof — reflection could reach a
/// writer it does not see.
/// </summary>
public sealed class ReadOnlyTripwireTests
{
    private static readonly string[] RevitAssemblies = ["RevitAPI", "RevitAPIUI", "RevitAPIIFC"];

    /// <summary>Types whose every member is a write, or the start of one.</summary>
    private static readonly string[] WritingTypes =
    [
        "Autodesk.Revit.DB.Transaction",
        "Autodesk.Revit.DB.SubTransaction",
        "Autodesk.Revit.DB.TransactionGroup",
        "Autodesk.Revit.UI.ExternalEvent",
    ];

    private static readonly string[] WritingMembers =
    [
        "Autodesk.Revit.DB.Document.Regenerate",
        "Autodesk.Revit.DB.Document.Delete",
        "Autodesk.Revit.DB.Document.Save",
        "Autodesk.Revit.DB.Document.SaveAs",
        "Autodesk.Revit.DB.Document.Export",
        "Autodesk.Revit.DB.Document.Close",
        "Autodesk.Revit.DB.Parameter.Set",
        "Autodesk.Revit.DB.Parameter.SetValueString",
    ];

    /// <summary>
    /// Setters that configure something other than the model: how geometry is
    /// read (<c>Options</c>), how the ribbon button is described at startup
    /// (<c>ItemData</c> and <c>PushButtonData</c>), and what a dialog says
    /// (<c>TaskDialog</c>).
    /// </summary>
    private static readonly string[] HarmlessSetterTypes =
        ["Autodesk.Revit.DB.Options", "Autodesk.Revit.UI.ItemData", "Autodesk.Revit.UI.PushButtonData", "Autodesk.Revit.UI.TaskDialog"];

    /// <summary>Every Revit API member the add-in uses, each reviewed as reading, configuring a read, or UI.</summary>
    private static readonly string[] ReviewedReadOnly =
    [
        "Autodesk.Revit.ApplicationServices.Application.WriteJournalComment",
        "Autodesk.Revit.ApplicationServices.Application.get_Language",
        "Autodesk.Revit.Attributes.TransactionAttribute..ctor",
        "Autodesk.Revit.DB.BoundingBoxXYZ.get_Max",
        "Autodesk.Revit.DB.BoundingBoxXYZ.get_Min",
        "Autodesk.Revit.DB.BoundingBoxXYZ.get_Transform",
        "Autodesk.Revit.DB.Category.get_BuiltInCategory",
        "Autodesk.Revit.DB.CompoundStructure.GetWallSweepsInfo",
        "Autodesk.Revit.DB.Curve.GetEndPoint",
        "Autodesk.Revit.DB.Curve.Tessellate",
        "Autodesk.Revit.DB.DesignOption.get_IsPrimary",
        "Autodesk.Revit.DB.Document.GetElement",
        "Autodesk.Revit.DB.Document.get_IsFamilyDocument",
        "Autodesk.Revit.DB.Document.get_IsModelInCloud",
        "Autodesk.Revit.DB.Document.get_IsModified",
        "Autodesk.Revit.DB.Document.get_PathName",
        "Autodesk.Revit.DB.Edge.Tessellate",
        "Autodesk.Revit.DB.Face.GetEdgesAsCurveLoops",
        "Autodesk.Revit.DB.Element.GetGeneratingElementIds",
        "Autodesk.Revit.DB.Element.get_BoundingBox",
        "Autodesk.Revit.DB.Element.get_Category",
        "Autodesk.Revit.DB.Element.get_DesignOption",
        "Autodesk.Revit.DB.Element.get_Geometry",
        "Autodesk.Revit.DB.Element.get_Id",
        "Autodesk.Revit.DB.Element.get_Location",
        "Autodesk.Revit.DB.Element.get_Name",
        "Autodesk.Revit.DB.Element.get_Parameter",
        "Autodesk.Revit.DB.Element.get_UniqueId",
        "Autodesk.Revit.DB.ElementId.op_Equality",
        "Autodesk.Revit.DB.ElementId.op_Inequality",
        "Autodesk.Revit.DB.ElementType.get_FamilyName",
        "Autodesk.Revit.DB.FaceArray.get_Size",
        "Autodesk.Revit.DB.FamilyInstance.get_Host",
        "Autodesk.Revit.DB.FilteredElementCollector..ctor",
        "Autodesk.Revit.DB.FilteredElementCollector.OfCategory",
        "Autodesk.Revit.DB.FilteredElementCollector.WhereElementIsNotElementType",
        "Autodesk.Revit.DB.HostObjAttributes.GetCompoundStructure",
        "Autodesk.Revit.DB.HostObject.FindInserts",
        "Autodesk.Revit.DB.IFC.ExporterIFCUtils.ComputeAreaOfCurveLoops",
        "Autodesk.Revit.DB.IFC.ExporterIFCUtils.GetInstanceCutoutFromWall",
        "Autodesk.Revit.DB.IFC.ExporterIFCUtils.HasElevationProfile",
        "Autodesk.Revit.DB.InstanceVoidCutUtils.GetCuttingVoidInstances",
        "Autodesk.Revit.DB.LabelUtils.GetLabelFor",
        "Autodesk.Revit.DB.Line.get_Direction",
        "Autodesk.Revit.DB.LocationCurve.get_Curve",
        "Autodesk.Revit.DB.Opening.get_BoundaryRect",
        "Autodesk.Revit.DB.Opening.get_Host",
        "Autodesk.Revit.DB.Opening.get_IsRectBoundary",
        "Autodesk.Revit.DB.Options..ctor",
        "Autodesk.Revit.DB.Options.set_DetailLevel",
        "Autodesk.Revit.DB.Parameter.AsDouble",
        "Autodesk.Revit.DB.Parameter.AsInteger",
        "Autodesk.Revit.DB.Parameter.AsString",
        "Autodesk.Revit.DB.Parameter.get_HasValue",
        "Autodesk.Revit.DB.Parameter.get_StorageType",
        "Autodesk.Revit.DB.Solid.get_Edges",
        "Autodesk.Revit.DB.Solid.get_Faces",
        "Autodesk.Revit.DB.Transform.OfPoint",
        "Autodesk.Revit.DB.UnitTypeId.get_SquareMeters",
        "Autodesk.Revit.DB.UnitUtils.ConvertFromInternalUnits",
        "Autodesk.Revit.DB.Wall.get_CrossSection",
        "Autodesk.Revit.DB.Wall.get_WallType",
        "Autodesk.Revit.DB.WallType.get_Kind",
        "Autodesk.Revit.DB.XYZ..ctor",
        "Autodesk.Revit.DB.XYZ.DotProduct",
        "Autodesk.Revit.DB.XYZ.GetLength",
        "Autodesk.Revit.DB.XYZ.get_X",
        "Autodesk.Revit.DB.XYZ.get_Y",
        "Autodesk.Revit.DB.XYZ.get_Z",
        "Autodesk.Revit.DB.XYZ.op_Subtraction",
        "Autodesk.Revit.UI.ExternalCommandData.get_Application",
        "Autodesk.Revit.UI.ItemData.set_ToolTip",
        "Autodesk.Revit.UI.PushButtonData..ctor",
        "Autodesk.Revit.UI.PushButtonData.set_AvailabilityClassName",
        "Autodesk.Revit.UI.RibbonPanel.AddItem",
        "Autodesk.Revit.UI.TaskDialog.Show",
        "Autodesk.Revit.UI.TaskDialog..ctor",
        "Autodesk.Revit.UI.TaskDialog.set_ExpandedContent",
        "Autodesk.Revit.UI.TaskDialog.set_MainContent",
        "Autodesk.Revit.UI.TaskDialog.set_MainInstruction",
        "Autodesk.Revit.UI.UIApplication.get_ActiveUIDocument",
        "Autodesk.Revit.UI.UIApplication.get_Application",
        "Autodesk.Revit.UI.UIControlledApplication.CreateRibbonPanel",
        "Autodesk.Revit.UI.UIDocument.get_Document",
    ];

    [Fact]
    public void TheAddInReferencesOnlyRevitApisReviewedAsReadOnly()
    {
        string[] unreviewed = [.. RevitMemberReferences()
            .Select(reference => reference.FullName)
            .Distinct()
            .Where(name => !ReviewedReadOnly.Contains(name))];

        Assert.True(unreviewed.Length == 0, "Unreviewed Revit API members: " + string.Join(", ", unreviewed));
    }

    /// <summary>The allowlist can never absorb a known writer, however it is edited.</summary>
    [Fact]
    public void NoKnownWriterIsEverReviewedAsReadOnly()
    {
        Assert.DoesNotContain(ReviewedReadOnly, IsKnownWriter);
    }

    [Fact]
    public void TheAddInImplementsNoExternalEventHandler()
    {
        using PEReader pe = new(File.OpenRead(AddInPath()));
        MetadataReader metadata = pe.GetMetadataReader();

        string[] handlers = [.. metadata.TypeDefinitions
            .Select(metadata.GetTypeDefinition)
            .Where(type => type.GetInterfaceImplementations()
                .Select(handle => metadata.GetInterfaceImplementation(handle).Interface)
                .Any(iface => iface.Kind == HandleKind.TypeReference
                    && metadata.GetString(metadata.GetTypeReference((TypeReferenceHandle)iface).Name) == "IExternalEventHandler"))
            .Select(type => metadata.GetString(type.Name))];

        Assert.Empty(handlers);
    }

    /// <summary>
    /// The tripwire must see real references, or an empty result proves
    /// nothing: the add-in does read parameters and geometry.
    /// </summary>
    [Fact]
    public void TheTripwireSeesTheAddInsRevitReferences()
    {
        string[] all = [.. RevitMemberReferences().Select(reference => reference.FullName)];

        Assert.Contains("Autodesk.Revit.DB.Element.get_Parameter", all);
        Assert.Contains("Autodesk.Revit.DB.Options.set_DetailLevel", all);
    }

    private static bool IsKnownWriter(string fullName)
    {
        int member = fullName.LastIndexOf('.', fullName.Length - 1 - (fullName.EndsWith("..ctor", StringComparison.Ordinal) ? 5 : 0));
        string type = fullName[..member];
        string name = fullName[(member + 1)..];
        return WritingTypes.Contains(type)
            || WritingMembers.Contains(fullName)
            || (name.StartsWith("set_", StringComparison.Ordinal) && !HarmlessSetterTypes.Contains(type));
    }

    private static List<(string Type, string Member, string FullName)> RevitMemberReferences()
    {
        using PEReader pe = new(File.OpenRead(AddInPath()));
        MetadataReader metadata = pe.GetMetadataReader();
        List<(string, string, string)> references = [];

        foreach (MemberReferenceHandle handle in metadata.MemberReferences)
        {
            MemberReference member = metadata.GetMemberReference(handle);
            if (member.Parent.Kind != HandleKind.TypeReference)
            {
                continue;
            }

            TypeReference type = metadata.GetTypeReference((TypeReferenceHandle)member.Parent);
            if (type.ResolutionScope.Kind != HandleKind.AssemblyReference
                || !RevitAssemblies.Contains(metadata.GetString(metadata.GetAssemblyReference((AssemblyReferenceHandle)type.ResolutionScope).Name)))
            {
                continue;
            }

            string typeName = $"{metadata.GetString(type.Namespace)}.{metadata.GetString(type.Name)}";
            string memberName = metadata.GetString(member.Name);
            references.Add((typeName, memberName, $"{typeName}.{memberName}"));
        }

        return references;
    }

    private static string AddInPath() => Path.Combine(AppContext.BaseDirectory, "Metrado.Revit2027.dll");
}
