using Autodesk.Revit.DB;
using Metrado.Revit2027;

namespace Metrado.HostHarness;

/// <summary>
/// Holds the per-layer add-back (task 3.2) to ground truth before it is
/// written. For each opening Metrado measures in a layered wall, floor or
/// roof in force, up to a cap per category, it deletes the opening in a
/// transaction that is rolled back and reads what each material gets back:
/// its volume against the host's gain times the material's summed layer
/// width, and its area against the host's gain times its layer count. An
/// opening it cannot delete is listed with the reason. As a negative control
/// it sets one tested wall type to wrap at inserts, in the same kind of
/// transaction, and probes its openings again, so the two runs can be
/// compared. The model is never saved.
/// </summary>
public static class LayerOpeningProbe
{
    /// <param name="HostGainM2">Ground truth: how much the host's computed area grows when the opening is deleted.</param>
    public sealed record OpeningResult(
        string Host, string Category, string Type, string Opening, List<string> Conditions, double MeasuredM2, double HostGainM2, List<MaterialResult> Materials);

    /// <param name="VolumeGainM3">What deleting the opening gives the material back in volume.</param>
    /// <param name="WidthShareM3">The opening's gain times the material's summed layer width: the share the rule would add back.</param>
    public sealed record MaterialResult(string Material, int Layers, double WidthM, double VolumeGainM3, double WidthShareM3, double AreaGainM2, double CountShareM2);

    /// <param name="NotProbed">Each measured opening left out, with why: no element to delete, or no layers read.</param>
    public sealed record Result(List<OpeningResult> Openings, List<string> NotProbed, List<OpeningResult> Control, string? ControlNote, bool ModifiedAfterProbe);

    /// <param name="maxOpenings">The cap on each category's probed openings, so floors and roofs are reached whatever the walls hold.</param>
    public static Result Run(Document document, int maxOpenings)
    {
        List<OpeningResult> openings = [];
        List<string> notProbed = [];
        Dictionary<string, int> probed = [];
        List<(HostObject Host, HostReading Reading)> hosts =
        [
            .. WallReader.Walls(document).Select(wall => ((HostObject)wall, WallReader.Read(document, wall))),
            .. SurfaceReader.Surfaces(document, BuiltInCategory.OST_Floors).Select(floor => (floor, SurfaceReader.Read(document, floor, HostTakeoff.FloorsKey))),
            .. SurfaceReader.Surfaces(document, BuiltInCategory.OST_Roofs).Select(roof => (roof, SurfaceReader.Read(document, roof, HostTakeoff.RoofsKey))),
        ];

        foreach ((HostObject host, HostReading reading) in hosts.Where(entry => entry.Reading.Openings.Count > 0))
        {
            foreach (OpeningReading opening in reading.Openings)
            {
                if (probed.GetValueOrDefault(reading.CategoryKey) >= maxOpenings)
                {
                    break;
                }

                if (document.GetElement(opening.UniqueId) is null)
                {
                    notProbed.Add($"{reading.CategoryKey} {opening.UniqueId}: no element to delete (a hole of the host's own outline)");
                }
                else if (Probe(document, host, reading.CategoryKey, opening) is OpeningResult result)
                {
                    openings.Add(result);
                    probed[reading.CategoryKey] = probed.GetValueOrDefault(reading.CategoryKey) + 1;
                }
                else
                {
                    notProbed.Add($"{reading.CategoryKey} {opening.UniqueId}: its host's layers could not be read");
                }
            }
        }

        (List<OpeningResult> control, string? note) = Control(document, hosts);
        return new Result(openings, notProbed, control, note, document.IsModified);
    }

