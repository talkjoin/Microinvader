using UnityEngine;

[System.Serializable]
public class EnemySpawnConfig
{
    public GameObject Prefab;
    [Tooltip("Relative probability weight")]
    public float SpawnWeight = 1f;
    [Tooltip("Minimum floor before this enemy appears")]
    public int MinFloorDepth = 1;

    [Tooltip("If checked, this enemy only spawns in the biomes listed below. " +
             "If unchecked, it can spawn in any biome (subject to MinFloorDepth).")]
    public bool RestrictToBiomes = false;
    public BiomeType[] AllowedBiomes;

    public bool AllowedInBiome(BiomeType biome)
    {
        if (!RestrictToBiomes || AllowedBiomes == null || AllowedBiomes.Length == 0) return true;
        foreach (var b in AllowedBiomes)
            if (b == biome) return true;
        return false;
    }
}
