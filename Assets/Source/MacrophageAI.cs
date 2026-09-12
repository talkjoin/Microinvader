using UnityEngine;

public class MacrophageAI : EnemyBaseAI
{
    [Header("Macrophage – Slam")]
    public float SlamRadius    = 1.5f;
    public float SlamKnockback = 4f;

    protected override void ExecuteAttack()
    {
        if (Player == null) return;
        if (Vector2.Distance(transform.position, Player.position) > SlamRadius) return;

        Anim.SetTrigger("Slam");
        Player.GetComponent<BacteriumController>()?.TakeDamage(AttackDamage);

        var prb = Player.GetComponent<Rigidbody2D>();
        if (prb != null)
        {
            Vector2 dir = (Player.position - transform.position).normalized;
            prb.AddForce(dir * SlamKnockback, ForceMode2D.Impulse);
        }
    }
}
