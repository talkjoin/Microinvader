// GameManager.cs
// Central orchestrator for the whole game: UI screen flow, biome progression,
// mutation points, and the player's health/shield HUD.
//
// Attach to a persistent GameObject in the boot scene. Wire up all the
// [Header] fields in the Inspector (panels, buttons, sliders, text fields).
//
// Flow:
//   StartScreen --(Start New Game / Start Assist Mode)--> Hint --> Playing
//   Playing --(player dies)--> DeathScreen --(Continue)--> FreeMutationPick --> Hint --> Playing (biome 1)
//   Playing --(portal, not last biome)--> ShopScreen --(Next Stage)--> Hint --> Playing (next biome)
//   Playing --(portal, last biome)--> VictoryScreen
//
// Assumes TextMeshPro (TMP_Text). Swap for UnityEngine.UI.Text if your
// project doesn't use TMP.

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    // ── Biome progression ───────────────────────────────────────────────
    [Header("Biome Order")]
    [Tooltip("The order biomes are played in. The player always starts at index 0.")]
    public BiomeType[] BiomeOrder =
    {
        BiomeType.Lungs, BiomeType.Bloodstream, BiomeType.LymphNodes, BiomeType.BrainCortex
    };

    int  _biomeIndex;
    bool _assistMode;
    int  _mutationPoints;

    // ── References ──────────────────────────────────────────────────────
    [Header("References")]
    public DungeonManager Dungeon;

    BacteriumController _player;

    // ── HUD (shown while playing) ───────────────────────────────────────
    [Header("HUD")]
    public GameObject HUDPanel;
    public Slider     HealthBarSlider;
    public Slider     ShieldBarSlider;
    public TMP_Text   MutationPointsText;
    public Image      ScreenBackgroundImage;

    // ── Start screen ─────────────────────────────────────────────────────
    [Header("Start Screen")]
    public GameObject StartPanel;
    public Button     StartNewGameButton;
    public Button     StartAssistModeButton;

    // ── Death / continue screen ─────────────────────────────────────────
    [Header("Death Screen")]
    public GameObject DeathPanel;
    public Button     ContinueButton;

    [Header("Free Mutation Pick Screen (shown after Continue)")]
    public GameObject FreeMutationPanel;
    public Transform  FreeMutationCardContainer;

    // ── Portal shop screen ───────────────────────────────────────────────
    [Header("Portal Shop Screen")]
    public GameObject ShopPanel;
    public Transform  ShopCardContainer;
    public TMP_Text   ShopMutationPointsText;
    public Button     NextStageButton;

    // ── Victory screen ──────────────────────────────────────────────────
    [Header("Victory Screen")]
    public GameObject VictoryPanel;
    public Button     PlayAgainButton; // optional

    // ── Hint / education screen ─────────────────────────────────────────
    [Header("Hint Screen")]
    public GameObject   HintPanel;
    public Image        HintImage;
    public TMP_Text     HintTitleText;
    public TMP_Text     HintBodyText;
    public Button       HintSkipButton;
    public ImmuneFact[] ImmuneFacts;

    [Header("Mutation Card Prefab (used by free-pick and shop screens)")]
    public MutationCardUI MutationCardPrefab;

    [Header("Assist Mode")]
    [Tooltip("Seconds between each small heal tick while in Assist Mode.")]
    public float AssistRegenInterval = 2f;
    [Tooltip("Health restored per tick while in Assist Mode.")]
    public int   AssistRegenAmount   = 1;

    Coroutine _assistRoutine;
    Action    _afterHint;

    // ── Lifecycle ────────────────────────────────────────────────────────
    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        StartNewGameButton?.onClick.AddListener(() => OnStartClicked(false));
        StartAssistModeButton?.onClick.AddListener(() => OnStartClicked(true));
        ContinueButton?.onClick.AddListener(OnContinueClicked);
        NextStageButton?.onClick.AddListener(OnNextStageClicked);
        HintSkipButton?.onClick.AddListener(OnHintSkipClicked);
        PlayAgainButton?.onClick.AddListener(StartFreshRun);

        EnemyHealth.OnAnyEnemyDied += AddMutationPoints;
    }

    void OnDestroy()
    {
        EnemyHealth.OnAnyEnemyDied -= AddMutationPoints;
    }

    void Start()
    {
        _mutationPoints = 0;
        UpdateMutationPointsUI();
        ShowOnly(StartPanel);
    }

    // ── Start screen ─────────────────────────────────────────────────────
    void OnStartClicked(bool assistMode)
    {
        _assistMode = assistMode;
        StartFreshRun();
    }

    // A completely clean playthrough: no mutations carried over from any
    // previous run, and none left over from a previous play session either.
    // Used by "Start New Game" / "Start in Assist Mode" and by the victory
    // screen's Play Again button.
    void StartFreshRun()
    {
        MutationSystem.Instance?.ResetAll();
        BeginNewRun();
    }

    // Resets biome progress and run-scoped currency and moves to the hint
    // screen. Deliberately does NOT touch mutations - callers decide what
    // mutation state should look like (nothing, or just the free pick)
    // before calling this, so mutations still stack normally biome-to-biome
    // within a run without leaking into the *next* run.
    void BeginNewRun()
    {
        _biomeIndex     = 0;
        _mutationPoints = 0;
        UpdateMutationPointsUI();
        ShowHintThen(() => StartBiome(_biomeIndex));
    }

    // ── Biome / floor flow ───────────────────────────────────────────────
    void StartBiome(int index)
    {
        ShowOnly(HUDPanel);

        index = Mathf.Clamp(index, 0, BiomeOrder.Length - 1);
        Dungeon.CurrentBiome = BiomeOrder[index];
        Dungeon.FloorDepth   = index + 1;
        Dungeon.GenerateDungeon(UnityEngine.Random.Range(0, int.MaxValue));

        BindPlayer(Dungeon.Player);

        if (_assistMode) StartAssistRegen();
        else StopAssistRegen();
    }

    void BindPlayer(BacteriumController player)
    {
        // Unhook from whatever player we were tracking before (previous floor's instance).
        if (_player != null)
        {
            _player.OnHealthChanged -= UpdateHealthBar;
            _player.OnShieldChanged -= UpdateShieldBar;
            _player.OnDied          -= OnPlayerDied;
        }

        _player = player;
        if (_player == null)
        {
            Debug.LogWarning("GameManager: no player was spawned by DungeonManager.");
            return;
        }

        _player.OnHealthChanged += UpdateHealthBar;
        _player.OnShieldChanged += UpdateShieldBar;
        _player.OnDied          += OnPlayerDied;

        // Prime the HUD immediately (events only fire on change, not on subscribe).
        UpdateHealthBar(_player.CurrentHealth, _player.MaxHealth);
        UpdateShieldBar(_player.CurrentShield, _player.MaxShield);

        // Apply every mutation unlocked so far to this fresh player instance.
        // NOTE: call this exactly once per spawn - it's safe here because each
        // floor spawns a brand-new BacteriumController (see DungeonManager.SpawnPlayer),
        // so MaxShield always starts from its prefab default before mutations stack.
        if (MutationSystem.Instance != null) MutationSystem.Instance.ApplyToPlayer(_player);
    }

    // ── Assist mode ──────────────────────────────────────────────────────
    void StartAssistRegen()
    {
        StopAssistRegen();
        _assistRoutine = StartCoroutine(AssistRegenRoutine());
    }

    void StopAssistRegen()
    {
        if (_assistRoutine != null) { StopCoroutine(_assistRoutine); _assistRoutine = null; }
    }

    IEnumerator AssistRegenRoutine()
    {
        var wait = new WaitForSeconds(AssistRegenInterval);
        while (true)
        {
            yield return wait;
            if (_player != null && _player.IsAlive) _player.Heal(AssistRegenAmount);
        }
    }

    // ── HUD updates ──────────────────────────────────────────────────────
    void UpdateHealthBar(int current, int max)
    {
        if (HealthBarSlider == null) return;
        HealthBarSlider.maxValue = max;
        HealthBarSlider.value    = current;
    }

    void UpdateShieldBar(int current, int max)
    {
        if (ShieldBarSlider == null) return;
        ShieldBarSlider.maxValue = max;
        ShieldBarSlider.value    = current;
    }

    void AddMutationPoints(int amount)
    {
        _mutationPoints += amount;
        UpdateMutationPointsUI();
    }

    void UpdateMutationPointsUI()
    {
        if (MutationPointsText     != null) MutationPointsText.text     = $"Mutation Points: {_mutationPoints}";
        if (ShopMutationPointsText != null) ShopMutationPointsText.text = $"Mutation Points: {_mutationPoints}";
    }

    // ── Death / continue flow ───────────────────────────────────────────
    void OnPlayerDied()
    {
        StopAssistRegen();
        ShowOnly(DeathPanel);
    }

    void OnContinueClicked()
    {
        if (MutationSystem.Instance == null)
        {
            Debug.LogWarning("GameManager: MutationSystem.Instance is missing from the scene.");
            StartFreshRun();
            return;
        }

        ShowOnly(FreeMutationPanel);
        PopulateMutationCards(
            FreeMutationCardContainer,
            MutationSystem.Instance.GetRandomFreeChoices(3),
            free: true,
            onPicked: OnFreeMutationPicked);
    }

    void OnFreeMutationPicked(MutationDefinition chosen)
    {
        // Wipe everything accumulated during the run that just ended (and any
        // leftover save) before applying the pick - the free mutation should
        // be the ONLY thing carried into the restarted run, not stacked on
        // top of whatever had built up before dying.
        MutationSystem.Instance.ResetAll();
        MutationSystem.Instance.SelectMutation(chosen);
        BeginNewRun();
    }

    // ── Portal / shop flow ───────────────────────────────────────────────
    [Tooltip("How many random mutation cards the portal shop offers per visit.")]
    public int ShopOfferCount = 3;

    // The specific 3 cards rolled for the *current* shop visit. Buying one
    // removes just that card - it does not re-roll the remaining offers.
    // A fresh set is rolled the next time a portal is reached.
    readonly List<MutationDefinition> _shopOffers = new List<MutationDefinition>();

    // Called by ExitPortal when the player reaches the exit.
    public void OnPortalEntered()
    {
        bool isLastBiome = _biomeIndex >= BiomeOrder.Length - 1;
        if (isLastBiome)
        {
            ShowOnly(VictoryPanel);
            return;
        }

        RollShopOffers();
        ShowOnly(ShopPanel);
        RefreshShop();
    }

    void RollShopOffers()
    {
        _shopOffers.Clear();
        if (MutationSystem.Instance == null) return;
        _shopOffers.AddRange(MutationSystem.Instance.GetRandomFreeChoices(ShopOfferCount));
    }

    void RefreshShop()
    {
        UpdateMutationPointsUI();
        PopulateMutationCards(ShopCardContainer, _shopOffers, free: false, onPicked: OnMutationPurchased);
    }

    void OnMutationPurchased(MutationDefinition def)
    {
        if (def == null || _mutationPoints < def.Cost) return;

        _mutationPoints -= def.Cost;
        MutationSystem.Instance.SelectMutation(def);
        _shopOffers.Remove(def); // just this card disappears; the other offers stay put
        RefreshShop();
    }

    void OnNextStageClicked()
    {
        _biomeIndex++;
        ShowHintThen(() => StartBiome(_biomeIndex));
    }

    // ── Mutation card list helper (shared by free-pick and shop screens) ─
    void PopulateMutationCards(Transform container, IReadOnlyList<MutationDefinition> options, bool free, Action<MutationDefinition> onPicked)
    {
        if (container == null || MutationCardPrefab == null) return;

        for (int i = container.childCount - 1; i >= 0; i--)
            Destroy(container.GetChild(i).gameObject);

        if (options == null) return;

        foreach (var def in options)
        {
            if (def == null) continue;
            var card = Instantiate(MutationCardPrefab, container);
            bool canAfford = free || _mutationPoints >= def.Cost;
            card.Init(def, def.Cost, free, canAfford, () => onPicked(def));
        }
    }

    // ── Hint / education screen ─────────────────────────────────────────
    void ShowHintThen(Action after)
    {
        _afterHint = after;
        ShowOnly(HintPanel);

        if (ImmuneFacts != null && ImmuneFacts.Length > 0)
        {
            var fact = ImmuneFacts[UnityEngine.Random.Range(0, ImmuneFacts.Length)];
            if (HintTitleText != null) HintTitleText.text = fact.Title;
            if (HintBodyText  != null) HintBodyText.text  = fact.FactText;
            if (HintImage     != null) HintImage.sprite   = fact.Image;
        }
    }

    void OnHintSkipClicked()
    {
        var next = _afterHint;
        _afterHint = null;
        next?.Invoke();
    }

    // ── Panel visibility helper ─────────────────────────────────────────
    void ShowOnly(GameObject panel)
    {
        SetActive(StartPanel, panel);
        SetActive(HUDPanel, panel);
        SetActive(DeathPanel, panel);
        SetActive(FreeMutationPanel, panel);
        SetActive(ShopPanel, panel);
        SetActive(VictoryPanel, panel);
        SetActive(HintPanel, panel);

        // Gameplay (enemy AI, physics, player movement) only runs while the
        // HUD/Playing screen is up. Every other screen is a menu/interstitial
        // laid over the world - without this, enemies keep chasing and
        // attacking the player behind the shop/hint/death panels, which is
        // exactly what was happening before this fix.
        // UI buttons/EventSystem are unaffected by timeScale, so menus stay responsive.
        Time.timeScale = (panel == HUDPanel) ? 1f : 0f;
        if (ScreenBackgroundImage)
        {
            ScreenBackgroundImage.enabled = (panel != HUDPanel);
        }
    }

    static void SetActive(GameObject go, GameObject target) => go?.SetActive(go == target);
}

// Simple data container for the educational hint screen (point 8).
// Create an array of these on the GameManager and fill each with a
// title, a short fact, and an illustrative sprite.
[Serializable]
public class ImmuneFact
{
    public string Title;
    [TextArea(2, 5)] public string FactText;
    public Sprite Image;
}
