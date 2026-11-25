using System.IO;
using Autodesk.Revit.DB;
using Viridian.Core.Detailing;
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

    /// <summary>Cartão a que o elemento pertence (foto, texto, linha e ponto do mesmo cartão).</summary>
    public string CardId { get; set; } = string.Empty;

    /// <summary>Papel do elemento: foto, texto, linha, ponto ou moldura.</summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>Exemplar ligado ao cartão (a ponta da linha acompanha a planta).</summary>
    public string TargetUniqueId { get; set; } = string.Empty;

    /// <summary>Posição do texto em relação ao canto superior esquerdo da foto (pés).</summary>
    public double TextDx { get; set; }

    public double TextDy { get; set; }

    public const string RoleImage = "foto";
    public const string RoleText = "texto";
    public const string RoleLeader = "linha";
    public const string RoleDot = "ponto";
    public const string RoleFrame = "moldura";
}

/// <summary>Espécie a ilustrar no quadro: a espécie, o código no projeto e onde ela aparece na vista.</summary>
public sealed record PhotoBoardSpecies(Species Species, string Code, string PhotoFile, IReadOnlyList<Point2> Targets)
{
    /// <summary>Exemplares correspondentes a <see cref="Targets"/> (mesma ordem), para a linha acompanhar a planta.</summary>
    public IReadOnlyList<string> TargetIds { get; init; } = Array.Empty<string>();
}

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
        GraphicsStyle? framePen = DrawingRenderer.PenStyle(document, view, LinePen.Media);
        GraphicsStyle? leaderPen = DrawingRenderer.PenStyle(document, view, LinePen.Fina);

        foreach (PhotoCard card in cards)
        {
            PhotoBoardSpecies item = bySpecies[card.Key];
            string cardId = Guid.NewGuid().ToString("N");
            int targetIndex = item.Targets.ToList().FindIndex(t => t.DistanceTo(card.Target) < 1e-6);
            string targetId = targetIndex >= 0 && targetIndex < item.TargetIds.Count ? item.TargetIds[targetIndex] : string.Empty;
            PhotoBoardLink Link(string role, double dx = 0, double dy = 0) => new()
            {
                ViewId = view.Id.Value, SpeciesId = item.Species.Id, CardId = cardId, Role = role, TargetUniqueId = targetId, TextDx = dx, TextDy = dy,
            };

            XYZ topLeft = RevitUnits.ToXyz(new Point2(card.Box.MinX, card.Box.MaxY), z);
            if (EnsureImageType(document, item.PhotoFile) is ImageType type
                && PlaceImage(document, view, type, topLeft, RevitUnits.ToFeet(M(CardWidthMm)), RevitUnits.ToFeet(M(PhotoHeightMm))) is ImageInstance image)
            {
                ElementData.Write(image, ElementData.KindPhotoBoard, Link(PhotoBoardLink.RoleImage));
                topLeft = image.GetLocation(BoxPlacement.TopLeft);

                // Moldura fina em volta da foto (acompanha a foto quando ela é arrastada).
                XYZ bottomRight = image.GetLocation(BoxPlacement.BottomRight);
                foreach (DetailCurve line in Rectangle(document, view, Bounds2.FromPoints(new[] { RevitUnits.ToPoint2(topLeft), RevitUnits.ToPoint2(bottomRight) }), z).OfType<DetailCurve>())
                {
                    if (framePen is not null)
                    {
                        line.LineStyle = framePen;
                    }

                    XYZ start = line.GeometryCurve.GetEndPoint(0);
                    ElementData.Write(line, ElementData.KindPhotoBoard, Link(PhotoBoardLink.RoleFrame, start.X - topLeft.X, start.Y - topLeft.Y));
                }
            }
            else
            {
                // Sem foto: moldura vazia, para manter o quadro organizado.
                foreach (Element line in Rectangle(document, view, new Bounds2(card.Box.MinX, card.Box.MaxY - M(PhotoHeightMm), card.Box.MaxX, card.Box.MaxY), z))
                {
                    ElementData.Write(line, ElementData.KindPhotoBoard, Link(PhotoBoardLink.RoleFrame));
                }
            }

            string title = (item.Code + " " + item.Species.PopularName).Trim().ToUpperInvariant();
            string text = title + "\n" + item.Species.ScientificName;
            using (var options = new TextNoteOptions(textType) { HorizontalAlignment = HorizontalTextAlignment.Center })
            {
                var at = new Point2(card.Box.Center.X, card.Box.MaxY - M(PhotoHeightMm + 1.5));
                XYZ position = RevitUnits.ToXyz(at, z);
                TextNote note = TextNote.Create(document, view.Id, position, RevitUnits.MmToFeet(CardWidthMm), LabelService.ToRevitText(text), options); // largura no papel
                ElementData.Write(note, ElementData.KindPhotoBoard, Link(PhotoBoardLink.RoleText, position.X - topLeft.X, position.Y - topLeft.Y));
                Emphasize(note, title.Length, item.Species.ScientificName.Length);
            }

            // Linha indicadora até o exemplar, com um ponto marcando a planta.
            XYZ a = RevitUnits.ToXyz(card.Anchor, z);
            XYZ b = RevitUnits.ToXyz(card.Target, z);
            if (a.DistanceTo(b) > document.Application.ShortCurveTolerance * 2)
            {
                DetailCurve leader = document.Create.NewDetailCurve(view, Line.CreateBound(a, b));
                if (leaderPen is not null)
                {
                    leader.LineStyle = leaderPen;
                }

                ElementData.Write(leader, ElementData.KindPhotoBoard, Link(PhotoBoardLink.RoleLeader));
            }

            double dot = RevitUnits.ToFeet(M(1.0));
            if (dot * Math.PI > document.Application.ShortCurveTolerance * 2)
            {
                DetailCurve marker = document.Create.NewDetailCurve(view, Arc.Create(b, dot, 0, 2 * Math.PI * 0.999, XYZ.BasisX, XYZ.BasisY));
                if (framePen is not null)
                {
                    marker.LineStyle = framePen;
                }

                ElementData.Write(marker, ElementData.KindPhotoBoard, Link(PhotoBoardLink.RoleDot));
            }
        }

        ExpandCrop(view, cards, drawing, M(GapMm));
        return cards.Count;
    }

    /// <summary>
    /// Refaz um cartão depois que a foto (ou o exemplar) foi movida: o texto acompanha a foto e a linha sai do lado do
    /// cartão voltado para a planta até a posição atual da planta. Chamado pelo atualizador; já está em transação.
    /// </summary>
    public static void FollowCard(Document document, string cardId)
    {
        var parts = new List<(Element Element, PhotoBoardLink Link)>();
        foreach (Element element in ElementData.ElementsOfKind(document, ElementData.KindPhotoBoard))
        {
            if (ElementData.TryRead(element, ElementData.KindPhotoBoard, out PhotoBoardLink link) && link.CardId == cardId)
            {
                parts.Add((element, link));
            }
        }

        if (parts.FirstOrDefault(p => p.Element is ImageInstance).Element is not ImageInstance image)
        {
            return;
        }

        XYZ topLeft = image.GetLocation(BoxPlacement.TopLeft);
        XYZ bottomRight = image.GetLocation(BoxPlacement.BottomRight);
        double z = topLeft.Z;
        Bounds2 box = Bounds2.FromPoints(new[] { RevitUnits.ToPoint2(topLeft), RevitUnits.ToPoint2(bottomRight) });

        // Texto logo abaixo da foto, na mesma posição relativa de quando o quadro foi criado.
        foreach ((Element element, PhotoBoardLink link) in parts.Where(p => p.Link.Role == PhotoBoardLink.RoleText))
        {
            if (element is TextNote note)
            {
                XYZ wanted = new(topLeft.X + link.TextDx, topLeft.Y + link.TextDy, note.Coord.Z);
                XYZ delta = wanted - note.Coord;
                if (delta.GetLength() > 1e-6)
                {
                    ElementTransformUtils.MoveElement(document, note.Id, delta);
                }

                Bounds2 textBox = TextBounds(note) ?? box;
                box = Bounds2.FromPoints(new[] { new Point2(box.MinX, box.MaxY), new Point2(box.MaxX, Math.Min(box.MinY, textBox.MinY)) });
            }
        }

        // Moldura: cada lado volta à posição relativa à foto.
        foreach ((Element element, PhotoBoardLink link) in parts.Where(p => p.Link.Role == PhotoBoardLink.RoleFrame))
        {
            if (element is CurveElement { GeometryCurve: Curve side } frameLine)
            {
                XYZ start = side.GetEndPoint(0);
                XYZ wanted = new(topLeft.X + link.TextDx, topLeft.Y + link.TextDy, start.Z);
                XYZ delta = wanted - start;
                if (delta.GetLength() > 1e-6)
                {
                    ElementTransformUtils.MoveElement(document, frameLine.Id, delta);
                }
            }
        }

        // Ponta da linha: o exemplar ligado (se ainda existe) ou a ponta atual.
        PhotoBoardLink any = parts[0].Link;
        Element? leader = parts.FirstOrDefault(p => p.Link.Role == PhotoBoardLink.RoleLeader).Element;
        Element? dot = parts.FirstOrDefault(p => p.Link.Role == PhotoBoardLink.RoleDot).Element;
        XYZ? target = null;
        if (any.TargetUniqueId.Length > 0 && document.GetElement(any.TargetUniqueId) is FamilyInstance { Location: LocationPoint lp })
        {
            target = new XYZ(lp.Point.X, lp.Point.Y, z);
        }
        else if (leader is CurveElement { GeometryCurve: Curve current })
        {
            target = current.GetEndPoint(1);
        }

        if (target is null)
        {
            return;
        }

        Point2 anchor = PhotoCardGeometry.Anchor(box, RevitUnits.ToPoint2(target));
        XYZ a = RevitUnits.ToXyz(anchor, z);
        if (leader is CurveElement curve && a.DistanceTo(target) > document.Application.ShortCurveTolerance * 2)
        {
            curve.SetGeometryCurve(Line.CreateBound(a, new XYZ(target.X, target.Y, z)), true);
        }

        if (dot is CurveElement circle && circle.GeometryCurve is Arc arc && arc.Center.DistanceTo(new XYZ(target.X, target.Y, arc.Center.Z)) > 1e-6)
        {
            ElementTransformUtils.MoveElement(document, circle.Id, new XYZ(target.X - arc.Center.X, target.Y - arc.Center.Y, 0));
        }
    }

    /// <summary>Primeira linha (código e nome popular) em negrito e o nome científico em itálico.</summary>
    private static void Emphasize(TextNote note, int titleLength, int scientificLength)
    {
        try
        {
            FormattedText formatted = note.GetFormattedText();
            if (titleLength > 0)
            {
                formatted.SetBoldStatus(new TextRange(0, titleLength), true);
            }

            if (scientificLength > 0)
            {
                formatted.SetItalicStatus(new TextRange(titleLength + 1, scientificLength), true);
            }

            note.SetFormattedText(formatted);
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException ex)
        {
            ViridianLog.Warn("Quadro de fotos: formatação do texto.", ex);
        }
    }

    private static Bounds2? TextBounds(TextNote note)
    {
        try
        {
            View? view = note.Document.GetElement(note.OwnerViewId) as View;
            BoundingBoxXYZ? bb = note.get_BoundingBox(view);
            return bb is null ? null : Bounds2.FromPoints(new[] { RevitUnits.ToPoint2(bb.Min), RevitUnits.ToPoint2(bb.Max) });
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException)
        {
            return null;
        }
    }

    /// <summary>Cartões ligados a um exemplar (para a linha acompanhar a planta movida).</summary>
    public static IEnumerable<string> CardsOfTarget(Document document, ICollection<string> targetUniqueIds)
    {
        var cards = new HashSet<string>();
        foreach (Element element in ElementData.ElementsOfKind(document, ElementData.KindPhotoBoard))
        {
            if (ElementData.TryRead(element, ElementData.KindPhotoBoard, out PhotoBoardLink link)
                && link.CardId.Length > 0 && targetUniqueIds.Contains(link.TargetUniqueId))
            {
                cards.Add(link.CardId);
            }
        }

        return cards;
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
