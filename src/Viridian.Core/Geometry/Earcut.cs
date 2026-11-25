namespace Viridian.Core.Geometry;

/// <summary>
/// Triangulação de polígonos com furos por remoção de orelhas, adaptada do algoritmo "earcut" (Mapbox, licença ISC).
/// Aceita contornos imperfeitos (pontos repetidos, pequenas autointerseções): sempre devolve o que conseguir
/// triangular, sem lançar exceções.
/// </summary>
public static class Earcut
{
    /// <summary>
    /// Triangula um contorno externo com seus furos. Os índices devolvidos (de três em três) apontam para a lista
    /// formada pelo contorno seguido dos furos, na ordem recebida; os triângulos saem no sentido anti-horário.
    /// </summary>
    public static List<int> Triangulate(IReadOnlyList<Point2> outer, IReadOnlyList<IReadOnlyList<Point2>>? holes = null)
    {
        ArgumentNullException.ThrowIfNull(outer);
        var xs = new List<double>();
        var ys = new List<double>();
        var holeStarts = new List<int>();
        foreach (Point2 p in outer)
        {
            xs.Add(p.X);
            ys.Add(p.Y);
        }

        if (holes is not null)
        {
            foreach (IReadOnlyList<Point2> hole in holes)
            {
                if (hole.Count < 3)
                {
                    continue;
                }

                holeStarts.Add(xs.Count);
                foreach (Point2 p in hole)
                {
                    xs.Add(p.X);
                    ys.Add(p.Y);
                }
            }
        }

        var triangles = new List<int>();
        var e = new Run(xs, ys, triangles);
        int outerEnd = holeStarts.Count > 0 ? holeStarts[0] : xs.Count;
        Node? outerNode = e.LinkedList(0, outerEnd, true);
        if (outerNode is null || outerNode.Next == outerNode.Prev)
        {
            return triangles;
        }

        if (holeStarts.Count > 0)
        {
            outerNode = e.EliminateHoles(holeStarts, outerNode);
        }

        e.EarcutLinked(outerNode, 0);
        return triangles;
    }

    private sealed class Node
    {
        public Node(int i, double x, double y)
        {
            I = i;
            X = x;
            Y = y;
            Prev = this;
            Next = this;
        }

        public int I { get; }

        public double X { get; }

        public double Y { get; }

        public Node Prev { get; set; }

        public Node Next { get; set; }

        public bool Steiner { get; set; }
    }

    private sealed class Run
    {
        private readonly List<double> _x;
        private readonly List<double> _y;
        private readonly List<int> _triangles;
        private int _budget;

        public Run(List<double> x, List<double> y, List<int> triangles)
        {
            _x = x;
            _y = y;
            _triangles = triangles;

            // Proteção contra laços patológicos: o trabalho total é limitado (bem acima do necessário).
            long n = x.Count + 16;
            _budget = (int)Math.Min(int.MaxValue, n * n * 8 + 100000);
        }

        public Node? LinkedList(int start, int end, bool clockwise)
        {
            Node? last = null;
            if (clockwise == (SignedArea(start, end) > 0))
            {
                for (int i = start; i < end; i++)
                {
                    last = Insert(i, last);
                }
            }
            else
            {
                for (int i = end - 1; i >= start; i--)
                {
                    last = Insert(i, last);
                }
            }

            if (last is not null && Equals(last, last.Next))
            {
                Remove(last);
                last = last.Next;
            }

            return last;
        }

        private double SignedArea(int start, int end)
        {
            double sum = 0;
            for (int i = start, j = end - 1; i < end; j = i++)
            {
                sum += (_x[j] - _x[i]) * (_y[i] + _y[j]);
            }

            return sum;
        }

        private Node Insert(int i, Node? last)
        {
            var p = new Node(i, _x[i], _y[i]);
            if (last is not null)
            {
                p.Next = last.Next;
                p.Prev = last;
                last.Next.Prev = p;
                last.Next = p;
            }

            return p;
        }

        private static void Remove(Node p)
        {
            p.Next.Prev = p.Prev;
            p.Prev.Next = p.Next;
        }

        private static bool Equals(Node a, Node b) => a.X == b.X && a.Y == b.Y;

        private static double Area(Node p, Node q, Node r) => (q.Y - p.Y) * (r.X - q.X) - (q.X - p.X) * (r.Y - q.Y);

        private Node? Filter(Node? start, Node? end = null)
        {
            if (start is null)
            {
                return null;
            }

            end ??= start;
            Node p = start;
            bool again;
            do
            {
                again = false;
                if (--_budget < 0)
                {
                    return end;
                }

                if (!p.Steiner && (Equals(p, p.Next) || Area(p.Prev, p, p.Next) == 0))
                {
                    Remove(p);
                    p = end = p.Prev;
                    if (p == p.Next)
                    {
                        break;
                    }

                    again = true;
                }
                else
                {
                    p = p.Next;
                }
            }
            while (again || p != end);

            return end;
        }

