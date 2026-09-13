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

public interface IEnemyAI
{
    void Initialise(bool[,] tiles, int mapWidth, int mapHeight);
}
