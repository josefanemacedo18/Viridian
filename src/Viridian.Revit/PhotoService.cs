using System.IO;
using Autodesk.Revit.DB;
using Viridian.Core.Geometry;
using Viridian.Core.Identification;
using Viridian.Core.Model;
using Viridian.Library;

namespace Viridian.Revit;

/// <summary>Vínculo gravado nos elementos do quadro de fotos (para refazer o quadro sem duplicar).</summary>
public sealed class PhotoBoardLink
{
    public long ViewId { get; set; }

    public string SpeciesId { get; set; } = string.Empty;
}

/// <summary>Espécie a ilustrar no quadro: a espécie, o código no projeto e onde ela aparece na vista.</summary>
public sealed record PhotoBoardSpecies(Species Species, string Code, string PhotoFile, IReadOnlyList<Point2> Targets);

/// <summary>Fotografias das espécies em vistas do Revit: legenda ilustrada e quadro de fotos ao redor da planta.</summary>
public sealed class PhotoService
{
    /// <summary>Tamanho do cartão no papel, em milímetros.</summary>
    public const double CardWidthMm = 34;
    public const double PhotoHeightMm = 25;
    public const double CardHeightMm = 34;
    public const double GapMm = 6;

    private readonly LabelService _labels;

    public PhotoService(LabelService labels)
    {
        _labels = labels;
    }

