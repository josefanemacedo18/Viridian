using System.Runtime.CompilerServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.DirectContext3D;
using Autodesk.Revit.DB.ExternalService;
using Viridian.Core.Model3D;
using Viridian.Core.Model3D.Illustration;
using Viridian.Library;

namespace Viridian.Revit.Illustrated;

/// <summary>
/// Servidor do serviço DirectContext3D: a cada quadro desenhado pelo Revit, põe a imagem de cada exemplar de frente
/// para a câmera (regra em <see cref="SpriteBillboard"/>) e envia os triângulos coloridos. As cores são as dos
/// vértices, por isso não dependem de material. Em Sombreado e Cores consistentes vão sem normais e sem luz (cor exata).
/// O Realista só desenha geometria com normais e ignora a cor dos vértices (usa a cor do efeito): nele cada cor vai num
/// lote próprio, com um efeito daquela cor. Em Linha oculta a imagem fica em preto e branco (contorno e fundo branco)
/// e em Estrutura de arame só o contorno.
/// </summary>
internal sealed class SpriteServer : IDirectContext3DServer
{
    private static readonly Guid ServerId = new("8E3B7D52-4C1A-4F0B-9C2E-5A61D0B7E9A4");

    /// <summary>Limite de vértices por buffer (os índices do Revit são de 16 bits).</summary>
    private const int MaxVerticesPerChunk = 30000;

    private readonly ConditionalWeakTable<SpriteMesh, GpuSprite> _gpu = new();
    private readonly ConditionalWeakTable<CarpetItem, GpuSprite> _carpets = new();
    private readonly object _lock = new();
    private VertexFormat? _format;
    private EffectInstance? _effect;
    private VertexFormat? _litFormat;
    private readonly Dictionary<Viridian.Core.Colors.Rgb, EffectInstance> _litEffects = new();
    private bool _errorLogged;

    public Guid GetServerId() => ServerId;

    public ExternalServiceId GetServiceId() => ExternalServices.BuiltInExternalServices.DirectContext3DService;

    public string GetName() => "Viridian — vegetação ilustrada";

    public string GetVendorId() => "Viridian";

    public string GetDescription() => "Desenha a vegetação ilustrada das espécies do Viridian, sempre de frente para a câmera, nas vistas 3D, cortes e elevações.";

    public string GetApplicationId() => "Viridian";

    public string GetSourceId() => string.Empty;

    public bool UsesHandles() => false;

    public bool UseInTransparentPass(View dBView) => false;

    public bool CanExecute(View dBView) => IllustratedVegetation.TryGetSnapshot(dBView, out _);

    public Outline GetBoundingBox(View dBView) =>
        IllustratedVegetation.TryGetSnapshot(dBView, out ViewSnapshot snapshot)
            ? new Outline(snapshot.Min, snapshot.Max)
            : new Outline(XYZ.Zero, XYZ.Zero);

    public void RenderScene(View dBView, DisplayStyle displayStyle)
    {
        try
        {
            if (!IllustratedVegetation.TryGetSnapshot(dBView, out ViewSnapshot snapshot) || DrawContext.IsTransparentPass())
            {
                return;
            }

            Camera camera = DrawContext.GetCamera();
            bool perspective = camera.ProjectionMethod == ProjectionMethod.Perspective;
            Vec3 eye = ToVec(camera.EyePosition), direction = ToVec(camera.ViewDirection), up = ToVec(camera.UpDirection);
            Mode mode = displayStyle switch
            {
                DisplayStyle.Wireframe => Mode.Wire,
                DisplayStyle.HLR => Mode.Line,
                DisplayStyle.Realistic or DisplayStyle.RealisticWithEdges or DisplayStyle.Rendering => Mode.Lit,
                _ => Mode.Color,
            };

            double forward = RevitUnits.ToFeet(IllustratedVegetation.ForwardM);
            lock (_lock)
            {
                // Sem luz (cor exata do vértice), como até a 2.2.0: Sombreado, Cores consistentes, Linha oculta, arame.
                if (_format is null || _effect is null || !_format.IsValid() || !_effect.IsValid())
                {
                    _format = new VertexFormat(VertexFormatBits.PositionColored);
                    _effect = new EffectInstance(VertexFormatBits.PositionColored);
                }

                // Realista: com normais, que ele exige; a cor vem do efeito de cada lote de cor (LitEffect).
                if (_litFormat is null || !_litFormat.IsValid())
                {
                    _litFormat = new VertexFormat(VertexFormatBits.PositionNormal);
                }

                VertexFormat format = mode == Mode.Lit ? _litFormat : _format;
                EffectInstance effect = _effect;
                Func<Viridian.Core.Colors.Rgb, EffectInstance> lit = LitEffect;

                // Tapetes dos maciços, no lugar (sem transformação).
                DrawContext.SetWorldTransform(Transform.Identity);
                foreach (CarpetItem carpet in snapshot.Carpets)
                {
                    if (mode == Mode.Wire)
                    {
                        break;
                    }

                    _carpets.GetValue(carpet, c => new GpuSprite(c.Mesh.Vertices, c.Mesh.Triangles, Array.Empty<int>(), c.BaseFeet)).Draw(mode, format, effect, lit);
                }

                foreach (SpriteInstance instance in snapshot.Instances)
                {
                    if (DrawContext.IsInterrupted())
                    {
                        break;
                    }

                    SpriteFrame frame = SpriteBillboard.Place(instance.BaseFeet, instance.Rotation, instance.HeightFeet, eye, direction, up, perspective, forward);
                    SpriteMesh? mesh = instance.Sprites.TryGetNearest(instance.Stage, frame.View);
                    if (mesh is null)
                    {
                        IllustratedVegetation.RequestWarm(instance.Sprites, instance.Stage);
                        continue;
                    }

                    GpuSprite gpu = _gpu.GetValue(mesh, m => new GpuSprite(m.Vertices, m.Triangles, m.OutlineTriangles, 0));
                    var transform = Transform.Identity;
                    transform.Origin = ToXyz(frame.Origin);
                    transform.BasisX = ToXyz(frame.Right);
                    transform.BasisY = ToXyz(frame.Up);
                    transform.BasisZ = ToXyz(frame.Normal);
                    DrawContext.SetWorldTransform(transform);
                    gpu.Draw(mode, format, effect, lit);
                }

                DrawContext.SetWorldTransform(Transform.Identity);
            }
        }
        catch (Exception ex)
        {
            if (!_errorLogged)
            {
                _errorLogged = true;
                ViridianLog.Error("Vegetação ilustrada: falha ao desenhar na vista " + dBView?.Name + ".", ex);
            }
        }
    }

