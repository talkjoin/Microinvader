// EnemySpawnConfig.cs
// Serialisable data class used by DungeonManager's inspector array.

using UnityEngine;

[System.Serializable]
public class EnemySpawnConfig
{
    public GameObject Prefab;
    [Tooltip("Relative probability weight")]
    public float SpawnWeight = 1f;
    [Tooltip("Minimum floor before this enemy appears")]
    public int MinFloorDepth = 1;
}
