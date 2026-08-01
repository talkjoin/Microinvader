// GameData.cs
// Shared enums and interfaces used across all MicroInvader scripts.
// No namespace — drop this anywhere inside your Unity Assets/Scripts folder.

public enum BiomeType
{
    Lungs,
    Bloodstream,
    LymphNodes,
    BrainCortex
}

public enum EnemyType
{
    Macrophage,
    Neutrophil,
    Antibody
}

// All enemy AI components implement this so DungeonManager can
// hand them the tile grid without knowing their concrete type.
public interface IEnemyAI
{
    void Initialise(bool[,] tiles, int mapWidth, int mapHeight);
}
