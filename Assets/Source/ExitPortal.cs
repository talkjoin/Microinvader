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

                if (ActivatedVFXPrefab != null)
            Instantiate(ActivatedVFXPrefab, transform.position, Quaternion.identity);

        GameManager.Instance?.OnPortalEntered();
    }
}