    /// <summary>
    /// Efeito de uma cor para o Realista: ambiente, difusa e emissiva na cor (a emissiva mantém a cor visível qualquer
    /// que seja a luz da vista). Um por cor, reaproveitado entre quadros.
    /// </summary>
    private EffectInstance LitEffect(Viridian.Core.Colors.Rgb color)
    {
        if (_litEffects.TryGetValue(color, out EffectInstance? effect) && effect.IsValid())
        {
            return effect;
        }

        var c = new Color(color.R, color.G, color.B);
        effect = new EffectInstance(VertexFormatBits.PositionNormal);
        effect.SetColor(c);
        effect.SetAmbientColor(c);
        effect.SetDiffuseColor(c);
        effect.SetEmissiveColor(c);
        effect.SetSpecularColor(new Color(0, 0, 0));
        effect.SetGlossiness(0);
        effect.SetTransparency(0);
        _litEffects[color] = effect;
        return effect;
    }

    /// <summary>Cores arredondadas (passo de 16) nos lotes do Realista: poucos lotes por imagem, sem diferença visível na luz do Realista.</summary>
    private static Viridian.Core.Colors.Rgb Quantize(Viridian.Core.Colors.Rgb c)
    {
        static byte Q(byte v) => (byte)Math.Min(255, (v + 8) / 16 * 16);
        return new Viridian.Core.Colors.Rgb(Q(c.R), Q(c.G), Q(c.B));
    }

    private static Vec3 ToVec(XYZ p) => new(p.X, p.Y, p.Z);

    private static XYZ ToXyz(Vec3 v) => new(v.X, v.Y, v.Z);

    internal enum Mode
    {
        /// <summary>Cores dos vértices sem luz (Sombreado, Cores consistentes).</summary>
        Color,

        /// <summary>Cores dos vértices com normais (Realista e renderização).</summary>
        Lit,

        Line,
        Wire,
    }

    /// <summary>Os buffers de uma imagem na placa de vídeo, por estilo (feitos na primeira vez que são desenhados).</summary>
    private sealed class GpuSprite
    {
        private readonly IReadOnlyList<SpriteVertex> _vertices;
        private readonly IReadOnlyList<int> _triangles;
        private readonly IReadOnlyList<int> _outline;
        private readonly double _baseFeet;
        private readonly Dictionary<Mode, List<Chunk>> _chunks = new();

        /// <param name="baseFeet">Cota somada a z (tapetes, que já estão no lugar); zero nas imagens.</param>
        public GpuSprite(IReadOnlyList<SpriteVertex> vertices, IReadOnlyList<int> triangles, IReadOnlyList<int> outline, double baseFeet)
        {
            _vertices = vertices;
            _triangles = triangles;
            _outline = outline;
            _baseFeet = baseFeet;
        }

        public void Draw(Mode mode, VertexFormat format, EffectInstance effect, Func<Viridian.Core.Colors.Rgb, EffectInstance> lit)
        {
            if (!_chunks.TryGetValue(mode, out List<Chunk>? chunks) || chunks.Any(c => !c.IsValid))
            {
                if (chunks is not null)
                {
                    foreach (Chunk old in chunks)
                    {
                        old.Dispose();
                    }
                }

                chunks = Build(mode);
                _chunks[mode] = chunks;
            }

            foreach (Chunk chunk in chunks)
            {
                EffectInstance used = mode == Mode.Lit && chunk.Color is Viridian.Core.Colors.Rgb color ? lit(color) : effect;
                DrawContext.FlushBuffer(chunk.Vertices, chunk.VertexCount, chunk.Indices, chunk.IndexCount, format, used, PrimitiveType.TriangleList, 0, chunk.Primitives);
            }
        }