        public void EarcutLinked(Node? ear, int pass)
        {
            if (ear is null)
            {
                return;
            }

            Node stop = ear;
            while (ear.Prev != ear.Next)
            {
                if (--_budget < 0)
                {
                    return;
                }

                Node prev = ear.Prev, next = ear.Next;
                if (IsEar(ear))
                {
                    _triangles.Add(prev.I);
                    _triangles.Add(ear.I);
                    _triangles.Add(next.I);
                    Remove(ear);
                    ear = next.Next;
                    stop = next.Next;
                    continue;
                }

                ear = next;
                if (ear == stop)
                {
                    if (pass == 0)
                    {
                        EarcutLinked(Filter(ear), 1);
                    }
                    else if (pass == 1)
                    {
                        Node? cured = CureLocalIntersections(Filter(ear));
                        EarcutLinked(cured, 2);
                    }
                    else if (pass == 2)
                    {
                        SplitEarcut(ear);
                    }

                    break;
                }
            }
        }

        private bool IsEar(Node ear)
        {
            Node a = ear.Prev, b = ear, c = ear.Next;
            if (Area(a, b, c) >= 0)
            {
                return false; // vértice côncavo
            }

            double x0 = Math.Min(a.X, Math.Min(b.X, c.X)), y0 = Math.Min(a.Y, Math.Min(b.Y, c.Y));
            double x1 = Math.Max(a.X, Math.Max(b.X, c.X)), y1 = Math.Max(a.Y, Math.Max(b.Y, c.Y));
            Node p = c.Next;
            while (p != a)
            {
                if (--_budget < 0)
                {
                    return false;
                }

                if (p.X >= x0 && p.X <= x1 && p.Y >= y0 && p.Y <= y1
                    && PointInTriangle(a.X, a.Y, b.X, b.Y, c.X, c.Y, p.X, p.Y)
                    && Area(p.Prev, p, p.Next) >= 0)
                {
                    return false;
                }

                p = p.Next;
            }

            return true;
        }

        private Node? CureLocalIntersections(Node? start)
        {
            if (start is null)
            {
                return null;
            }

            Node p = start;
            do
            {
                if (--_budget < 0)
                {
                    break;
                }

                Node a = p.Prev, b = p.Next.Next;
                if (!Equals(a, b) && Intersects(a, p, p.Next, b) && LocallyInside(a, b) && LocallyInside(b, a))
                {
                    _triangles.Add(a.I);
                    _triangles.Add(p.I);
                    _triangles.Add(b.I);
                    Remove(p);
                    Remove(p.Next);
                    p = start = b;
                }

                p = p.Next;
            }
            while (p != start);

            return Filter(p);
        }

        private void SplitEarcut(Node start)
        {
            Node a = start;
            do
            {
                Node b = a.Next.Next;
                while (b != a.Prev)
                {
                    if (--_budget < 0)
                    {
                        return;
                    }

                    if (a.I != b.I && IsValidDiagonal(a, b))
                    {
                        Node c = Split(a, b);
                        Node? fa = Filter(a, a.Next);
                        Node? fc = Filter(c, c.Next);
                        EarcutLinked(fa, 0);
                        EarcutLinked(fc, 0);
                        return;
                    }

                    b = b.Next;
                }

                a = a.Next;
            }
            while (a != start);
        }

        public Node EliminateHoles(List<int> holeStarts, Node outerNode)
        {
            var queue = new List<Node>();
            for (int h = 0; h < holeStarts.Count; h++)
            {
                int start = holeStarts[h];
                int end = h < holeStarts.Count - 1 ? holeStarts[h + 1] : _x.Count;
                Node? list = LinkedList(start, end, false);
                if (list is null)
                {
                    continue;
                }

                if (list == list.Next)
                {
                    list.Steiner = true;
                }

                queue.Add(Leftmost(list));
            }

            queue.Sort((a, b) => a.X != b.X ? a.X.CompareTo(b.X) : a.Y.CompareTo(b.Y));
            foreach (Node hole in queue)
            {
                outerNode = EliminateHole(hole, outerNode);
            }

            return outerNode;
        }

        private Node EliminateHole(Node hole, Node outerNode)
        {
            Node? bridge = FindHoleBridge(hole, outerNode);
            if (bridge is null)
            {
                return outerNode;
            }

            Node bridgeReverse = Split(bridge, hole);
            Filter(bridgeReverse, bridgeReverse.Next);
            return Filter(bridge, bridge.Next) ?? outerNode;
        }

