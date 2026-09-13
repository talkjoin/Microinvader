
using UnityEngine;
using UnityEngine.Tilemaps;

[System.Serializable]
public class BiomeTileSet
{
    public BiomeType Biome;
    public TileBase  Floor;
    public TileBase  FloorVariant;
    public TileBase  WallTop;
    public TileBase  WallSide;
}

public class DungeonRenderer : MonoBehaviour
{
    [Header("Tilemaps")]
    public Tilemap FloorTilemap;
    public Tilemap WallTilemap;

    [Header("Biome Tile Sets")]
    public BiomeTileSet[] BiomeTileSets;

    public void Render(bool[,] tiles, BiomeType biome)
    {
        FloorTilemap.ClearAllTiles();
        WallTilemap.ClearAllTiles();

        BiomeTileSet ts = GetTileSet(biome);
        if (ts == null) { Debug.LogError("No tile set for biome: " + biome); return; }

        int w = tiles.GetLength(0);
        int h = tiles.GetLength(1);

        for (int x = 0; x < w; x++)
        for (int y = 0; y < h; y++)
        {
            var pos = new Vector3Int(x, y, 0);
            if (tiles[x, y])
            {
                TileBase ft = (Random.value < 0.15f && ts.FloorVariant != null)
                    ? ts.FloorVariant : ts.Floor;
                FloorTilemap.SetTile(pos, ft);
            }
            else
            {
                bool floorBelow = y > 0 && tiles[x, y - 1];
                WallTilemap.SetTile(pos, floorBelow ? ts.WallTop : ts.WallSide);
            }
        }
    }

    public Vector3 TileToWorld(int x, int y)
        => FloorTilemap.GetCellCenterWorld(new Vector3Int(x, y, 0));

    BiomeTileSet GetTileSet(BiomeType b)
    {
        foreach (var ts in BiomeTileSets)
            if (ts.Biome == b) return ts;
        return BiomeTileSets != null && BiomeTileSets.Length > 0 ? BiomeTileSets[0] : null;
    }
}
