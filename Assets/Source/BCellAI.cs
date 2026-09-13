using System.Collections;
using UnityEngine;

public class BCellAI : EnemyBaseAI
{
    [Header("Antibody – Burst")]
    public GameObject ProjectilePrefab;
    public int   BurstCount    = 3;
    public float BurstInterval = 0.15f;
    public float SpreadAngle   = 10f;

    protected override void ExecuteAttack()
    {
        StartCoroutine(FireBurst());
    }

    IEnumerator FireBurst()
    {
        for (int i = 0; i < BurstCount; i++)
        {
            Fire(i);
            yield return new WaitForSeconds(BurstInterval);
        }
    }

    void Fire(int idx)
    {
        if (ProjectilePrefab == null || Player == null) return;
        Vector3 dir = (Player.position - transform.position).normalized;
        float   off = (idx - (BurstCount - 1) / 2f) * SpreadAngle;
        dir = Quaternion.Euler(0, 0, off) * dir;

        var go = Instantiate(ProjectilePrefab, transform.position, Quaternion.identity);
        go.GetComponent<AntibodyProjectile>()?.Launch(dir, AttackDamage);
        Anim.SetTrigger("Shoot");
    }
}