        private Node? FindHoleBridge(Node hole, Node outerNode)
        {
            Node p = outerNode;
            double hx = hole.X, hy = hole.Y, qx = double.NegativeInfinity;
            Node? m = null;
            do
            {
                if (hy <= p.Y && hy >= p.Next.Y && p.Next.Y != p.Y)
                {
                    double x = p.X + (hy - p.Y) * (p.Next.X - p.X) / (p.Next.Y - p.Y);
                    if (x <= hx && x > qx)
                    {
                        qx = x;
                        m = p.X < p.Next.X ? p : p.Next;
                        if (x == hx)
                        {
                            return m;
                        }
                    }
                }

                p = p.Next;
            }
            while (p != outerNode);

            if (m is null)
            {
                return null;
            }

            Node stop = m;
            double mx = m.X, my = m.Y, tanMin = double.PositiveInfinity;
            p = m;
            do
            {
                if (hx >= p.X && p.X >= mx && hx != p.X
                    && PointInTriangle(hy < my ? hx : qx, hy, mx, my, hy < my ? qx : hx, hy, p.X, p.Y))
                {
                    double tan = Math.Abs(hy - p.Y) / (hx - p.X);
                    if (LocallyInside(p, hole)
                        && (tan < tanMin || (tan == tanMin && (p.X > m.X || (p.X == m.X && SectorContainsSector(m, p))))))
                    {
                        m = p;
                        tanMin = tan;
                    }
                }

                p = p.Next;
            }
            while (p != stop);

            return m;
        }

        private static bool SectorContainsSector(Node m, Node p) => Area(m.Prev, m, p.Prev) < 0 && Area(p.Next, m, m.Next) < 0;

        private static Node Leftmost(Node start)
        {
            Node p = start, leftmost = start;
            do
            {
                if (p.X < leftmost.X || (p.X == leftmost.X && p.Y < leftmost.Y))
                {
                    leftmost = p;
                }

                p = p.Next;
            }
            while (p != start);

            return leftmost;
        }

        private static bool PointInTriangle(double ax, double ay, double bx, double by, double cx, double cy, double px, double py) =>
            (cx - px) * (ay - py) >= (ax - px) * (cy - py)
            && (ax - px) * (by - py) >= (bx - px) * (ay - py)
            && (bx - px) * (cy - py) >= (cx - px) * (by - py);

        private bool IsValidDiagonal(Node a, Node b) =>
            a.Next.I != b.I && a.Prev.I != b.I && !IntersectsPolygon(a, b)
            && ((LocallyInside(a, b) && LocallyInside(b, a) && MiddleInside(a, b) && (Area(a.Prev, a, b.Prev) != 0 || Area(a, b.Prev, b) != 0))
                || (Equals(a, b) && Area(a.Prev, a, a.Next) > 0 && Area(b.Prev, b, b.Next) > 0));

        private static int Sign(double v) => v > 0 ? 1 : v < 0 ? -1 : 0;

        private static bool OnSegment(Node p, Node q, Node r) =>
            q.X <= Math.Max(p.X, r.X) && q.X >= Math.Min(p.X, r.X) && q.Y <= Math.Max(p.Y, r.Y) && q.Y >= Math.Min(p.Y, r.Y);

        private static bool Intersects(Node p1, Node q1, Node p2, Node q2)
        {
            int o1 = Sign(Area(p1, q1, p2)), o2 = Sign(Area(p1, q1, q2)), o3 = Sign(Area(p2, q2, p1)), o4 = Sign(Area(p2, q2, q1));
            if (o1 != o2 && o3 != o4)
            {
                return true;
            }

            return (o1 == 0 && OnSegment(p1, p2, q1)) || (o2 == 0 && OnSegment(p1, q2, q1))
                || (o3 == 0 && OnSegment(p2, p1, q2)) || (o4 == 0 && OnSegment(p2, q1, q2));
        }

        private bool IntersectsPolygon(Node a, Node b)
        {
            Node p = a;
            do
            {
                if (--_budget < 0)
                {
                    return true;
                }

                if (p.I != a.I && p.Next.I != a.I && p.I != b.I && p.Next.I != b.I && Intersects(p, p.Next, a, b))
                {
                    return true;
                }

                p = p.Next;
            }
            while (p != a);

            return false;
        }

        private static bool LocallyInside(Node a, Node b) => Area(a.Prev, a, a.Next) < 0
            ? Area(a, b, a.Next) >= 0 && Area(a, a.Prev, b) >= 0
            : Area(a, b, a.Prev) < 0 || Area(a, a.Next, b) < 0;

        private static bool MiddleInside(Node a, Node b)
        {
            Node p = a;
            bool inside = false;
            double px = (a.X + b.X) / 2, py = (a.Y + b.Y) / 2;
            do
            {
                if ((p.Y > py) != (p.Next.Y > py) && p.Next.Y != p.Y && px < (p.Next.X - p.X) * (py - p.Y) / (p.Next.Y - p.Y) + p.X)
                {
                    inside = !inside;
                }

                p = p.Next;
            }
            while (p != a);

            return inside;
        }

        private static Node Split(Node a, Node b)
        {
            var a2 = new Node(a.I, a.X, a.Y);
            var b2 = new Node(b.I, b.X, b.Y);
            Node an = a.Next, bp = b.Prev;
            a.Next = b;
            b.Prev = a;
            a2.Next = an;
            an.Prev = a2;
            b2.Next = a2;
            a2.Prev = b2;
            bp.Next = b2;
            b2.Prev = bp;
            return b2;
        }
    }
}
