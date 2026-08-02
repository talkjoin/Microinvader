// BacteriumController.cs
// Player bacterium movement, dash, shooting, and health.
// Requires: Rigidbody2D, Animator
// Uses Unity's new Input System. If you use the old Input System,
// replace OnMove/OnDash/OnFire with Input.GetAxis calls in Update().

using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Animator))]
public class BacteriumController : MonoBehaviour
{
    [Header("Movement")]
    public float MoveSpeed        = 5f;
    public float AccelerationTime = 0.08f;

    [Header("Dash")]
    public float DashSpeed    = 18f;
    public float DashDuration = 0.12f;
    public float DashCooldown = 0.8f;

    [Header("Shooting")]
    public GameObject ToxinPrefab;
    public Transform  ProjectileOrigin;
    public float      ShotCooldown = 0.35f;

    [Header("Health")]
    public int   MaxHealth  = 5;
    public int   MaxShield  = 3;
    public float IFrameTime = 0.6f;

    [Header("Hit Flash")]
    public SpriteRenderer SR;
    public Color HitColor = Color.red;
    public float FlashTime = 0.1f;

    // ── Runtime ───────────────────────────────────────────────────────────
    Rigidbody2D _rb;
    Animator    _anim;
    Vector2     _moveInput;
    Vector2     _smoothVel;
    Color       _orig;

    int   _health, _shield;
    bool  _dashing, _invincible;
    float _dashTimer, _shotTimer;

    // Multipliers set by MutationSystem
    float _speedMult  = 1f;
    float _damageMult = 1f;

    public event System.Action<int, int> OnHealthChanged;
    public event System.Action<int, int> OnShieldChanged;
    public event System.Action           OnDied;

    public bool IsAlive => _health > 0;

    void Awake()
    {
        _rb     = GetComponent<Rigidbody2D>();
        _anim   = GetComponent<Animator>();
        _health = MaxHealth;
        _shield = MaxShield;
        if (SR == null) SR = GetComponent<SpriteRenderer>();
        if (SR != null) _orig = SR.color;
    }

    void Update()
    {
        _dashTimer -= Time.deltaTime;
        _shotTimer -= Time.deltaTime;
    }

    void FixedUpdate()
    {
        if (!_dashing) Move();
    }

    // ── Input System callbacks ────────────────────────────────────────────
    // These are called automatically by a PlayerInput component.
    public void OnMove(InputValue v) => _moveInput = v.Get<Vector2>();

    public void OnDash(InputValue v)  { if (v.isPressed) TryDash(); }

    public void OnShootLeft(InputValue v)  { if (v.isPressed) TryShoot(-transform.right); }

    public void OnShootRight(InputValue v) { if (v.isPressed) TryShoot(transform.right); }

    public void OnShootUp(InputValue v) { if (v.isPressed) TryShoot(transform.up); }

    public void OnShootDown(InputValue v) { if (v.isPressed) TryShoot(-transform.up); }


    // ── Movement ──────────────────────────────────────────────────────────
    void Move()
    {
        Vector2 target = _moveInput * MoveSpeed * _speedMult;
        _rb.linearVelocity = Vector2.SmoothDamp(_rb.linearVelocity, target,
                                           ref _smoothVel, AccelerationTime);
        //_anim.SetFloat("Speed", _rb.linearVelocity.magnitude);

        if (_moveInput.sqrMagnitude > 0.01f)
        {
            float angle = Mathf.Atan2(_moveInput.y, _moveInput.x) * Mathf.Rad2Deg;
            //transform.rotation = Quaternion.AngleAxis(angle, Vector3.forward);
        }
    }

    // ── Dash ──────────────────────────────────────────────────────────────
    void TryDash()
    {
        if (_dashing || _dashTimer > 0f || _moveInput == Vector2.zero) return;
        StartCoroutine(DashRoutine());
    }

    IEnumerator DashRoutine()
    {
        _dashing = _invincible = true;
        _anim.SetBool("IsDashing", true);
        _rb.linearVelocity = _moveInput.normalized * DashSpeed;
        yield return new WaitForSeconds(DashDuration);
        _dashing   = false;
        _dashTimer = DashCooldown;
        _anim.SetBool("IsDashing", false);
        yield return new WaitForSeconds(0.1f);
        _invincible = false;
    }

    // ── Shooting ──────────────────────────────────────────────────────────
    void TryShoot(Vector3 dir)
    {

        if (_shotTimer > 0f || ToxinPrefab == null) return;
        Vector3 origin = ProjectileOrigin != null ? ProjectileOrigin.position : transform.position;
        //Vector3 dir = AimDirection();
        var p = Instantiate(ToxinPrefab, origin, Quaternion.identity);
        p.GetComponent<ToxinProjectile>()?.Launch(dir, _damageMult);
        _shotTimer = ShotCooldown;        
    }

    Vector3 AimDirection()
    {
        if (Mouse.current != null)
        {
            Vector3 mw = Camera.main.ScreenToWorldPoint(Mouse.current.position.ReadValue());
            mw.z = 0f;
            return (mw - transform.position).normalized;
        }
        return transform.right;
    }

    // ── Damage ────────────────────────────────────────────────────────────
    public void TakeDamage(int amount)
    {
        if (_invincible || !IsAlive) return;
        if (_shield > 0)
        {
            _shield = Mathf.Max(0, _shield - amount);
            OnShieldChanged?.Invoke(_shield, MaxShield);
        }
        else
        {
            _health = Mathf.Max(0, _health - amount);
            OnHealthChanged?.Invoke(_health, MaxHealth);
            StartCoroutine(Flash());
        }
        if (_health <= 0) { Die(); return; }
        StartCoroutine(IFrameRoutine());
    }

    IEnumerator IFrameRoutine()
    {
        _invincible = true;
        yield return new WaitForSeconds(IFrameTime);
        _invincible = false;
    }

    IEnumerator Flash()
    {
        if (SR == null) yield break;
        SR.color = HitColor;
        yield return new WaitForSeconds(FlashTime);
        SR.color = _orig;
    }

    void Die()
    {
        _anim.SetTrigger("Die");
        _rb.linearVelocity = Vector2.zero;
        enabled = false;
        OnDied?.Invoke();
    }

    // Called by MutationSystem at run start
    public void ApplyMutations(float speedMult, float damageMult, int bonusShield)
    {
        _speedMult  = speedMult;
        _damageMult = damageMult;
        MaxShield  += bonusShield;
        _shield     = MaxShield;
    }
}
