// AntibodyProjectile.cs
// Projectile fired by AntibodyAI.

using UnityEngine;

public class AntibodyProjectile : MonoBehaviour
{
    public float    Speed    = 7f;
    public float    MaxRange = 8f;
    public LayerMask PlayerLayer;

    Vector3 _dir, _start;
    int     _damage;

    public void Launch(Vector3 dir, int damage)
    {
        _dir    = dir.normalized;
        _damage = damage;
        _start  = transform.position;
        float a = Mathf.Atan2(_dir.y, _dir.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.AngleAxis(a, Vector3.forward);
    }

    void Update()
    {
        transform.position += _dir * Speed * Time.deltaTime;
        if (Vector3.Distance(transform.position, _start) >= MaxRange)
            Destroy(gameObject);
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (((1 << other.gameObject.layer) & PlayerLayer) == 0) return;
        other.GetComponent<BacteriumController>()?.TakeDamage(_damage);
        Destroy(gameObject);
    }
}
