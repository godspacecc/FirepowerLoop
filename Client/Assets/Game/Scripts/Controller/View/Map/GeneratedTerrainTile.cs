using UnityEngine;

namespace Game
{
    public sealed class GeneratedTerrainTile : MonoBehaviour
    {
        [SerializeField] private string mapName;
        [SerializeField] private int row;
        [SerializeField] private int column;
        [SerializeField] private int totalRows;
        [SerializeField] private int totalColumns;
        [SerializeField] private float tileWorldSize;
        [SerializeField] private int heightmapResolution;
        [SerializeField] private int generatorVersion;
        [SerializeField] private Terrain terrain;

        public string MapName => mapName;
        public int Row => row;
        public int Column => column;
        public int TotalRows => totalRows;
        public int TotalColumns => totalColumns;
        public float TileWorldSize => tileWorldSize;
        public int HeightmapResolution => heightmapResolution;
        public int GeneratorVersion => generatorVersion;
        public Terrain Terrain => terrain;
    }
}
