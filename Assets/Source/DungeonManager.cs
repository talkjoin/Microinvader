// attach to a GameObject with Grid.

using System.Collections.Generic;
using UnityEngine;

public class DungeonManager : MonoBehaviour
{
    [Header("Map Size")]
    public int MapWidth   = 80;
    public int MapHeight  = 60;
    public int MinRoomSize = 8;
    public int MaxDepth    = 5;

    [Header("References")]
    public DungeonRenderer Renderer;
    public GameObject      PlayerPrefab;
    public GameObject      ExitPortalPrefab;

    [Header("Enemy Spawning")]
    public EnemySpawnConfig[] EnemySpawnConfigs;

    [Header("Biome & Difficulty")]
    public BiomeType CurrentBiome = BiomeType.Lungs;
    public int       FloorDepth   = 1;

    // Read by other scripts to know where the player spawns
    public RectInt SpawnRoom { get; private set; }
    public bool[,] Tiles     { get; private set; }

    void Start()
    {
        GenerateDungeon(Random.Range(0, int.MaxValue));
    }

    public void GenerateDungeon(int seed)
    {
        // step1 : generate
        var gen = new BSPDungeonGenerator(
            MapWidth, MapHeight, MinRoomSize, MaxDepth,
            corridorWidth: 2, caIterations: 3, caThreshold: 4);
        Tiles = gen.Generate(seed);
        var rooms = new List<RectInt>(gen.Rooms);

        if (rooms.Count == 0) { Debug.LogError("No rooms generated!"); return; }

        // step2 : render
        if (Renderer != null) Renderer.Render(Tiles, CurrentBiome);

        // step 3: spawn player in first room
        SpawnRoom = rooms[0];
        SpawnPlayer(SpawnRoom, RoomCenter(SpawnRoom));

        /*if (PlayerPrefab != null)
        {
            Vector3 pos = RoomCenter(SpawnRoom);
            var player = Instantiate(PlayerPrefab, pos, Quaternion.identity);
            // Pass tile data to player if needed
        }
        */



        // step 4 : populate remaining rooms
        for (int i = 1; i < rooms.Count; i++)
            PopulateRoom(rooms[i], i);

        // step 5 : exit portal in last room
        if (ExitPortalPrefab != null)
            Instantiate(ExitPortalPrefab, RoomCenter(rooms[rooms.Count - 1]), Quaternion.identity);

        Debug.Log($"Dungeon ready {rooms.Count} rooms, floor {FloorDepth}, biome {CurrentBiome}");
    }

    void PopulateRoom(RectInt room, int index)
    {
        int count = Mathf.Clamp(FloorDepth, 1, 5) + (index % 3 == 0 ? 1 : 0);
        for (int i = 0; i < count; i++)
        {
            int rx = Random.Range(room.x + 1, room.xMax - 1);
            int ry = Random.Range(room.y + 1, room.yMax - 1);
            Vector3 pos = Renderer != null
                ? Renderer.TileToWorld(rx, ry)
                : new Vector3(rx, ry, 0);
            SpawnEnemy(pos);
        }
    }

    private void SpawnPlayer(RectInt room, Vector3 pos)
    {
        /* Vector3 pos = _renderer != null
            ? Renderer.TileToWorld(room.x + room.width / 2, room.y + room.height / 2)
            : new Vector3(room.x + room.width / 2, room.y + room.height / 2, 0);
        */

        if (PlayerPrefab != null)
        {
            var player = Instantiate(PlayerPrefab, pos, Quaternion.identity);

            // Auto-assign camera target
            var cam = Camera.main.GetComponent<CameraFollow>();
            if (cam != null)
            {
                cam.SetTarget(player.transform);
                cam.SetBounds(new Vector2(0, 0), new Vector2(MapWidth, MapHeight));
            }
        }
    }

    void SpawnEnemy(Vector3 pos)
    {
        if (EnemySpawnConfigs == null || EnemySpawnConfigs.Length == 0) return;
        EnemySpawnConfig cfg = PickEnemyConfig();
        if (cfg == null || cfg.Prefab == null) return;

        var go = Instantiate(cfg.Prefab, pos, Quaternion.identity);
        var ai = go.GetComponent<IEnemyAI>();
        ai?.Initialise(Tiles, MapWidth, MapHeight);
    }

    EnemySpawnConfig PickEnemyConfig()
    {
        float total = 0f;
        foreach (var c in EnemySpawnConfigs)
            if (FloorDepth >= c.MinFloorDepth) total += c.SpawnWeight;

        float roll = Random.Range(0f, total);
        float cum  = 0f;
        foreach (var c in EnemySpawnConfigs)
        {
            if (FloorDepth < c.MinFloorDepth) continue;
            cum += c.SpawnWeight;
            if (roll <= cum) return c;
        }
        return EnemySpawnConfigs[0];
    }

    Vector3 RoomCenter(RectInt r)
    {
        int cx = r.x + r.width  / 2;
        int cy = r.y + r.height / 2;
        return Renderer != null
            ? Renderer.TileToWorld(cx, cy)
            : new Vector3(cx, cy, 0);
    }
}