    /// <summary>Null when the opening has no element to delete (a hole of the host's own outline) or the host no layers.</summary>
    private static OpeningResult? Probe(Document document, HostObject host, string category, OpeningReading opening)
    {
        if (document.GetElement(opening.UniqueId) is not Element cut
            || MaterialLayerReader.Read(document, host, null) is not LayerReading before)
        {
            return null;
        }

        List<string> conditions = [.. Conditions(before)];
        double hostBefore = Area(host);
        (double hostAfter, Dictionary<string, (double Volume, double Area)> after) = Without(document, host, cut.Id);
        double gain = hostAfter - hostBefore;

        return new OpeningResult(
            host.UniqueId,
            category,
            document.GetElement(host.GetTypeId())?.Name ?? "?",
            opening.UniqueId,
            conditions,
            M2(opening.AreaSquareFeet),
            M2(gain),
            [.. before.Materials.Select(material =>
            {
                List<LayerFacts> layers = [.. before.Layers.Where(layer => layer.MaterialUniqueId == material.UniqueId)];
                double width = layers.Sum(layer => layer.WidthFeet);
                (double volume, double area) = after.GetValueOrDefault(material.UniqueId, (double.NaN, double.NaN));
                return new MaterialResult(
                    material.Name,
                    layers.Count,
                    UnitUtils.ConvertFromInternalUnits(width, UnitTypeId.Meters),
                    M3(volume - material.VolumeCubicFeet),
                    M3(width * gain),
                    M2(area - material.AreaSquareFeet),
                    M2(layers.Count * gain));
            })]);
    }

    /// <summary>Sets the first tested wall type without conditions to wrap at inserts, rolled back, and probes its openings.</summary>
    private static (List<OpeningResult> Control, string? Note) Control(Document document, List<(HostObject Host, HostReading Reading)> hosts)
    {
        foreach ((HostObject host, HostReading reading) in hosts.Where(entry => entry.Host is Wall && entry.Reading.Openings.Count > 0))
        {
            if (document.GetElement(host.GetTypeId()) is not WallType type
                || type.GetCompoundStructure() is not CompoundStructure structure
                || structure.OpeningWrapping != OpeningWrappingCondition.None
                || structure.GetLayers().Count < 2)
            {
                continue;
            }

            using TransactionGroup group = new(document, "Metrado probe control (rolled back)");
            group.Start();
            try
            {
                using (Transaction transaction = new(document, "Metrado probe control"))
                {
                    transaction.Start();
                    structure.OpeningWrapping = OpeningWrappingCondition.ExteriorAndInterior;
                    type.SetCompoundStructure(structure);
                    transaction.Commit();
                }

                List<OpeningResult> control = [.. reading.Openings.Take(3)
                    .Select(opening => Probe(document, host, HostTakeoff.WallsKey, opening))
                    .OfType<OpeningResult>()];
                return (control, $"type {type.Name} set to wrap at inserts on both faces");
            }
            catch (Exception ex)
            {
                return ([], $"control failed on {type.Name}: {ex.Message}");
            }
            finally
            {
                group.RollBack();
            }
        }

        return ([], "no wall type without conditions has openings");
    }

    /// <summary>The host's area and each material's volume and area once the cut is deleted, rolled back.</summary>
    private static (double Area, Dictionary<string, (double Volume, double Area)> Materials) Without(Document document, HostObject host, ElementId cut)
    {
        using Transaction transaction = new(document, "Metrado probe (rolled back)");
        transaction.Start();
        try
        {
            document.Delete(cut);
            document.Regenerate();
            return (Area(host), host.GetMaterialIds(false)
                .Select(id => document.GetElement(id))
                .OfType<Material>()
                .ToDictionary(material => material.UniqueId, material => (host.GetMaterialVolume(material.Id), host.GetMaterialArea(material.Id, false))));
        }
        catch (Exception)
        {
            return (double.NaN, []);
        }
        finally
        {
            transaction.RollBack();
        }
    }

    private static IEnumerable<string> Conditions(LayerReading reading)
    {
        if (reading.Type.WrapsAtInserts)
        {
            yield return "WrapsAtInserts";
        }

        if (reading.Type.VerticallyCompound)
        {
            yield return "VerticallyCompound";
        }

        if (reading.Type.VariableLayer)
        {
            yield return "VariableLayer";
        }

        if (reading.Type.ShapeEdited)
        {
            yield return "ShapeEdited";
        }

        if (reading.Layers.Any(layer => layer.Function == "StructuralDeck"))
        {
            yield return "StructuralDeck";
        }
    }

    private static double Area(Element element) =>
        element.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED)?.AsDouble() ?? double.NaN;

    private static double M2(double squareFeet) => UnitUtils.ConvertFromInternalUnits(squareFeet, UnitTypeId.SquareMeters);

    private static double M3(double cubicFeet) => UnitUtils.ConvertFromInternalUnits(cubicFeet, UnitTypeId.CubicMeters);
}
