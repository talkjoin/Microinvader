using UnityEngine;
using UnityEngine.SceneManagement;

public class ExitPortal : MonoBehaviour
{
    [Header("Next Floor")]
    public BiomeType NextBiome = BiomeType.Bloodstream;
    public int NextFloorDepth = 2;

    [Header("Visual Feedback")]
    public GameObject ActivatedVFXPrefab;

    private bool _activated = false;

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (_activated) return;
        if (!other.CompareTag("Player")) return;

        _activated = true;

        // Spawn a little burst effect
        if (ActivatedVFXPrefab != null)
            Instantiate(ActivatedVFXPrefab, transform.position, Quaternion.identity);

        // Tell DungeonManager to generate the next floor
        var dm = FindFirstObjectByType<DungeonManager>();
        if (dm != null)
        {
            dm.FloorDepth = NextFloorDepth;
            dm.CurrentBiome = NextBiome;
            dm.GenerateDungeon(Random.Range(0, int.MaxValue));
        }
    }
}