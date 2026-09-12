//script fort the protal

using UnityEngine;

public class ExitPortal : MonoBehaviour
{
    [Header("Visual Feedback")]
    public GameObject ActivatedVFXPrefab;

    private bool _activated = false;

    private void OnTriggerEnter2D(Collider2D other)
    {
        Debug.Log($"Portal triggered by: {other.name}, tag: {other.tag}");

        if (_activated) return;
        if (!other.CompareTag("Player")) return;

        _activated = true;

        // spawn a little burst effect
        if (ActivatedVFXPrefab != null)
            Instantiate(ActivatedVFXPrefab, transform.position, Quaternion.identity);

        // Hand off to GameManager: it owns the biome order/progression now and
        // will show either the portal shop screen or the victory screen
        // depending on whether this was the last biome.
        GameManager.Instance?.OnPortalEntered();
    }
}
