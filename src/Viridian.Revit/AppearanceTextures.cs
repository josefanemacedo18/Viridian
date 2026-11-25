using System.IO;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Visual;
using Viridian.Core.Colors;
using Viridian.Core.Model3D.Textures;
using Viridian.Core.Paving;
using Viridian.Library;

namespace Viridian.Revit;

/// <summary>Aparências de material com imagem (textura) na escala real, para o estilo Realista e a renderização.</summary>
public static class AppearanceTextures
{
    /// <summary>Liga uma imagem (bitmap unificado) a uma propriedade da aparência e ajusta o tamanho real da imagem.</summary>
    public static string ConnectBitmap(AssetProperty? property, string file, double sizeM, string label)
    {
        if (property is null)
        {
            return $"{label}: propriedade não encontrada";
        }

        Asset? connected = property.GetSingleConnectedAsset();
        if (connected is null)
        {
            property.AddConnectedAsset("UnifiedBitmapSchema");
            connected = property.GetSingleConnectedAsset();
        }

        if (connected?.FindByName(UnifiedBitmap.UnifiedbitmapBitmap) is not AssetPropertyString path)
        {
            return $"{label}: não aceitou imagem";
        }

        if (!path.IsValidValue(file))
        {
            return $"{label}: caminho não aceito";
        }

        path.Value = file;
        string scale = string.Empty;
        foreach (string axis in new[] { UnifiedBitmap.TextureRealWorldScaleX, UnifiedBitmap.TextureRealWorldScaleY })
        {
            if (connected.FindByName(axis) is AssetPropertyDistance distance)
            {
                double value = ToUnit(sizeM, distance.GetUnitTypeId());
                if (distance.IsValidValue(value))
                {
                    distance.Value = value;
                    scale = $" ({sizeM:0.##} m)";
                }
            }
        }

        return $"{label}: imagem ligada{scale}";
    }

    private static double ToUnit(double meters, ForgeTypeId unit) =>
        unit == UnitTypeId.Inches ? meters / 0.0254
        : unit == UnitTypeId.Feet ? meters / 0.3048
        : unit == UnitTypeId.Centimeters ? meters * 100
        : unit == UnitTypeId.Millimeters ? meters * 1000
        : unit == UnitTypeId.Meters ? meters
        : UnitUtils.ConvertToInternalUnits(meters, UnitTypeId.Meters);

    /// <summary>Aparência genérica da biblioteca do Revit, usada como ponto de partida (nula se não houver).</summary>
    public static Asset? GenericTemplate(Autodesk.Revit.ApplicationServices.Application app) =>
        app.GetAssets(AssetType.Appearance)
            .Where(a => a.FindByName(Generic.GenericDiffuse) is not null)
            .OrderByDescending(a => a.Name.Contains("Generic", StringComparison.OrdinalIgnoreCase) || a.Name.Contains("Genérico", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();

    /// <summary>
    /// Textura da pedra gravada na biblioteca (ou na pasta temporária, se a biblioteca for somente leitura). O nome
    /// leva a assinatura do material: muda a cor ou o grão, muda o arquivo.
    /// </summary>
    public static (string File, double SizeM, Rgb Average) StoneTexture(LibraryPaths paths, LooseMaterial material)
    {
        string name = SafeFileName(material.Id) + "-" + Core.Paving.StoneTexture.Signature(material) + ".png";
        double size = Core.Paving.StoneTexture.TileMeters(material);
        foreach (string folder in new[] { paths.StoneTexturesFolder, Path.Combine(Path.GetTempPath(), "Viridian", "Texturas", "Pedras") })
        {
            string file = Path.Combine(folder, name);
            try
            {
                RgbaImage image = Core.Paving.StoneTexture.Render(material);
                if (!File.Exists(file))
                {
                    Directory.CreateDirectory(folder);
                    PngWriter.Save(image, file);
                }

                return (file, size, Core.Paving.StoneTexture.Average(image));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ViridianLog.Warn("Textura da pedra: não foi possível gravar em " + folder, ex);
            }
        }

        throw new ViridianException("Não foi possível gravar a textura da pedra (nem na biblioteca nem na pasta temporária).");
    }

    /// <summary>
    /// Põe a textura na aparência do material (Realista e renderização), na escala real, e a cor média da textura no
    /// material (Sombreado e Cores consistentes). A hachura de superfície continua para a leitura em planta. Precisa
    /// de transação. Devolve uma observação para o registro quando o Revit não aceitar a imagem.
    /// </summary>
    public static string? ApplyStone(Document document, ElementId materialId, string file, double sizeM, Rgb average)
    {
        if (document.GetElement(materialId) is not Material material)
        {
            return "material não encontrado";
        }

        material.Color = new Color(average.R, average.G, average.B);
        try
        {
            string assetName = "VIR Textura - " + material.Name;
            AppearanceAssetElement? asset = AppearanceAssetElement.GetAppearanceAssetElementByName(document, assetName);
            if (asset is null)
            {
                Asset? template = GenericTemplate(document.Application);
                if (template is null)
                {
                    return "sem aparência genérica na biblioteca do Revit: material só com cor";
                }

                asset = AppearanceAssetElement.Create(document, assetName, template);
            }

            material.AppearanceAssetId = asset.Id;
            string note;
            using (var scope = new AppearanceAssetEditScope(document))
            {
                Asset editable = scope.Start(asset.Id);
                note = ConnectBitmap(editable.FindByName(Generic.GenericDiffuse), file, sizeM, "textura");
                scope.Commit(true);
            }

            return note.Contains("ligada", StringComparison.Ordinal) ? null : note;
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException ex)
        {
            return "aparência recusada: " + ex.Message;
        }
    }

    /// <summary>
    /// Aparência de cor lisa (sem imagem) no material, para o estilo Realista e a renderização mostrarem a cor do
    /// material em vez do cinza padrão. Precisa de transação. Falhas ficam só no registro.
    /// </summary>
    public static void SetPlainColor(Document document, Material material, Rgb color)
    {
        try
        {
            string assetName = "VIR Cor - " + color.Hex;
            AppearanceAssetElement? asset = AppearanceAssetElement.GetAppearanceAssetElementByName(document, assetName);
            if (asset is null)
            {
                Asset? template = GenericTemplate(document.Application);
                if (template is null)
                {
                    return;
                }

                asset = AppearanceAssetElement.Create(document, assetName, template);
                using var scope = new AppearanceAssetEditScope(document);
                Asset editable = scope.Start(asset.Id);
                if (editable.FindByName(Generic.GenericDiffuse) is AssetPropertyDoubleArray4d diffuse)
                {
                    // Sem imagem ligada à cor (o modelo genérico pode trazer uma).
                    if (diffuse.GetSingleConnectedAsset() is not null)
                    {
                        diffuse.RemoveConnectedAsset();
                    }

                    diffuse.SetValueAsColor(new Color(color.R, color.G, color.B));
                }

                if (editable.FindByName(Generic.GenericReflectivityAt0deg) is AssetPropertyDouble reflectivity && reflectivity.IsValidValue(0.0))
                {
                    reflectivity.Value = 0.0;
                }

                scope.Commit(true);
            }

            material.AppearanceAssetId = asset.Id;
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException ex)
        {
            ViridianLog.Warn("Aparência de cor do material " + material.Name, ex);
        }
    }

    private static string SafeFileName(string text) =>
        string.Concat(text.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c));
}