        private List<Chunk> Build(Mode mode)
        {
            if (mode != Mode.Lit)
            {
                return Build(mode, mode == Mode.Wire ? _outline : _triangles, null);
            }

            // Realista: um lote por cor (o efeito do lote dá a cor).
            var byColor = new Dictionary<Viridian.Core.Colors.Rgb, List<int>>();
            for (int i = 0; i + 2 < _triangles.Count; i += 3)
            {
                Viridian.Core.Colors.Rgb color = Quantize(_vertices[_triangles[i]].Color);
                if (!byColor.TryGetValue(color, out List<int>? list))
                {
                    byColor[color] = list = new List<int>();
                }

                list.Add(_triangles[i]);
                list.Add(_triangles[i + 1]);
                list.Add(_triangles[i + 2]);
            }

            var all = new List<Chunk>();
            foreach ((Viridian.Core.Colors.Rgb color, List<int> list) in byColor)
            {
                all.AddRange(Build(mode, list, color));
            }

            return all;
        }

        private List<Chunk> Build(Mode mode, IReadOnlyList<int> triangles, Viridian.Core.Colors.Rgb? color)
        {
            var result = new List<Chunk>();
            var map = new Dictionary<int, int>();
            var vertices = new List<int>();
            var local = new List<int>();
            double k = RevitUnits.ToFeet(1);

            void Flush()
            {
                if (local.Count == 0)
                {
                    return;
                }

                int vertexCount = vertices.Count;
                VertexBuffer vb;
                if (mode == Mode.Lit)
                {
                    // Normal na direção da câmera (o eixo Z da imagem aponta para ela).
                    vb = new VertexBuffer(vertexCount * VertexPositionNormal.GetSizeInFloats());
                    vb.Map(vertexCount * VertexPositionNormal.GetSizeInFloats());
                    VertexStreamPositionNormal vs = vb.GetVertexStreamPositionNormal();
                    foreach (int index in vertices)
                    {
                        SpriteVertex v = _vertices[index];
                        vs.AddVertex(new VertexPositionNormal(new XYZ(v.X * k, v.Y * k, v.Z * k + _baseFeet), XYZ.BasisZ));
                    }
                }
                else
                {
                    vb = new VertexBuffer(vertexCount * VertexPositionColored.GetSizeInFloats());
                    vb.Map(vertexCount * VertexPositionColored.GetSizeInFloats());
                    VertexStreamPositionColored vs = vb.GetVertexStreamPositionColored();
                    foreach (int index in vertices)
                    {
                        SpriteVertex v = _vertices[index];
                        bool white = mode == Mode.Line && !v.Outline;
                        byte r = white ? (byte)255 : v.Color.R, g = white ? (byte)255 : v.Color.G, b = white ? (byte)255 : v.Color.B;
                        vs.AddVertex(new VertexPositionColored(new XYZ(v.X * k, v.Y * k, v.Z * k + _baseFeet), new ColorWithTransparency(r, g, b, 0)));
                    }
                }

                vb.Unmap();

                // Cada triângulo vai nos dois sentidos: a imagem aparece qualquer que seja o lado considerado frente.
                int primitives = local.Count / 3 * 2;
                int indexCount = primitives * IndexTriangle.GetSizeInShortInts();
                var ib = new IndexBuffer(indexCount);
                ib.Map(indexCount);
                IndexStreamTriangle stream = ib.GetIndexStreamTriangle();
                for (int i = 0; i + 2 < local.Count; i += 3)
                {
                    stream.AddTriangle(new IndexTriangle(local[i], local[i + 1], local[i + 2]));
                    stream.AddTriangle(new IndexTriangle(local[i], local[i + 2], local[i + 1]));
                }

                ib.Unmap();
                result.Add(new Chunk(vb, vertexCount, ib, indexCount, primitives, color));
                map.Clear();
                vertices.Clear();
                local.Clear();
            }

            for (int i = 0; i + 2 < triangles.Count; i += 3)
            {
                if (vertices.Count + 3 > MaxVerticesPerChunk)
                {
                    Flush();
                }

                for (int c = 0; c < 3; c++)
                {
                    int index = triangles[i + c];
                    if (!map.TryGetValue(index, out int localIndex))
                    {
                        localIndex = vertices.Count;
                        map[index] = localIndex;
                        vertices.Add(index);
                    }

                    local.Add(localIndex);
                }
            }

            Flush();
            return result;
        }
    }

    private sealed record Chunk(VertexBuffer Vertices, int VertexCount, IndexBuffer Indices, int IndexCount, int Primitives, Viridian.Core.Colors.Rgb? Color) : IDisposable
    {
        public bool IsValid => Vertices.IsValid() && Indices.IsValid();

        public void Dispose()
        {
            Vertices.Dispose();
            Indices.Dispose();
        }
    }
}
