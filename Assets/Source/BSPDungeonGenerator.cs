using System.Collections.Generic;
using UnityEngine;

public class BSPDungeonGenerator
{
    public int MapWidth  { get; private set; }
    public int MapHeight { get; private set; }

    private int _minRoomSize;
    private int _maxDepth;
    private int _corridorWidth;
    private int _caIterations;
    private int _caThreshold;

    private bool[,]      _tiles;
    private List<RectInt> _rooms;

    public IReadOnlyList<RectInt> Rooms => _rooms;
    public bool[,] Tiles => _tiles;


    public BSPDungeonGenerator(
        int mapWidth      = 80,
        int mapHeight     = 60,
        int minRoomSize   = 8,
        int maxDepth      = 5,
        int corridorWidth = 2,
        int caIterations  = 3,
        int caThreshold   = 4)
    {
        MapWidth       = mapWidth;
        MapHeight      = mapHeight;
        _minRoomSize   = minRoomSize;
        _maxDepth      = maxDepth;
        _corridorWidth = corridorWidth;
        _caIterations  = caIterations;
        _caThreshold   = caThreshold;
    }

    public bool[,] Generate(int seed)
    {
        Random.InitState(seed);
        _tiles = new bool[MapWidth, MapHeight];
        _rooms = new List<RectInt>();

        var root = new BSPNode(new RectInt(0, 0, MapWidth, MapHeight));
        SplitNode(root, 0);
        PlaceRooms(root);
        ConnectNode(root);
        for (int i = 0; i < _caIterations; i++) SmoothWalls();

        return _tiles;
    }

    // BSP
    void SplitNode(BSPNode node, int depth)
    {
        if (depth >= _maxDepth) return;

        bool horizontal = ShouldSplitHorizontal(node.Bounds);
        int splitPos;

        if (horizontal)
        {
            int lo = node.Bounds.y + _minRoomSize;
            int hi = node.Bounds.yMax - _minRoomSize;
            if (lo >= hi) return;
            splitPos = Random.Range(lo, hi);

            node.Left  = new BSPNode(new RectInt(node.Bounds.x, node.Bounds.y,
                                                  node.Bounds.width, splitPos - node.Bounds.y));
            node.Right = new BSPNode(new RectInt(node.Bounds.x, splitPos,
                                                  node.Bounds.width, node.Bounds.yMax - splitPos));
        }
        else
        {
            int lo = node.Bounds.x + _minRoomSize;
            int hi = node.Bounds.xMax - _minRoomSize;
            if (lo >= hi) return;
            splitPos = Random.Range(lo, hi);

            node.Left  = new BSPNode(new RectInt(node.Bounds.x, node.Bounds.y,
                                                  splitPos - node.Bounds.x, node.Bounds.height));
            node.Right = new BSPNode(new RectInt(splitPos, node.Bounds.y,
                                                  node.Bounds.xMax - splitPos, node.Bounds.height));
        }

        SplitNode(node.Left,  depth + 1);
        SplitNode(node.Right, depth + 1);
    }

    bool ShouldSplitHorizontal(RectInt b)
    {
        float r = (float)b.width / b.height;
        if (r > 1.25f) return false;
        if (r < 0.75f) return true;
        return Random.value > 0.5f;
    }

    // Room placement
    void PlaceRooms(BSPNode node)
    {
        if (node == null) return;

        if (node.IsLeaf)
        {
            int m  = 1;
            int x  = node.Bounds.x + m;
            int y  = node.Bounds.y + m;
            int w  = node.Bounds.width  - m * 2;
            int h  = node.Bounds.height - m * 2;
            int sw = Random.Range(0, Mathf.Max(0, w - _minRoomSize + 1));
            int sh = Random.Range(0, Mathf.Max(0, h - _minRoomSize + 1));
            x += sw / 2; y += sh / 2; w -= sw; h -= sh;

            if (w < 3 || h < 3) return;

            var room = new RectInt(x, y, w, h);
            node.Room = room;
            _rooms.Add(room);
            for (int tx = room.x; tx < room.xMax; tx++)
                for (int ty = room.y; ty < room.yMax; ty++)
                    SetTile(tx, ty, true);
        }
        else
        {
            PlaceRooms(node.Left);
            PlaceRooms(node.Right);
        }
    }

    // Corridors
    void ConnectNode(BSPNode node)
    {
        if (node == null || node.IsLeaf) return;
        ConnectNode(node.Left);
        ConnectNode(node.Right);
        CarveCorridorLShaped(GetRoomCenter(node.Left), GetRoomCenter(node.Right));
    }

    Vector2Int GetRoomCenter(BSPNode node)
    {
        if (node == null) return Vector2Int.zero;
        if (node.Room.HasValue)
        {
            var r = node.Room.Value;
            return new Vector2Int(r.x + r.width / 2, r.y + r.height / 2);
        }
        var lc = GetRoomCenter(node.Left);
        var rc = GetRoomCenter(node.Right);
        return new Vector2Int((lc.x + rc.x) / 2, (lc.y + rc.y) / 2);
    }

    void CarveCorridorLShaped(Vector2Int a, Vector2Int b)
    {
        if (Random.value > 0.5f) { CarveH(a.x, b.x, a.y); CarveV(a.y, b.y, b.x); }
        else                     { CarveV(a.y, b.y, a.x); CarveH(a.x, b.x, b.y); }
    }

    void CarveH(int x1, int x2, int y)
    {
        int xMin = Mathf.Min(x1, x2), xMax = Mathf.Max(x1, x2);
        for (int x = xMin; x <= xMax; x++)
            for (int w = 0; w < _corridorWidth; w++) SetTile(x, y + w, true);
    }

    void CarveV(int y1, int y2, int x)
    {
        int yMin = Mathf.Min(y1, y2), yMax = Mathf.Max(y1, y2);
        for (int y = yMin; y <= yMax; y++)
            for (int w = 0; w < _corridorWidth; w++) SetTile(x + w, y, true);
    }

    // Cellular automata
    void SmoothWalls()
    {
        var next = new bool[MapWidth, MapHeight];
        for (int x = 0; x < MapWidth; x++)
        for (int y = 0; y < MapHeight; y++)
        {
            if (x == 0 || x == MapWidth-1 || y == 0 || y == MapHeight-1) { next[x,y] = false; continue; }
            int n = CountFloorNeighbours(x, y);
            next[x,y] = _tiles[x,y] ? n >= _caThreshold : n > _caThreshold;
        }
        _tiles = next;
    }



    int CountFloorNeighbours(int x, int y)
    {
        int c = 0;
        for (int nx = x-1; nx <= x+1; nx++)
        for (int ny = y-1; ny <= y+1; ny++)
        {
            if (nx == x && ny == y) continue;
            if (nx < 0 || nx >= MapWidth || ny < 0 || ny >= MapHeight) continue;
            if (_tiles[nx,ny]) c++;
        }
        return c;
    }

    void SetTile(int x, int y, bool v)
    {
        if (x >= 0 && x < MapWidth && y >= 0 && y < MapHeight) _tiles[x,y] = v;
    }

    // Internal BSP node
    class BSPNode
    {
        public RectInt  Bounds;
        public BSPNode  Left, Right;
        public RectInt? Room;
        public bool IsLeaf => Left == null && Right == null;
        public BSPNode(RectInt b) { Bounds = b; }
    }
}
