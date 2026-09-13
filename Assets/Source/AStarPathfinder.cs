
// Static A* pathfinder on a bool[,] tile grid.
// returns a List<Vector2Int> of waypoints from start to goal.

using System.Collections.Generic;
using UnityEngine;

public static class AStarPathfinder
{
    static readonly Vector2Int[] Dirs =
    {
        Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right,
        new Vector2Int( 1, 1), new Vector2Int(-1, 1),
        new Vector2Int( 1,-1), new Vector2Int(-1,-1)
    };

    public static List<Vector2Int> FindPath(
        bool[,] tiles, Vector2Int start, Vector2Int goal, int w, int h)
    {
        if (!Walkable(tiles, goal, w, h)) return new List<Vector2Int>();

        if (!Walkable(tiles, start, w, h))
        {
            var snapped = FindNearestWalkable(tiles, start, w, h, 3);
            if (snapped == null) return new List<Vector2Int>();
            start = snapped.Value;
        }

        var open  = new SortedList<float, Node>(new DupComparer());
        var nodes = new Dictionary<Vector2Int, Node>();

        var s = new Node(start, null, 0, H(start, goal));
        open.Add(s.F, s);
        nodes[start] = s;

        while (open.Count > 0)
        {
            var cur = open.Values[0];
            open.RemoveAt(0);

            if (cur.Pos == goal) return Rebuild(cur);

            foreach (var d in Dirs)
            {
                var nb  = cur.Pos + d;
                if (!Walkable(tiles, nb, w, h)) continue;
                float step = (d.x != 0 && d.y != 0) ? 1.414f : 1f;
                float g    = cur.G + step;

                if (nodes.TryGetValue(nb, out var ex))
                {
                    if (g >= ex.G) continue;
                    ex.G = g; ex.Parent = cur;
                }
                else
                {
                    var n = new Node(nb, cur, g, H(nb, goal));
                    nodes[nb] = n;
                    open.Add(n.F, n);
                }
            }
        }
        return new List<Vector2Int>();
    }

    static bool Walkable(bool[,] t, Vector2Int p, int w, int h)
        => p.x >= 0 && p.x < w && p.y >= 0 && p.y < h && t[p.x, p.y];


    static Vector2Int? FindNearestWalkable(bool[,] tiles, Vector2Int origin, int w, int h, int maxRadius)
    {
        for (int r = 1; r <= maxRadius; r++)
        {
            for (int dx = -r; dx <= r; dx++)
            for (int dy = -r; dy <= r; dy++)
            {
                if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != r) continue; // only this ring's edge
                var p = origin + new Vector2Int(dx, dy);
                if (Walkable(tiles, p, w, h)) return p;
            }
        }
        return null;
    }

    static float H(Vector2Int a, Vector2Int b)
    {
        int dx = Mathf.Abs(a.x - b.x), dy = Mathf.Abs(a.y - b.y);
        return dx + dy - 0.586f * Mathf.Min(dx, dy);
    }

    static List<Vector2Int> Rebuild(Node n)
    {
        var path = new List<Vector2Int>();
        while (n != null) { path.Add(n.Pos); n = n.Parent; }
        path.Reverse();
        return path;
    }

    class Node
    {
        public Vector2Int Pos; public Node Parent;
        public float G, H;
        public float F => G + H;
        public Node(Vector2Int p, Node par, float g, float h)
        { Pos = p; Parent = par; G = g; H = h; }
    }

    class DupComparer : IComparer<float>
    {
        public int Compare(float x, float y)
        { int r = x.CompareTo(y); return r == 0 ? 1 : r; }
    }
}
