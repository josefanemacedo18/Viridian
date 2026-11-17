using Autodesk.Revit.DB;
using Viridian.Core.Geometry;

namespace Viridian.Revit;

/// <summary>Conversões entre as unidades internas do Revit (pés) e as do Viridian (metros e milímetros).</summary>
public static class RevitUnits
{
    public const double MetersPerFoot = 0.3048;

    public static double ToFeet(double meters) => meters / MetersPerFoot;

    public static double ToMeters(double feet) => feet * MetersPerFoot;

    public static double MmToFeet(double millimeters) => millimeters / 304.8;

    public static double SquareFeetToSquareMeters(double squareFeet) => squareFeet * MetersPerFoot * MetersPerFoot;

    public static XYZ ToXyz(Point2 point, double zFeet = 0) => new(ToFeet(point.X), ToFeet(point.Y), zFeet);

    public static Point2 ToPoint2(XYZ point) => new(ToMeters(point.X), ToMeters(point.Y));
}