    /// <summary>Tipo de imagem da fotografia (importada para dentro do projeto; reaproveitada se já existir).</summary>
    public static ImageType? EnsureImageType(Document document, string file)
    {
        if (string.IsNullOrWhiteSpace(file) || !File.Exists(file))
        {
            return null;
        }

        string name = Path.GetFileName(file);
        using (var collector = new FilteredElementCollector(document))
        {
            ImageType? existing = collector.OfClass(typeof(ImageType)).Cast<ImageType>()
                .FirstOrDefault(t => string.Equals(SafeName(t), name, StringComparison.OrdinalIgnoreCase)
                                     || string.Equals(SafePath(t), file, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                return existing;
            }
        }

        try
        {
            using var options = new ImageTypeOptions(file, false, ImageTypeSource.Import);
            return ImageType.Create(document, options);
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException ex)
        {
            ViridianLog.Warn("Não foi possível carregar a fotografia " + file, ex);
            return null;
        }
    }

    /// <summary>
    /// Coloca a foto com o canto superior esquerdo no ponto informado, cabendo na largura e na altura dadas
    /// (em pés, no modelo), sem distorcer. Precisa de transação.
    /// </summary>
    public static ImageInstance? PlaceImage(Document document, View view, ImageType type, XYZ topLeft, double maxWidth, double maxHeight)
    {
        try
        {
            using var placement = new ImagePlacementOptions(topLeft, BoxPlacement.TopLeft);
            ImageInstance image = ImageInstance.Create(document, view, type.Id, placement);
            image.LockProportions = true;
            image.Width = maxWidth;
            if (image.Height > maxHeight)
            {
                image.Height = maxHeight;
            }

            // Centraliza horizontalmente no espaço reservado.
            double slack = maxWidth - image.Width;
            image.SetLocation(new XYZ(topLeft.X + slack / 2, topLeft.Y, topLeft.Z), BoxPlacement.TopLeft);
            return image;
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException ex)
        {
            ViridianLog.Warn("Não foi possível colocar a fotografia na vista.", ex);
            return null;
        }
    }

    /// <summary>
    /// Cria (ou refaz) o quadro de fotos na vista: um cartão por espécie nas margens do desenho, com foto,
    /// código e nome, ligado por linha a um exemplar. Retorna a quantidade de cartões. Precisa de transação.
    /// </summary>
    public int CreateBoard(Document document, View view, IReadOnlyList<PhotoBoardSpecies> species)
    {
        RemoveBoard(document, view);
        if (species.Count == 0)
        {
            return 0;
        }

        double scale = Math.Max(1, view.Scale);
        double M(double paperMm) => paperMm * scale / 1000.0;

        Bounds2 drawing = DrawingBounds(view, species);
        var items = species.Select(s => new PhotoBoardItem(s.Species.Id, s.Targets)).ToList();
        IReadOnlyList<PhotoCard> cards = PhotoBoardLayout.Arrange(drawing, items, M(CardWidthMm), M(CardHeightMm), M(GapMm));
        double z = PlantingService.LevelOf(view)?.ProjectElevation ?? 0;
        ElementId textType = _labels.EnsureTextType(document).Id;
        var bySpecies = species.ToDictionary(s => s.Species.Id, StringComparer.OrdinalIgnoreCase);

        foreach (PhotoCard card in cards)
        {
            PhotoBoardSpecies item = bySpecies[card.Key];
            var link = new PhotoBoardLink { ViewId = view.Id.Value, SpeciesId = item.Species.Id };
            var created = new List<Element>();

            XYZ topLeft = RevitUnits.ToXyz(new Point2(card.Box.MinX, card.Box.MaxY), z);
            if (EnsureImageType(document, item.PhotoFile) is ImageType type
                && PlaceImage(document, view, type, topLeft, RevitUnits.ToFeet(M(CardWidthMm)), RevitUnits.ToFeet(M(PhotoHeightMm))) is ImageInstance image)
            {
                created.Add(image);
            }
            else
            {
                // Sem foto: moldura vazia, para manter o quadro organizado.
                created.AddRange(Rectangle(document, view, new Bounds2(card.Box.MinX, card.Box.MaxY - M(PhotoHeightMm), card.Box.MaxX, card.Box.MaxY), z));
            }

            string text = (item.Code + " " + item.Species.PopularName).Trim().ToUpperInvariant() + "\n" + item.Species.ScientificName;
            using (var options = new TextNoteOptions(textType) { HorizontalAlignment = HorizontalTextAlignment.Center })
            {
                var at = new Point2(card.Box.Center.X, card.Box.MaxY - M(PhotoHeightMm + 1.5));
                TextNote note = TextNote.Create(document, view.Id, RevitUnits.ToXyz(at, z), RevitUnits.MmToFeet(CardWidthMm), LabelService.ToRevitText(text), options); // largura no papel
                created.Add(note);
            }

            // Linha indicadora até o exemplar, com um ponto marcando a planta.
            XYZ a = RevitUnits.ToXyz(card.Anchor, z);
            XYZ b = RevitUnits.ToXyz(card.Target, z);
            if (a.DistanceTo(b) > document.Application.ShortCurveTolerance * 2)
            {
                created.Add(document.Create.NewDetailCurve(view, Line.CreateBound(a, b)));
            }

            double dot = RevitUnits.ToFeet(M(0.8));
            if (dot * Math.PI > document.Application.ShortCurveTolerance * 2)
            {
                created.Add(document.Create.NewDetailCurve(view, Arc.Create(b, dot, 0, 2 * Math.PI * 0.999, XYZ.BasisX, XYZ.BasisY)));
            }

            foreach (Element element in created)
            {
                ElementData.Write(element, ElementData.KindPhotoBoard, link);
            }
        }

        ExpandCrop(view, cards, drawing, M(GapMm));
        return cards.Count;
    }

    /// <summary>Com região de corte ativa, amplia o recorte para que os cartões fiquem visíveis na vista e na prancha.</summary>
    private static void ExpandCrop(View view, IReadOnlyList<PhotoCard> cards, Bounds2 drawing, double margin)
    {
        if (!view.CropBoxActive || cards.Count == 0 || view.CropBox is not BoundingBoxXYZ crop)
        {
            return;
        }

        Bounds2 all = Bounds2.FromPoints(cards.SelectMany(c => new[] { new Point2(c.Box.MinX, c.Box.MinY), new Point2(c.Box.MaxX, c.Box.MaxY) })
            .Concat(new[] { new Point2(drawing.MinX, drawing.MinY), new Point2(drawing.MaxX, drawing.MaxY) })).Expand(margin);
        try
        {
            Transform inverse = crop.Transform.Inverse;
            var corners = new[]
            {
                inverse.OfPoint(RevitUnits.ToXyz(new Point2(all.MinX, all.MinY))), inverse.OfPoint(RevitUnits.ToXyz(new Point2(all.MaxX, all.MinY))),
                inverse.OfPoint(RevitUnits.ToXyz(new Point2(all.MaxX, all.MaxY))), inverse.OfPoint(RevitUnits.ToXyz(new Point2(all.MinX, all.MaxY))),
            };
            var expanded = new BoundingBoxXYZ
            {
                Transform = crop.Transform,
                Min = new XYZ(Math.Min(crop.Min.X, corners.Min(c => c.X)), Math.Min(crop.Min.Y, corners.Min(c => c.Y)), crop.Min.Z),
                Max = new XYZ(Math.Max(crop.Max.X, corners.Max(c => c.X)), Math.Max(crop.Max.Y, corners.Max(c => c.Y)), crop.Max.Z),
            };
            view.CropBox = expanded;
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException ex)
        {
            ViridianLog.Warn("Não foi possível ampliar a região de corte para o quadro de fotos.", ex);
        }
    }

    /// <summary>Remove o quadro de fotos anterior desta vista.</summary>
    public static int RemoveBoard(Document document, View view)
    {
        var ids = new List<ElementId>();
        foreach (Element element in ElementData.ElementsOfKind(document, ElementData.KindPhotoBoard))
        {
            if (element.OwnerViewId == view.Id)
            {
                ids.Add(element.Id);
            }
        }

        if (ids.Count > 0)
        {
            document.Delete(ids);
        }

        return ids.Count;
    }

    /// <summary>Área do desenho: a região de corte da vista, ou a extensão das plantas com folga.</summary>
    private static Bounds2 DrawingBounds(View view, IReadOnlyList<PhotoBoardSpecies> species)
    {
        if (view.CropBoxActive && view.CropBox is BoundingBoxXYZ crop)
        {
            XYZ p1 = crop.Transform.OfPoint(crop.Min);
            XYZ p2 = crop.Transform.OfPoint(crop.Max);
            Point2 a = RevitUnits.ToPoint2(p1);
            Point2 b = RevitUnits.ToPoint2(p2);
            return new Bounds2(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
        }

        Bounds2 extent = Bounds2.FromPoints(species.SelectMany(s => s.Targets));
        double margin = Math.Max(2, species.Max(s => s.Species.CrownDiameterM));
        return extent.Expand(margin);
    }

    private static IEnumerable<Element> Rectangle(Document document, View view, Bounds2 box, double z)
    {
        Point2[] corners = { new(box.MinX, box.MinY), new(box.MaxX, box.MinY), new(box.MaxX, box.MaxY), new(box.MinX, box.MaxY) };
        for (int i = 0; i < 4; i++)
        {
            yield return document.Create.NewDetailCurve(view, Line.CreateBound(RevitUnits.ToXyz(corners[i], z), RevitUnits.ToXyz(corners[(i + 1) % 4], z)));
        }
    }

    private static string SafeName(ImageType type)
    {
        try
        {
            return type.Name;
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException)
        {
            return string.Empty;
        }
    }

    private static string SafePath(ImageType type)
    {
        try
        {
            return type.Path ?? string.Empty;
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException)
        {
            return string.Empty;
        }
    }
}
