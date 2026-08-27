// Assets/CoreEngine/WorldGen/WorldMeta.cs
//
// Phase 3, file 1 of the spec's ordered list (§13 Phase 3): the world.meta
// writer/reader per D.2 — header {uint seed, byte sizeClass, ushort
// formatVersion, uint anchorCount, uint biomeSeedCount, uint contentVersionHash}
// + frozen FeatureAnchor[] + Voronoi biome seeds, CRC32 trailer, atomic write.
//
// D.2 names the FeatureAnchor and biome-seed arrays but does not define their
// field layouts anywhere in Appendix A — so the layouts below are THIS FILE's
// definition, versioned by formatVersion. They are frozen once written
// (world.meta is "written once, read-only"): change = bump FORMAT_VERSION.
//
// This is on the "AI may NOT modify without explicit review" list (§0.x —
// world.meta format). Treat as frozen after this session.
using System;
using System.IO;
using Unity.Mathematics;

namespace VoxelEngine.WorldGen
{
    public enum FeatureKind : byte
    {
        Mountain = 0, // per-column HEIGHT ADD within radius (FeatureCarve.HeightDelta)
        Crater   = 1, // per-column height bowl + rim (FeatureCarve.HeightDelta)
        Cave     = 2, // 3D horizontal air capsule (FeatureCarve.CaveContains)
    }

    // Serialized layout (formatVersion 1), fixed order, little-endian:
    //   byte kind; float cx, cy, cz; float radius; float magnitude;
    //   float dirX, dirZ; float halfLength; uint salt;   (= 37 bytes)
    // All positions/lengths in VOXEL units. cy/dir/halfLength are only
    // meaningful for kind==Cave (written as 0 for height features).
    public struct FeatureAnchor
    {
        public FeatureKind kind;
        public float cx, cy, cz;     // center (voxels); cy unused for height kinds
        public float radius;         // influence radius (height kinds) / bore radius (cave)
        public float magnitude;      // height add (mountain) / bowl depth (crater); unused for cave
        public float dirX, dirZ;     // cave axis, normalized, horizontal
        public float halfLength;     // cave half-length along the axis
        public uint salt;            // per-anchor deterministic variation
    }

    // Serialized layout (formatVersion 1): float x, z; byte biomeId. (= 9 bytes)
    public struct BiomeSeed
    {
        public float x, z;   // voxels
        public byte biomeId; // index into Biomes.Table
    }

    public class WorldMetaData
    {
        public uint seed;
        public byte sizeClass;
        public ushort formatVersion;
        public uint contentVersionHash;
        public FeatureAnchor[] anchors = Array.Empty<FeatureAnchor>();
        public BiomeSeed[] biomeSeeds = Array.Empty<BiomeSeed>();
    }

    public static class WorldMeta
    {
        public const ushort FORMAT_VERSION = 1;

        // ---------- Serialization ----------

        public static byte[] Serialize(WorldMetaData meta)
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);

            // D.2 header, exact field order.
            w.Write(meta.seed);
            w.Write(meta.sizeClass);
            w.Write(meta.formatVersion);
            w.Write((uint)meta.anchors.Length);
            w.Write((uint)meta.biomeSeeds.Length);
            w.Write(meta.contentVersionHash);

            foreach (var a in meta.anchors)
            {
                w.Write((byte)a.kind);
                w.Write(a.cx); w.Write(a.cy); w.Write(a.cz);
                w.Write(a.radius);
                w.Write(a.magnitude);
                w.Write(a.dirX); w.Write(a.dirZ);
                w.Write(a.halfLength);
                w.Write(a.salt);
            }
            foreach (var s in meta.biomeSeeds)
            {
                w.Write(s.x); w.Write(s.z);
                w.Write(s.biomeId);
            }

            w.Flush();
            byte[] body = ms.ToArray();

