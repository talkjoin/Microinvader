// All npc need this one each

using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Animator))]
public class EnemyHealth : MonoBehaviour
{
    [Header("Health")]
    public int MaxHealth = 3;

    [Header("Hit Flash")]
    public SpriteRenderer SR;
    public Color          HitColor    = Color.red;
    public float          FlashTime   = 0.1f;

    [Header("Death")]
    public GameObject DeathVFX;
    public float      DeathDelay = 0.4f;

    [Header("Drop")]
    public GameObject DropPrefab;
    [Range(0f,1f)]
    public float DropChance = 0.2f;

    [Header("Reward")]
    [Tooltip("Mutation points granted to the player when this enemy dies.")]
    [Min(0)]
    public int MutationPointReward = 1;

    int  _hp;
    bool _dead;
    Color _orig;

    // Per-instance death event (kept for any local listeners).
    public event System.Action<int> OnDied;
    // Global death event - GameManager subscribes to this once, rather than
    // hooking every individually spawned enemy, to track mutation points.
    public static event System.Action<int> OnAnyEnemyDied;

    public bool IsAlive => !_dead;

    void Awake()
    {
        _hp = MaxHealth;
        if (SR == null) SR = GetComponent<SpriteRenderer>();
        if (SR != null) _orig = SR.color;
    }

    public void TakeDamage(int amount)
    {
        if (_dead || amount <= 0) return;
        _hp = Mathf.Max(0, _hp - amount);
        StartCoroutine(Flash());
        if (_hp <= 0) StartCoroutine(Die());
    }

    IEnumerator Flash()
    {
        if (SR == null) yield break;
        SR.color = HitColor;
        yield return new WaitForSeconds(FlashTime);
        SR.color = _orig;
    }

    IEnumerator Die()
    {
        _dead = true;
        GetComponent<EnemyBaseAI>().enabled = false;
        var rb = GetComponent<Rigidbody2D>();
        if (rb) rb.linearVelocity = Vector2.zero;
        if (DeathVFX) Instantiate(DeathVFX, transform.position, Quaternion.identity);
        GetComponent<Animator>()?.SetTrigger("Die");
        yield return new WaitForSeconds(DeathDelay);
        if (DropPrefab && Random.value <= DropChance)
            Instantiate(DropPrefab, transform.position, Quaternion.identity);
        OnDied?.Invoke(MutationPointReward);
        OnAnyEnemyDied?.Invoke(MutationPointReward);
        Destroy(gameObject);
    }
}
