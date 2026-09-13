// Player bacterium movement, dash, shooting, and health.


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
    [Tooltip("Starting health cap before any mutations. MaxHealth grows from this via health-multiplier mutations.")]
    public int   BaseMaxHealth = 5;
    public float IFrameTime = 0.6f;

    [Header("Hit Flash")]
    public SpriteRenderer SR;
    public Color HitColor = Color.red;
    public float FlashTime = 0.1f;

    Rigidbody2D _rb;
    Animator    _anim;
    Vector2     _moveInput;
    Vector2     _smoothVel;
    Color       _orig;

    int   _health;
    int   _maxHealth;
    bool  _dashing, _invincible;
    float _dashTimer, _shotTimer;

    // Multipliers set by MutationSystem
    float _speedMult  = 1f;
    float _damageMult = 1f;

    public event System.Action<int, int> OnHealthChanged;
    public event System.Action           OnDied;

    public bool IsAlive => _health > 0;

    // Exposed so the HUD can read the starting values right after spawn
    // (OnHealthChanged only fires on *change*, not on subscribe).
    public int CurrentHealth => _health;
    public int MaxHealth => _maxHealth;

    void Awake()
    {
        _rb        = GetComponent<Rigidbody2D>();
        _anim      = GetComponent<Animator>();
        _maxHealth = BaseMaxHealth;
        _health    = _maxHealth;
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

    // called automatically by a PlayerInput component.
    public void OnMove(InputValue v) => _moveInput = v.Get<Vector2>();

    public void OnDash(InputValue v)  { if (v.isPressed) TryDash(); }

    public void OnShootLeft(InputValue v)  { if (v.isPressed) TryShoot(-transform.right); }

    public void OnShootRight(InputValue v) { if (v.isPressed) TryShoot(transform.right); }

    public void OnShootUp(InputValue v) { if (v.isPressed) TryShoot(transform.up); }

    public void OnShootDown(InputValue v) { if (v.isPressed) TryShoot(-transform.up); }

    [Header("Gamepad Aim")]
    [Tooltip("Stick must be pushed at least this far from center for OnFire to register a direction.")]
    [Range(0.05f, 0.9f)] public float AimDeadzone = 0.25f;

    Vector2 _aimInput;
    public void OnAim(InputValue v) => _aimInput = v.Get<Vector2>();

    public void OnFire(InputValue v)
    {
        if (!v.isPressed) return;
        if (_aimInput.sqrMagnitude < AimDeadzone * AimDeadzone) return; // stick too close to center - no clear direction
        TryShoot(_aimInput.normalized);
    }


    // movement stuff
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

    // dashing 
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

    // shooting
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

    // Damage related stuff
    public void TakeDamage(int amount)
    {
        if (_invincible || !IsAlive) return;
        _health = Mathf.Max(0, _health - amount);
        OnHealthChanged?.Invoke(_health, _maxHealth);
        StartCoroutine(Flash());
        if (_health <= 0) { Die(); return; }
        StartCoroutine(IFrameRoutine());
    }

    public void Heal(int amount)
    {
        if (!IsAlive || amount <= 0) return;
        _health = Mathf.Min(_maxHealth, _health + amount);
        OnHealthChanged?.Invoke(_health, _maxHealth);
    }

    public void SetHealth(int value)
    {
        _health = Mathf.Clamp(value, 0, _maxHealth);
        OnHealthChanged?.Invoke(_health, _maxHealth);
    }
    public void FullHeal()
    {
        _health = _maxHealth;
        OnHealthChanged?.Invoke(_health, _maxHealth);
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

    public void ApplyMutations(float speedMult, float damageMult, float healthMult)
    {
        _speedMult  = speedMult;
        _damageMult = damageMult;

        _maxHealth = Mathf.Max(1, Mathf.RoundToInt(BaseMaxHealth * healthMult));
        _health    = Mathf.Min(_health, _maxHealth); 
    }
}
