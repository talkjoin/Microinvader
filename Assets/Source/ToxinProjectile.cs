using UnityEngine;

public class ToxinProjectile : MonoBehaviour
{
    public float  Speed      = 15f;
    public float  MaxRange   = 15f;
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

        var myCollider = GetComponent<Collider2D>();
        if (myCollider != null)
        {
            var overlaps = Physics2D.OverlapCircleAll(transform.position, 0.15f, WallLayer);
            foreach (var wallCollider in overlaps)
                Physics2D.IgnoreCollision(myCollider, wallCollider);
        }
    }

    void Update()
    {
        if (!_launched) return;
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
