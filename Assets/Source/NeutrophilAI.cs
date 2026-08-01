// NeutrophilAI.cs
// Fast rush attacker. Charges at the player for contact damage.
// Attach to Neutrophil prefab alongside EnemyHealth.

using System.Collections;
using UnityEngine;

public class NeutrophilAI : EnemyBaseAI
{
    [Header("Neutrophil – Rush")]
    public float RushSpeed    = 8f;
    public float RushDuration = 0.2f;

    bool _rushing;

    protected override void ExecuteAttack()
    {
        if (!_rushing) StartCoroutine(Rush());
    }

    IEnumerator Rush()
    {
        _rushing = true;
        Anim.SetTrigger("Rush");
        Vector2 dir = Player != null
            ? ((Vector2)(Player.position - transform.position)).normalized
            : (Vector2)transform.right;

        float t = 0f;
        while (t < RushDuration)
        {
            Rb.linearVelocity = dir * RushSpeed;
            t += Time.fixedDeltaTime;
            yield return new WaitForFixedUpdate();
        }
        Rb.linearVelocity = Vector2.zero;
        _rushing = false;
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (!_rushing) return;
        if (!other.CompareTag("Player")) return;
        other.GetComponent<BacteriumController>()?.TakeDamage(AttackDamage);
    }
}
