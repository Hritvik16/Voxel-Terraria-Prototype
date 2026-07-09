using UnityEngine;
using System.Runtime.InteropServices;
using System.Linq;

namespace CoreEngine.Memory
{
    public class MaterialRegistry : MonoBehaviour
    {
        // Must match the exact byte-layout of our HLSL struct
        [StructLayout(LayoutKind.Sequential)]
        public struct MaterialData
        {
            public uint AlbedoRGBA;   // Packed 32-bit color
            public float Roughness;
            public float Emissivity;
            public int Viscosity;
            public int Flammability;
        }

        public ComputeBuffer MaterialPaletteBuffer { get; private set; }
        private const int MAX_MATERIALS = 256;

        private void Awake()
        {
            InitializeRegistry();
        }

        private void InitializeRegistry()
        {
            // Allocate the 256-element palette
            MaterialPaletteBuffer = new ComputeBuffer(MAX_MATERIALS, Marshal.SizeOf(typeof(MaterialData)), ComputeBufferType.Structured);

            // Load all VoxelMaterial ScriptableObjects from the Resources folder (or via direct reference)
            // Note: In production, AssetReference/Addressables is better, but this gets us moving.
            VoxelMaterial[] allMaterials = Resources.LoadAll<VoxelMaterial>("Materials");

            MaterialData[] paletteArray = new MaterialData[MAX_MATERIALS];

            foreach (var mat in allMaterials)
            {
                if (mat.MaterialID == 255)
                {
                    Debug.LogError("Material ID 255 is strictly reserved for the Dynamic VRAM Pointer. Skipping.");
                    continue;
                }

                // Pack the Unity Color32 into a uint for HLSL
                uint packedColor = (uint)((mat.Albedo.a << 24) | (mat.Albedo.b << 16) | (mat.Albedo.g << 8) | mat.Albedo.r);

                paletteArray[mat.MaterialID] = new MaterialData
                {
                    AlbedoRGBA = packedColor,
                    Roughness = mat.Roughness,
                    Emissivity = mat.Emissivity,
                    Viscosity = mat.Viscosity,
                    Flammability = mat.Flammability
                };
            }

            // Ship the registry to the GPU
            MaterialPaletteBuffer.SetData(paletteArray);
            Debug.Log($"<color=#00FFFF>Material Registry Compiled.</color> Loaded {allMaterials.Length} definitions into VRAM.");
        }

        private void OnDestroy()
        {
            MaterialPaletteBuffer?.Release();
        }
    }
}