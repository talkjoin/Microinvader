using System.Collections.Generic;
using UnityEngine;

public class MutationSystem : MonoBehaviour
{
    public static MutationSystem Instance { get; private set; }

    [Header("All possible mutations (assign ScriptableObjects)")]
    public MutationDefinition[] AllMutations;

    readonly List<MutationDefinition> _active = new List<MutationDefinition>();

    public event System.Action<MutationDefinition[]> OnChoicesOffered;
    public event System.Action<MutationDefinition>   OnSelected;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        Load();
    }

    
    public void OfferMutations(BiomeType biome)
    {
        var pool = FilterByBiome(biome);
        OnChoicesOffered?.Invoke(Pick(pool, 3));
    }

    
    public void SelectMutation(MutationDefinition m)
    {
        if (!_active.Contains(m)) _active.Add(m);
        Save();
        OnSelected?.Invoke(m);
    }

    
    public void ApplyToPlayer(BacteriumController player)
    {
        float sp = 1f, dm = 1f, hm = 1f;
        foreach (var m in _active) { sp *= m.SpeedMultiplier; dm *= m.DamageMultiplier; hm *= m.HealthMultiplier; }
        player.ApplyMutations(sp, dm, hm);
    }

    public void ResetAll() { _active.Clear(); PlayerPrefs.DeleteKey("MI_Mutations"); }

    
    public IReadOnlyList<MutationDefinition> ActiveMutations => _active;

    public bool IsActive(MutationDefinition m) => m != null && _active.Contains(m);

    
    public List<MutationDefinition> GetPurchasableMutations()
    {
        var list = new List<MutationDefinition>();
        foreach (var m in AllMutations)
            if (m != null && !_active.Contains(m)) list.Add(m);
        return list;
    }

    
    public MutationDefinition[] GetRandomFreeChoices(int n) => Pick(GetPurchasableMutations().ToArray(), n);

    MutationDefinition[] FilterByBiome(BiomeType b)
    {
        var out_ = new List<MutationDefinition>();
        foreach (var m in AllMutations) if (m.SourceBiome == b) out_.Add(m);
        if (out_.Count == 0) return AllMutations;   // fallback: all
        return out_.ToArray();
    }

    MutationDefinition[] Pick(MutationDefinition[] pool, int n)
    {
        var list = new List<MutationDefinition>(pool);
        int take = Mathf.Min(n, list.Count);
        for (int i = 0; i < take; i++)
        { int j = Random.Range(i, list.Count); (list[i], list[j]) = (list[j], list[i]); }
        return list.GetRange(0, take).ToArray();
    }

    void Save()
    {
        var ids = new List<string>();
        foreach (var m in _active) ids.Add(m.MutationId);
        PlayerPrefs.SetString("MI_Mutations", string.Join(",", ids));
        PlayerPrefs.Save();
    }

    void Load()
    {
        string s = PlayerPrefs.GetString("MI_Mutations", "");
        if (string.IsNullOrEmpty(s)) return;
        foreach (string id in s.Split(','))
            foreach (var m in AllMutations)
                if (m.MutationId == id && !_active.Contains(m)) { _active.Add(m); break; }
    }
}
