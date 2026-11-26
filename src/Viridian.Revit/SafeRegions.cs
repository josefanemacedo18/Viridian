using Autodesk.Revit.DB;
using Viridian.Library;

namespace Viridian.Revit;

/// <summary>
/// Criação tolerante de regiões preenchidas. O Revit recusa a região inteira (e, sem tratamento, desfaz a operação
/// toda) quando um contorno é aberto, curto demais, cruza a si mesmo ou toca outro contorno da mesma região. Aqui a
/// região é criada com todos os contornos de uma vez e, se o Revit recusar, contorno por contorno: só o que ele não
/// aceitar fica de fora (registrado no log), e o resto do desenho é mantido.
/// </summary>
public static class SafeRegions
{
    /// <summary>Retângulo horizontal de cantos opostos <paramref name="a"/> e <paramref name="c"/> (pés, na cota de a); null quando um lado é curto demais para o Revit.</summary>
    public static CurveLoop? Rectangle(XYZ a, XYZ c, double shortCurveTolerance)
    {
        var b = new XYZ(c.X, a.Y, a.Z);
        var d = new XYZ(a.X, c.Y, a.Z);
        if (a.DistanceTo(b) < shortCurveTolerance * 1.5 || b.DistanceTo(c) < shortCurveTolerance * 1.5)
        {
            return null;
        }

        return CurveLoop.Create(new List<Curve> { Line.CreateBound(a, b), Line.CreateBound(b, c), Line.CreateBound(c, d), Line.CreateBound(d, a) });
    }

    /// <summary>Cria a(s) região(ões) na vista. Nunca lança por contorno inválido; devolve as regiões criadas.</summary>
    public static List<FilledRegion> Create(Document document, ElementId typeId, ElementId viewId, IList<CurveLoop> loops, string what)
    {
        var created = new List<FilledRegion>();
        if (loops.Count == 0)
        {
            return created;
        }

        try
        {
            created.Add(FilledRegion.Create(document, typeId, viewId, loops));
            return created;
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException ex)
        {
            if (loops.Count == 1)
            {
                ViridianLog.Warn(what + ": contorno recusado pelo Revit e ignorado.", ex);
                return created;
            }

            ViridianLog.Warn(what + ": o Revit recusou os contornos juntos; criando um a um.", ex);
        }

        int skipped = 0;
        foreach (CurveLoop loop in loops)
        {
            try
            {
                created.Add(FilledRegion.Create(document, typeId, viewId, new List<CurveLoop> { loop }));
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                skipped++;
            }
        }

        if (skipped > 0)
        {
            ViridianLog.Warn($"{what}: {skipped} de {loops.Count} contornos recusados pelo Revit e ignorados.");
        }

        return created;
    }
}