            // CRC32 trailer over everything preceding it (as D.1/D.2).
            uint crc = Crc32(body, 0, body.Length);
            byte[] result = new byte[body.Length + 4];
            Buffer.BlockCopy(body, 0, result, 0, body.Length);
            result[body.Length + 0] = (byte)(crc & 0xFF);
            result[body.Length + 1] = (byte)((crc >> 8) & 0xFF);
            result[body.Length + 2] = (byte)((crc >> 16) & 0xFF);
            result[body.Length + 3] = (byte)((crc >> 24) & 0xFF);
            return result;
        }

        // Returns false (and null meta) on ANY structural problem: short file,
        // CRC mismatch, unknown formatVersion, counts inconsistent with length.
        public static bool TryDeserialize(byte[] bytes, out WorldMetaData meta)
        {
            meta = null;
            if (bytes == null || bytes.Length < 19 + 4) return false; // header + crc

            int bodyLen = bytes.Length - 4;
            uint storedCrc = (uint)(bytes[bodyLen] | (bytes[bodyLen + 1] << 8)
                           | (bytes[bodyLen + 2] << 16) | (bytes[bodyLen + 3] << 24));
            if (Crc32(bytes, 0, bodyLen) != storedCrc) return false;

            try
            {
                using var ms = new MemoryStream(bytes, 0, bodyLen);
                using var r = new BinaryReader(ms);

                var m = new WorldMetaData
                {
                    seed = r.ReadUInt32(),
                    sizeClass = r.ReadByte(),
                    formatVersion = r.ReadUInt16(),
                };
                uint anchorCount = r.ReadUInt32();
                uint seedCount = r.ReadUInt32();
                m.contentVersionHash = r.ReadUInt32();

                if (m.formatVersion != FORMAT_VERSION) return false;

                const int ANCHOR_BYTES = 37, SEED_BYTES = 9, HEADER_BYTES = 19;
                long expected = HEADER_BYTES + (long)anchorCount * ANCHOR_BYTES + (long)seedCount * SEED_BYTES;
                if (expected != bodyLen) return false;

                m.anchors = new FeatureAnchor[anchorCount];
                for (int i = 0; i < anchorCount; i++)
                {
                    m.anchors[i] = new FeatureAnchor
                    {
                        kind = (FeatureKind)r.ReadByte(),
                        cx = r.ReadSingle(), cy = r.ReadSingle(), cz = r.ReadSingle(),
                        radius = r.ReadSingle(),
                        magnitude = r.ReadSingle(),
                        dirX = r.ReadSingle(), dirZ = r.ReadSingle(),
                        halfLength = r.ReadSingle(),
                        salt = r.ReadUInt32(),
                    };
                }
                m.biomeSeeds = new BiomeSeed[seedCount];
                for (int i = 0; i < seedCount; i++)
                {
                    m.biomeSeeds[i] = new BiomeSeed
                    {
                        x = r.ReadSingle(), z = r.ReadSingle(),
                        biomeId = r.ReadByte(),
                    };
                }

                meta = m;
                return true;
            }
            catch (Exception)
            {
                return false; // truncated/garbled stream — reject, never throw upward
            }
        }

        // ---------- File I/O (atomic rename per D.1/D.2) ----------

        public static void WriteAtomic(string path, WorldMetaData meta)
        {
            byte[] bytes = Serialize(meta);
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            string tmp = path + ".tmp";
            File.WriteAllBytes(tmp, bytes);
            if (File.Exists(path))
                File.Replace(tmp, path, null); // atomic on APFS
            else
                File.Move(tmp, path);
        }

        public static bool TryRead(string path, out WorldMetaData meta)
        {
            meta = null;
            if (!File.Exists(path)) return false;
            byte[] bytes;
            try { bytes = File.ReadAllBytes(path); }
            catch (IOException) { return false; }
            return TryDeserialize(bytes, out meta);
        }

        // ---------- CRC32 (reflected, poly 0xEDB88320 — same family as D.1) ----------

        private static uint[] _crcTable;

        public static uint Crc32(byte[] data, int offset, int count)
        {
            if (_crcTable == null)
            {
                var t = new uint[256];
                for (uint i = 0; i < 256; i++)
                {
                    uint c = i;
                    for (int k = 0; k < 8; k++)
                        c = ((c & 1) != 0) ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                    t[i] = c;
                }
                _crcTable = t;
            }
            uint crc = 0xFFFFFFFFu;
            for (int i = offset; i < offset + count; i++)
                crc = _crcTable[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
            return crc ^ 0xFFFFFFFFu;
        }
    }
}