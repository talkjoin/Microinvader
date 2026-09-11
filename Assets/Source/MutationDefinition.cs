// ScriptableObject MicroInvader ->Mutation Definition

using UnityEngine;

[CreateAssetMenu(fileName = "Mutation_New", menuName = "MicroInvader/Mutation Definition")]
public class MutationDefinition : ScriptableObject
{
    public string      MutationId;
    public string      DisplayName;
    [TextArea]
    public string      Description;
    public Sprite      Icon;
    public BiomeType   SourceBiome;

    [Min(0.1f)] public float SpeedMultiplier  = 1f;
    [Min(0.1f)] public float DamageMultiplier = 1f;
    [Min(0)]    public int   BonusShield      = 0;

    [Header("Shop")]
    [Tooltip("Mutation points required to purchase this at the portal shop.")]
    [Min(0)]    public int   Cost             = 10;
}
