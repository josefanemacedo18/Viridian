namespace Viridian.Core.Paving;

/// <summary>Quantitativo de um tipo de piso aplicado no projeto.</summary>
public sealed class PavingQuantity
{
    public PavingType Type { get; init; } = new();

    public string TypeName => Type.Name;

    /// <summary>Área de projeto (m²).</summary>
    public double AreaM2 { get; init; }

    public int Elements { get; init; }

    /// <summary>Área de compra com a perda (m²).</summary>
    public double PurchaseAreaM2 => AreaM2 * (1 + Type.WastePercent / 100);

    /// <summary>Peças (nulo para pisos moldados, granulares, cacos e mosaicos).</summary>
    public int? Pieces => PavingLabels.IsModular(Type) && Type.LengthMm > 0 && Type.WidthMm > 0
        ? (int)Math.Ceiling(PurchaseAreaM2 / PieceAreaM2(Type) - 1e-9)
        : null;

    /// <summary>Volume do revestimento (m³): concreto, fulget, pedrisco.</summary>
    public double SurfaceVolumeM3 => AreaM2 * Type.ThicknessMm / 1000;

    /// <summary>Volume de cada camada (m³); mantas em m².</summary>
    public IReadOnlyList<(PavingLayer Layer, double Quantity, string Unit)> Layers =>
        Type.Layers.Select(l => l.Kind == PavingLayerKind.Manta
                ? (l, AreaM2 * 1.1, "m²") // sobreposição de cerca de 10%
                : (l, AreaM2 * l.ThicknessMm / 1000, "m³"))
            .ToList();

    /// <summary>Área ocupada por uma peça, contando metade da junta de cada lado (m²).</summary>
    public static double PieceAreaM2(PavingType t)
    {
        double area = t.LengthMm * t.WidthMm / 1e6;
        if (t.Pattern == LayingPattern.Hexagonal)
        {
            area = Math.Sqrt(3) / 2 * t.LengthMm * t.LengthMm / 1e6; // hexágono pela distância entre lados
        }

        return Math.Max(1e-6, area);
    }

    /// <summary>Descrição da quantidade de compra do revestimento.</summary>
    public string PurchaseText
    {
        get
        {
            var pt = System.Globalization.CultureInfo.GetCultureInfo("pt-BR");
            string area = PurchaseAreaM2.ToString("N2", pt) + " m²";
            if (Pieces is int pieces)
            {
                return $"{area} ({pieces.ToString("N0", pt)} peças)";
            }

            return Type.CastInPlace || Type.Pattern == LayingPattern.Granular
                ? $"{area} · {(SurfaceVolumeM3 * (1 + Type.WastePercent / 100)).ToString("N2", pt)} m³"
                : area;
        }
    }
}

/// <summary>Planilha do quantitativo de pisos.</summary>
public static class PavingSheets
{
    public static Export.XlsxSheet Build(IReadOnlyList<PavingQuantity> rows, string projectTitle, DateTime at)
    {
        var sheet = new Export.XlsxSheet("Pisos");
        sheet.TitleLines.Add("Quantitativo de pisos e pavimentações");
        sheet.TitleLines.Add((string.IsNullOrWhiteSpace(projectTitle) ? string.Empty : projectTitle + " · ")
                             + "Gerado pelo Viridian em " + at.ToString("dd/MM/yyyy HH:mm", System.Globalization.CultureInfo.GetCultureInfo("pt-BR")));
        sheet.Columns.AddRange(new[]
        {
            new Export.XlsxColumn("Piso", 34), new Export.XlsxColumn("Categoria", 20), new Export.XlsxColumn("Peça", 22),
            new Export.XlsxColumn("Pisos no projeto", 10, Export.XlsxFormat.Integer), new Export.XlsxColumn("Área (m²)", 12, Export.XlsxFormat.Decimal),
            new Export.XlsxColumn("Perda (%)", 9, Export.XlsxFormat.Decimal), new Export.XlsxColumn("Área de compra (m²)", 14, Export.XlsxFormat.Decimal),
            new Export.XlsxColumn("Peças", 10, Export.XlsxFormat.Integer), new Export.XlsxColumn("Camada", 34),
            new Export.XlsxColumn("Quantidade", 12, Export.XlsxFormat.Decimal), new Export.XlsxColumn("Unidade", 8),
        });
        foreach (PavingQuantity q in rows)
        {
            sheet.Rows.Add(new Export.XlsxRow(q.Type.Name, PavingLabels.Of(q.Type.Category), PavingDetail.Size(q.Type), q.Elements,
                Math.Round(q.AreaM2, 2), q.Type.WastePercent, Math.Round(q.PurchaseAreaM2, 2), q.Pieces, "Revestimento: " + q.Type.Material,
                Math.Round(q.Type.CastInPlace || q.Type.Pattern == LayingPattern.Granular ? q.SurfaceVolumeM3 : q.PurchaseAreaM2, 3),
                q.Type.CastInPlace || q.Type.Pattern == LayingPattern.Granular ? "m³" : "m²") { Bold = true });
            foreach ((PavingLayer layer, double quantity, string unit) in q.Layers)
            {
                sheet.Rows.Add(new Export.XlsxRow(null, null, null, null, null, null, null, null, layer.Name, Math.Round(quantity, 3), unit));
            }
        }

        sheet.Rows.Add(new Export.XlsxRow("Total", null, null, rows.Sum(r => r.Elements), Math.Round(rows.Sum(r => r.AreaM2), 2), null,
            Math.Round(rows.Sum(r => r.PurchaseAreaM2), 2), null, null, null, null) { Bold = true });
        return sheet;
    }
}
