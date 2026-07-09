using UnityEngine;

namespace CoreEngine.Memory
{
    [CreateAssetMenu(fileName = "NewVoxelMaterial", menuName = "Voxel Engine/Material")]
    public class VoxelMaterial : ScriptableObject
    {
        [Header("Identity (0 = Air, 255 = Dynamic Pointer)")]
        [Range(0, 255)]
        public byte MaterialID;

        [Header("Rendering Properties")]
        public Color32 Albedo;
        [Range(0f, 1f)] public float Roughness;
        [Range(0f, 10f)] public float Emissivity; // For glowing materials like Lava/Torches

        [Header("Simulation Rules")]
        [Range(0, 100)] public int Viscosity;     // E.g., Water = 1, Honey = 30
        [Range(0, 100)] public int Flammability;
    }
}