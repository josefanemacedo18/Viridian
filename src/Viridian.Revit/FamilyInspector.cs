using System.IO;
using Autodesk.Revit.DB;
using Viridian.Core.Geometry;
using Viridian.Library;
using RevitApplication = Autodesk.Revit.ApplicationServices.Application;

namespace Viridian.Revit;

/// <summary>Resultado da verificação de uma família personalizada antes do cadastro na biblioteca.</summary>
public sealed class FamilyInspection
{
    public string FilePath { get; set; } = string.Empty;

    public string FamilyName { get; set; } = string.Empty;

    public string CategoryName { get; set; } = string.Empty;

    /// <summary>Família da categoria Vegetação: pode ser usada como exemplar BIM.</summary>
    public bool IsPlanting { get; set; }

    /// <summary>Componente de detalhe: pode ser usado como símbolo gráfico de maciços.</summary>
    public bool IsDetailItem { get; set; }

    public int Lines2D { get; set; }

    public bool Has3DGeometry { get; set; }

    /// <summary>Maior dimensão do desenho 2D, em metros.</summary>
    public double ExtentM { get; set; }

    /// <summary>Distância entre o ponto de inserção e o centro do desenho, em metros.</summary>
    public double InsertionOffsetM { get; set; }

    public List<string> TypeNames { get; } = new();

    public List<string> Checks { get; } = new();

    public List<string> Warnings { get; } = new();

    public string Error { get; set; } = string.Empty;

    public bool IsUsable => string.IsNullOrEmpty(Error) && (IsPlanting || IsDetailItem);
}

/// <summary>Abre um arquivo de família e verifica se ele pode ser cadastrado na biblioteca do Viridian.</summary>
public static class FamilyInspector
{
    /// <summary>Não pode haver transação aberta no documento ativo.</summary>
    public static FamilyInspection Inspect(RevitApplication app, string file)
    {
        var result = new FamilyInspection { FilePath = file, FamilyName = Path.GetFileNameWithoutExtension(file) };
        if (!File.Exists(file))
        {
            result.Error = "Arquivo não encontrado.";
            return result;
        }

        if (!string.Equals(Path.GetExtension(file), ".rfa", StringComparison.OrdinalIgnoreCase))
        {
            result.Error = "Selecione um arquivo de família do Revit (.rfa).";
            return result;
        }

        // Se a família já estiver aberta nesta sessão, usa o documento aberto e não o fecha ao final.
        Document? alreadyOpen = null;
        foreach (Document open in app.Documents)
        {
            if (string.Equals(open.PathName, file, StringComparison.OrdinalIgnoreCase))
            {
                alreadyOpen = open;
                break;
            }
        }

        Document? document = alreadyOpen;
        try
        {
            document ??= app.OpenDocumentFile(file);
            if (document is null || !document.IsFamilyDocument || document.OwnerFamily is null)
            {
                result.Error = "O arquivo não é uma família do Revit.";
                return result;
            }

            Analyze(document, result);
        }
        catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ApplicationException or IOException)
        {
            ViridianLog.Warn("Falha ao inspecionar a família " + file, ex);
            result.Error = "O Revit não conseguiu abrir a família: " + ex.Message;
        }
        finally
        {
            if (alreadyOpen is null && document is not null)
            {
                try
                {
                    document.Close(false);
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException ex)
                {
                    ViridianLog.Warn("Falha ao fechar a família inspecionada.", ex);
                }
            }
        }

        return result;
    }

    private static void Analyze(Document document, FamilyInspection result)
    {
        Category? category = document.OwnerFamily.FamilyCategory;
        result.CategoryName = category?.Name ?? "(sem categoria)";
        BuiltInCategory builtIn = category?.BuiltInCategory ?? BuiltInCategory.INVALID;
        result.IsPlanting = builtIn == BuiltInCategory.OST_Planting;
        result.IsDetailItem = builtIn == BuiltInCategory.OST_DetailComponents;

        if (result.IsPlanting)
        {
            result.Checks.Add("Categoria Vegetação: pode ser usada como exemplar, com quantitativos e identificação.");
        }
        else if (result.IsDetailItem)
        {
            result.Checks.Add("Componente de detalhe: pode ser usado como símbolo gráfico de maciços (não é contado como planta).");
        }
        else
        {
            result.Error = $"A família pertence à categoria \"{result.CategoryName}\". Use uma família de Vegetação ou um componente de detalhe.";
            return;
        }

        foreach (FamilyType type in document.FamilyManager.Types)
        {
            if (!string.IsNullOrWhiteSpace(type.Name))
            {
                result.TypeNames.Add(type.Name);
            }
        }

        var points = new List<Point2>();
        using (var curves = new FilteredElementCollector(document))
        {
            foreach (CurveElement element in curves.OfClass(typeof(CurveElement)).Cast<CurveElement>())
            {
                if (element is not (SymbolicCurve or DetailCurve) || element.GeometryCurve is null)
                {
                    continue;
                }

                result.Lines2D++;
                foreach (XYZ point in element.GeometryCurve.Tessellate())
                {
                    points.Add(RevitUnits.ToPoint2(point));
                }
            }
        }

        using (var forms = new FilteredElementCollector(document))
        {
            result.Has3DGeometry = forms.OfClass(typeof(GenericForm)).GetElementCount() > 0;
        }

        if (result.Lines2D == 0)
        {
            result.Warnings.Add("A família não possui linhas 2D (simbólicas ou de detalhe): nada será exibido em planta.");
        }
        else
        {
            result.Checks.Add($"Representação 2D encontrada: {result.Lines2D} linha(s).");
            Bounds2 bounds = Bounds2.FromPoints(points);
            result.ExtentM = Math.Max(bounds.Width, bounds.Height);
            result.InsertionOffsetM = bounds.Center.Length;
            if (result.ExtentM > 0 && result.InsertionOffsetM > result.ExtentM * 0.1)
            {
                result.Warnings.Add(
                    $"O ponto de inserção está a {result.InsertionOffsetM:0.00} m do centro do desenho; a distribuição automática usa o ponto de inserção.");
            }
            else
            {
                result.Checks.Add("Ponto de inserção centralizado no desenho.");
            }
        }

        if (result.Has3DGeometry)
        {
            result.Warnings.Add("A família contém geometria 3D. O Viridian trabalha com representação 2D; a geometria 3D será mantida como está.");
        }

        if (result.IsPlanting)
        {
            result.Checks.Add("Os parâmetros VIR_ são vinculados pelo projeto; a família não precisa contê-los.");
        }
    }
}
