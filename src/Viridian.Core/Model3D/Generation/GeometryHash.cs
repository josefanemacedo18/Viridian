using System.Globalization;
using System.Text;

namespace Viridian.Core.Model3D.Generation;

/// <summary>
/// Hash reprodutível da geometria de um exemplar (FNV-1a de 64 bits sobre um texto canônico): muda quando qualquer
/// entrada que afeta a forma ou a aparência muda; não usa GetHashCode nem valores aleatórios não persistidos.
/// </summary>
public static class GeometryHash
{
    public static string Of(PlantRequest r)
    {
        ArgumentNullException.ThrowIfNull(r);
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.Append("g").Append(PlantGeometryGenerator.Version).Append('|');
        sb.Append(r.Profile.Signature()).Append('|');
        sb.Append(Math.Round(r.HeightM, 3).ToString(inv)).Append('|').Append(Math.Round(r.CrownDiameterM, 3).ToString(inv)).Append('|');
        sb.Append(r.Quality).Append('|').Append(r.EffectiveSeed.ToString(inv)).Append('|').Append(r.Foliage).Append('|');
        sb.Append(r.Flowers).Append('|').Append(r.SolidOnly).Append('|').Append(r.Variation).Append('|');
        if (r.Fruits is not null)
        {
            sb.Append(r.Fruits.Type).Append(r.Fruits.Color.Hex).Append(Math.Round(r.Fruits.SizeM, 4).ToString(inv));
        }

        return Text(sb.ToString());
    }

    /// <summary>Hash de um texto qualquer (16 dígitos hexadecimais).</summary>
    public static string Text(string text)
    {
        unchecked
        {
            ulong hash = 14695981039346656037UL;
            foreach (byte b in Encoding.UTF8.GetBytes(text ?? string.Empty))
            {
                hash ^= b;
                hash *= 1099511628211UL;
            }

            return hash.ToString("x16", CultureInfo.InvariantCulture);
        }
    }
}
