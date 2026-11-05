using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ArmorRestored
{
    internal sealed class TexDef
    {
        public string Name;
        public int Format, W, H, Mips;
        public byte[] Data;
        public bool Linear;
        Texture2D _tex;

        public Texture2D Get()
        {
            if (_tex != null) return _tex;
            if (Format < 0)
            {
                Mod.Log.Warning("texture " + Name + " is not DXT; left out");
                return null;
            }
            _tex = new Texture2D(W, H, (TextureFormat)Format, Mips > 1, Linear);
            _tex.name = "ArmorRestored " + Name;
            _tex.LoadRawTextureData(Data);
            _tex.Apply(false, true);
            UnityEngine.Object.DontDestroyOnLoad(_tex);
            Data = null;
            return _tex;
        }
    }

    internal sealed class MatDef
    {
        public string Name;
        public int Main, Normal, Metal, Occlusion;
        public Color Color;
        public float Metallic, Gloss;
        Material _mat;

        public Material Get()
        {
            if (_mat != null) return _mat;
            Shader sh = Shader.Find("Standard");
            if (sh == null) sh = Shader.Find("Township Tale/Cheap/SimpleStandard");
            _mat = new Material(sh);
            _mat.name = "ArmorRestored " + Name;
            _mat.SetColor("_Color", Color);
            _mat.SetFloat("_Metallic", Metallic);
            _mat.SetFloat("_Glossiness", Gloss);
            _mat.SetFloat("_GlossMapScale", Gloss);
            Texture2D t;
            if ((t = Tex(Main)) != null) _mat.SetTexture("_MainTex", t);
            if (Name == "Metal Armor")
            {
                _mat.SetFloat("_Metallic", 0.45f);
                _mat.SetFloat("_Glossiness", 0.4f);
            }
            else if ((t = Tex(Metal)) != null)
            {
                _mat.SetTexture("_MetallicGlossMap", t);
                _mat.EnableKeyword("_METALLICGLOSSMAP");
            }
            if ((t = Tex(Normal)) != null)
            {
                _mat.SetTexture("_BumpMap", t);
                _mat.EnableKeyword("_NORMALMAP");
            }
            if ((t = Tex(Occlusion)) != null) _mat.SetTexture("_OcclusionMap", t);
            UnityEngine.Object.DontDestroyOnLoad(_mat);
            return _mat;
        }

        static Texture2D Tex(int i)
        {
            return i >= 0 && i < Pack.Textures.Count ? Pack.Textures[i].Get() : null;
        }
    }

    internal sealed class MeshDef
    {
        public string Name;
        public Vector3[] V, N;
        public Vector4[] T;
        public Vector2[] UV;
        public BoneWeight[] W;
        public Matrix4x4[] Bind;
        public int[][] Subs;
        Mesh _mesh;

        public Mesh Get()
        {
            if (_mesh != null) return _mesh;
            Mesh m = new Mesh();
            m.name = "ArmorRestored " + Name;
            if (V.Length > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.vertices = V;
            if (N.Length == V.Length) m.normals = N;
            if (T.Length == V.Length) m.tangents = T;
            if (UV.Length == V.Length) m.uv = UV;
            if (W.Length == V.Length) m.boneWeights = W;
            if (Bind.Length > 0) m.bindposes = Bind;
            m.subMeshCount = Subs.Length;
            for (int i = 0; i < Subs.Length; i++) m.SetTriangles(Subs[i], i);
            if (N.Length != V.Length) m.RecalculateNormals();
            m.RecalculateBounds();
            m.UploadMeshData(true);
            UnityEngine.Object.DontDestroyOnLoad(m);
            _mesh = m;
            V = N = null; T = null; UV = null; W = null; Subs = null;
            return m;
        }
    }

    internal sealed class WornPart
    {
        public bool Skinned;
        public int Mesh;
        public int[] Mats;
        public string Bone;
        public Matrix4x4 Local;
        public string Root;
        public string[] Bones;
    }

    internal sealed class PickupPart
    {
        public int Mesh;
        public int[] Mats;
        public Matrix4x4 Local;
    }

    internal sealed class ColliderSpec
    {
        public int Shape;
        public Matrix4x4 Local;
        public Vector3 Center, Size;
        public float Radius, Height;
        public int Direction;
    }

    internal sealed class PieceDef
    {
        public string Id, Set, Slot, Source;
        public readonly List<WornPart> Worn = new List<WornPart>();
        public readonly List<PickupPart> Pickup = new List<PickupPart>();
        public readonly List<ColliderSpec> Colliders = new List<ColliderSpec>();
    }

    internal static class Pack
    {
        internal static readonly List<TexDef> Textures = new List<TexDef>();
        internal static readonly List<MatDef> Materials = new List<MatDef>();
        internal static readonly List<MeshDef> Meshes = new List<MeshDef>();
        internal static readonly List<PieceDef> Pieces = new List<PieceDef>();

        internal static bool Loaded { get { return Pieces.Count > 0; } }

        internal static void Load(bool visuals)
        {
            if (Loaded) return;
            Stream s = typeof(Pack).Assembly.GetManifestResourceStream("ArmorRestored.armor.pack");
            if (s == null) throw new FileNotFoundException("the DLL carries no armor.pack");
            using (BinaryReader r = new BinaryReader(s))
            {
                if (new string(r.ReadChars(4)) != "ARMR" || r.ReadInt32() != 1) throw new InvalidDataException("armor.pack is not version 1");
                int n = r.ReadInt32();
                for (int i = 0; i < n; i++)
                {
                    TexDef t = new TexDef { Name = Str(r), Format = r.ReadInt32(), W = r.ReadInt32(), H = r.ReadInt32(), Mips = r.ReadInt32() };
                    int len = r.ReadInt32();
                    if (visuals) t.Data = r.ReadBytes(len);
                    else r.BaseStream.Seek(len, SeekOrigin.Current);
                    Textures.Add(t);
                }
                n = r.ReadInt32();
                for (int i = 0; i < n; i++)
                {
                    MatDef m = new MatDef { Name = Str(r), Main = r.ReadInt32(), Normal = r.ReadInt32(), Metal = r.ReadInt32(), Occlusion = r.ReadInt32() };
                    m.Color = new Color(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                    m.Metallic = r.ReadSingle();
                    m.Gloss = r.ReadSingle();
                    if (m.Normal >= 0) Textures[m.Normal].Linear = true;
                    if (m.Metal >= 0) Textures[m.Metal].Linear = true;
                    Materials.Add(m);
                }
                n = r.ReadInt32();
                for (int i = 0; i < n; i++) Meshes.Add(ReadMesh(r, visuals));
                n = r.ReadInt32();
                for (int i = 0; i < n; i++) Pieces.Add(ReadPiece(r));
            }
        }

        static MeshDef ReadMesh(BinaryReader r, bool keep)
        {
            MeshDef m = new MeshDef { Name = Str(r) };
            int n = r.ReadInt32();
            m.V = new Vector3[n];
            for (int i = 0; i < n; i++) m.V[i] = V3(r);
            n = r.ReadInt32();
            m.N = new Vector3[n];
            for (int i = 0; i < n; i++) m.N[i] = V3(r);
            n = r.ReadInt32();
            m.T = new Vector4[n];
            for (int i = 0; i < n; i++) m.T[i] = new Vector4(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            n = r.ReadInt32();
            m.UV = new Vector2[n];
            for (int i = 0; i < n; i++) m.UV[i] = new Vector2(r.ReadSingle(), r.ReadSingle());
            n = r.ReadInt32();
            m.W = new BoneWeight[n];
            for (int i = 0; i < n; i++)
            {
                BoneWeight w = new BoneWeight();
                w.boneIndex0 = r.ReadInt32(); w.boneIndex1 = r.ReadInt32(); w.boneIndex2 = r.ReadInt32(); w.boneIndex3 = r.ReadInt32();
                w.weight0 = r.ReadSingle(); w.weight1 = r.ReadSingle(); w.weight2 = r.ReadSingle(); w.weight3 = r.ReadSingle();
                m.W[i] = w;
            }
            n = r.ReadInt32();
            m.Bind = new Matrix4x4[n];
            for (int i = 0; i < n; i++) m.Bind[i] = RowMajor(r);
            n = r.ReadInt32();
            m.Subs = new int[n][];
            for (int i = 0; i < n; i++)
            {
                int c = r.ReadInt32();
                int[] idx = new int[c];
                for (int k = 0; k < c; k++) idx[k] = r.ReadInt32();
                m.Subs[i] = idx;
            }
            if (!keep) { m.V = null; m.N = null; m.T = null; m.UV = null; m.W = null; m.Subs = null; }
            return m;
        }

        static PieceDef ReadPiece(BinaryReader r)
        {
            PieceDef p = new PieceDef { Id = Str(r), Set = Str(r), Slot = Str(r), Source = Str(r) };
            int n = r.ReadInt32();
            for (int i = 0; i < n; i++)
            {
                WornPart w = new WornPart { Skinned = r.ReadInt32() == 1, Mesh = r.ReadInt32() };
                w.Mats = Ints(r);
                if (!w.Skinned)
                {
                    w.Bone = Str(r);
                    w.Local = ColumnMajor(r);
                }
                else
                {
                    w.Root = Str(r);
                    int b = r.ReadInt32();
                    w.Bones = new string[b];
                    for (int k = 0; k < b; k++) w.Bones[k] = Str(r);
                }
                p.Worn.Add(w);
            }
            n = r.ReadInt32();
            for (int i = 0; i < n; i++)
            {
                PickupPart pp = new PickupPart { Mesh = r.ReadInt32() };
                pp.Mats = Ints(r);
                pp.Local = ColumnMajor(r);
                p.Pickup.Add(pp);
            }
            n = r.ReadInt32();
            for (int i = 0; i < n; i++)
            {
                ColliderSpec c = new ColliderSpec { Shape = r.ReadInt32() };
                c.Local = ColumnMajor(r);
                c.Center = V3(r);
                if (c.Shape == 0) c.Size = V3(r);
                else if (c.Shape == 1) { c.Radius = r.ReadSingle(); c.Height = r.ReadSingle(); c.Direction = r.ReadInt32(); }
                else c.Radius = r.ReadSingle();
                p.Colliders.Add(c);
            }
            return p;
        }

        static int[] Ints(BinaryReader r)
        {
            int n = r.ReadInt32();
            int[] a = new int[n];
            for (int i = 0; i < n; i++) a[i] = r.ReadInt32();
            return a;
        }

        static string Str(BinaryReader r)
        {
            int n = r.ReadInt32();
            return System.Text.Encoding.UTF8.GetString(r.ReadBytes(n));
        }

        static Vector3 V3(BinaryReader r)
        {
            return new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
        }

        static Matrix4x4 ColumnMajor(BinaryReader r)
        {
            Matrix4x4 m = new Matrix4x4();
            for (int i = 0; i < 16; i++) m[i] = r.ReadSingle();
            return m;
        }

        static Matrix4x4 RowMajor(BinaryReader r)
        {
            Matrix4x4 m = new Matrix4x4();
            for (int row = 0; row < 4; row++)
                for (int col = 0; col < 4; col++) m[row, col] = r.ReadSingle();
            return m;
        }

        internal static Material[] Mats(int[] idx)
        {
            Material[] a = new Material[idx.Length];
            for (int i = 0; i < idx.Length; i++) a[i] = Materials[idx[i]].Get();
            return a;
        }
    }
}
