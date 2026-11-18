using Autodesk.Revit.DB;
using Viridian.Core.Geometry;
using Viridian.Library;

namespace Viridian.Revit;

/// <summary>
/// Cota de apoio de cada ponto: a superfície mais alta do terreno (toposólido ou superfície topográfica, também em
/// vínculos) dentro da faixa de busca, a menos que haja uma laje ou cobertura entre o nível e o terreno (jardim sobre
/// laje, cobertura verde, pátio pavimentado); nesse caso a planta fica sobre a laje.
/// </summary>
public sealed class TerrainProbe
{
    private const double StructureClearance = 0.5;
    private readonly ReferenceIntersector? _terrain;
    private readonly ReferenceIntersector? _structure;
    private readonly double _search;

    public TerrainProbe(Document document, View3D view, bool enabled, double searchM)
    {
        _search = searchM;
        if (!enabled)
        {
            return;
        }

        try
        {
            var terrain = new ElementMulticategoryFilter(new List<BuiltInCategory> { BuiltInCategory.OST_Toposolid, BuiltInCategory.OST_Topography });
            _terrain = new ReferenceIntersector(terrain, FindReferenceTarget.Face, view) { FindReferencesInRevitLinks = true };
            var structure = new ElementMulticategoryFilter(new List<BuiltInCategory> { BuiltInCategory.OST_Floors, BuiltInCategory.OST_Roofs });
            _structure = new ReferenceIntersector(structure, FindReferenceTarget.Face, view) { FindReferencesInRevitLinks = true };
        }
        catch (Exception ex)
        {
            ViridianLog.Warn("Maquete: busca do terreno indisponível", ex);
        }
    }

    public bool LastBedOnTerrain { get; set; }

    /// <summary>Cota (m) sobre a qual a planta se apoia; falso quando nada foi encontrado e vale a cota do nível.</summary>
    public bool TryGround(Point2 plan, double levelZ, out double z)
    {
        z = levelZ;
        if (_terrain is null)
        {
            return false;
        }

        try
        {
            double? terrain = Hit(_terrain, plan, levelZ + _search, _search * 2);
            double floor = terrain ?? levelZ - 1.0;
            double? structure = _structure is null ? null : Hit(_structure, plan, levelZ + StructureClearance, levelZ + StructureClearance - floor);
            if (structure is double slab && (terrain is null || slab >= terrain.Value - 0.05))
            {
                z = slab;
                return true;
            }

            if (terrain is double t)
            {
                z = t;
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            ViridianLog.Warn("Maquete: consulta ao terreno", ex);
            return false;
        }
    }

    private static double? Hit(ReferenceIntersector intersector, Point2 plan, double originZ, double maxDistance)
    {
        if (maxDistance <= 0)
        {
            return null;
        }

        ReferenceWithContext? hit = intersector.FindNearest(RevitUnits.ToXyz(plan, RevitUnits.ToFeet(originZ)), -XYZ.BasisZ);
        if (hit is null)
        {
            return null;
        }

        double distance = RevitUnits.ToMeters(hit.Proximity);
        return distance <= maxDistance ? originZ - distance : null;
    }
}
