// ToxinProjectile.cs
// Fired by BacteriumController. Travels straight, deals damage on enemy hit.

using UnityEngine;

public class ToxinProjectile : MonoBehaviour
{
    public float  Speed      = 12f;
    public float  MaxRange   = 10f;
    public int    BaseDamage = 1;
    public LayerMask EnemyLayer;
    public LayerMask WallLayer;

    Vector3 _start, _dir;
    float   _damageMult = 1f;
    bool    _launched;

    public void Launch(Vector3 direction, float damageMult)
    {
        _dir        = direction.normalized;
        _damageMult = damageMult;
        _start      = transform.position;
        _launched   = true;
        //float angle = Mathf.Atan2(_dir.y, _dir.x) * Mathf.Rad2Deg;
        //transform.rotation = Quaternion.AngleAxis(angle, Vector3.forward);
    }

    void Update()
    {
        if (!_launched) return;
        Debug.Log("Launched!");
        transform.position += _dir * Speed * Time.deltaTime;
        if (Vector3.Distance(transform.position, _start) >= MaxRange)
            Destroy(gameObject);
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (((1 << other.gameObject.layer) & WallLayer) != 0)
        {
            Destroy(gameObject);
            return;
        }
        if (((1 << other.gameObject.layer) & EnemyLayer) != 0)
        {
            other.GetComponent<EnemyHealth>()
                 ?.TakeDamage(Mathf.RoundToInt(BaseDamage * _damageMult));
            Destroy(gameObject);
        }
    }
}
