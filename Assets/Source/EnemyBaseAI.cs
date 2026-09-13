using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Animator))]
[RequireComponent(typeof(EnemyHealth))]
public abstract class EnemyBaseAI : MonoBehaviour, IEnemyAI
{
    protected enum AIState { Patrol, Chase, Attack }
    protected AIState State = AIState.Patrol;

    [Header("Detection")]
    public float DetectionRadius = 6f;
    public float LoseAggroRadius = 10f;

    [Header("Speed")]
    public float PatrolSpeed = 1.5f;
    public float ChaseSpeed  = 3.5f;

    [Header("Attack")]
    public float AttackRange    = 1.2f;
    public float AttackCooldown = 1.5f;
    public int   AttackDamage   = 1;

    [Header("Pathfinding")]
    public float PathRefreshRate = 0.4f;

    // Runtime
    protected Rigidbody2D Rb;
    protected Animator    Anim;
    protected Transform   Player;

    bool[,]           _tiles;
    int               _mapW, _mapH;
    List<Vector2Int>  _path = new List<Vector2Int>();
    int               _pathIdx;
    float             _pathTimer, _attackTimer;
    Vector2           _patrolTarget;
    float             _patrolWait;

    // IEnemyAI
    public void Initialise(bool[,] tiles, int mapWidth, int mapHeight)
    {
        _tiles = tiles; _mapW = mapWidth; _mapH = mapHeight;
    }

    public void SetPlayer(Transform player)
    {
        if (player != null) Player = player;
    }

   
    protected virtual void Awake()
    {
        Rb   = GetComponent<Rigidbody2D>();
        Anim = GetComponent<Animator>();
        var pg = GameObject.FindGameObjectWithTag("Player");
        if (pg) Player = pg.transform;
        NewPatrolTarget();
    }

    void Update()
    {
        _pathTimer   -= Time.deltaTime;
        _attackTimer -= Time.deltaTime;
        UpdateFSM();
    }

    void FixedUpdate() { Move(); }

    // FSM
    void UpdateFSM()
    {
        if (Player == null) return;
        float dist = Vector2.Distance(transform.position, Player.position);

        switch (State)
        {
            case AIState.Patrol:
                if (dist <= DetectionRadius) GoTo(AIState.Chase);
                else UpdatePatrolWander();
                break;

            case AIState.Chase:
                if (dist > LoseAggroRadius) GoTo(AIState.Patrol);
                else if (dist <= AttackRange) GoTo(AIState.Attack);
                else if (_pathTimer <= 0f) { RefreshPath(); _pathTimer = PathRefreshRate; }
                break;

            case AIState.Attack:
                if (dist > AttackRange * 1.2f) GoTo(AIState.Chase);
                else if (_attackTimer <= 0f)
                { ExecuteAttack(); _attackTimer = AttackCooldown; }
                break;
        }
    }

    void GoTo(AIState s)
    {
        State = s;
        switch (s)
        {
            case AIState.Patrol: Anim.SetBool("IsChasing", false); NewPatrolTarget(); break;
            case AIState.Chase:  Anim.SetBool("IsChasing", true);  RefreshPath();     break;
            case AIState.Attack: Rb.linearVelocity = Vector2.zero; Anim.SetTrigger("Attack"); break;
        }
    }

    // movement
    void Move()
    {
        switch (State)
        {
            case AIState.Patrol: MoveToward(_patrolTarget, PatrolSpeed); break;
            case AIState.Chase:  FollowPath();                           break;
            case AIState.Attack: FacePlayer();                           break;
        }
    }

    void MoveToward(Vector2 target, float speed)
    {
        Vector2 dir = (target - (Vector2)transform.position).normalized;
        Rb.linearVelocity = dir * speed;
        Anim.SetFloat("Speed", Rb.linearVelocity.magnitude);
        Flip(dir);
        if (Vector2.Distance(transform.position, target) < 0.3f)
        {
            _patrolWait -= Time.deltaTime;
            if (_patrolWait <= 0f) NewPatrolTarget();
        }
    }

    void FollowPath()
    {
        if (_path == null || _pathIdx >= _path.Count)
        { Rb.linearVelocity = Vector2.zero; return; }

        Vector2 wp  = TileToWorld(_path[_pathIdx]);
        Vector2 dir = (wp - (Vector2)transform.position).normalized;
        Rb.linearVelocity = dir * ChaseSpeed;
        Anim.SetFloat("Speed", Rb.linearVelocity.magnitude);
        Flip(dir);
        if (Vector2.Distance(transform.position, wp) < 0.25f) _pathIdx++;
    }

    void FacePlayer()
    {
        if (Player) Flip((Player.position - transform.position).normalized);
    }

    void Flip(Vector2 dir)
    {
        if (Mathf.Abs(dir.x) > 0.01f)
            transform.localScale = new Vector3(Mathf.Sign(dir.x), 1f, 1f);
    }

    // patrol
    void UpdatePatrolWander() { /* wander handled in MoveToward timeout */ }

    void NewPatrolTarget()
    {
        for (int i = 0; i < 20; i++)
        {
            float  a  = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            float  d  = Random.Range(2f, DetectionRadius * 0.5f);
            Vector2 c = (Vector2)transform.position + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * d;
            var tp    = WorldToTile(c);
            if (_tiles != null &&
                tp.x >= 0 && tp.x < _mapW && tp.y >= 0 && tp.y < _mapH &&
                _tiles[tp.x, tp.y])
            { _patrolTarget = c; _patrolWait = Random.Range(0.5f, 2f); return; }
        }
        _patrolTarget = transform.position;
        _patrolWait   = 1f;
    }

    // A*
    void RefreshPath()
    {
        if (_tiles == null || Player == null) return;
        _path    = AStarPathfinder.FindPath(_tiles,
                       WorldToTile(transform.position),
                       WorldToTile(Player.position), _mapW, _mapH);
        _pathIdx = 0;
    }

    // Abstract attack
    protected abstract void ExecuteAttack();

    // Coord helpers (tile size = 1 Unity unit)
    Vector2Int WorldToTile(Vector2 w) =>
        new Vector2Int(Mathf.RoundToInt(w.x), Mathf.RoundToInt(w.y));

    Vector2 TileToWorld(Vector2Int t) => new Vector2(t.x, t.y);

    // Gizmos
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;  Gizmos.DrawWireSphere(transform.position, DetectionRadius);
        Gizmos.color = Color.red;     Gizmos.DrawWireSphere(transform.position, LoseAggroRadius);
        Gizmos.color = Color.magenta; Gizmos.DrawWireSphere(transform.position, AttackRange);
        if (_path == null) return;
        Gizmos.color = Color.cyan;
        for (int i = _pathIdx; i < _path.Count - 1; i++)
            Gizmos.DrawLine(TileToWorld(_path[i]), TileToWorld(_path[i+1]));
    }
}
