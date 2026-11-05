using System;
using System.Collections.Generic;
using System.Reflection;
using Alta;
using Alta.Inventory;
using Alta.Networking;
using HarmonyLib;
using UnityEngine;

namespace ArmorRestored
{
    internal static class Prefabs
    {
        const uint TemplatePrefab = 23950;
        const uint TemplateItem = 41010;
        const string TemplateName = "Wooden Stirring Spoon";
        const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        const BindingFlags Stat = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

        internal static readonly NetworkPrefab[] ByPiece = new NetworkPrefab[Catalog.Count];
        internal static readonly Item[] Items = new Item[Catalog.Count];
        internal static readonly string[] Refused = new string[Catalog.Count];
        internal static bool Done;
        internal static string Seam;
        static bool _busy, _waitLogged;
        static GameObject _holder;

        internal static bool IsArmor(uint prefabHash)
        {
            int p = Catalog.PieceOf(prefabHash);
            return p >= 0 && ByPiece[p] != null;
        }

        internal static string RefusedLine()
        {
            if (!Done) return "not registered";
            List<string> ids = new List<string>();
            for (int i = 0; i < Catalog.Count; i++) if (Refused[i] != null) ids.Add(Catalog.Id(i));
            return string.Join(",", ids.ToArray());
        }

        internal static void Register(string seam)
        {
            if (Done || _busy) return;
            _busy = true;
            try
            {
                FieldInfo itemsField = typeof(HashedGeneralValue<Item>).GetField("items", Stat);
                MethodInfo addToMap = typeof(PrefabManager).GetMethod("AddToPrefabMap", Stat);
                FieldInfo prefabHash = typeof(NetworkPrefab).GetField("hash", Inst);
                FieldInfo itemHash = typeof(HashedGeneralValue).GetField("hash", Inst);
                if (itemsField == null || addToMap == null || prefabHash == null || itemHash == null)
                {
                    Done = true;
                    Mod.Log.Error("a game seam moved (Item.items, AddToPrefabMap, NetworkPrefab.hash or HashedGeneralValue.hash); no armor registered");
                    return;
                }
                PrefabManager.PrepareSpawnSetups();
                HashedGeneralValue<Item>.CheckItems();
                Dictionary<uint, Item> items = (Dictionary<uint, Item>)itemsField.GetValue(null);
                if (items == null || items.Count == 0)
                {
                    if (!_waitLogged) Mod.Log.Msg("item registry not loaded yet (" + seam + "); retrying");
                    _waitLogged = true;
                    return;
                }
                Done = true;
                Seam = seam;

                NetworkPrefab source = PrefabManager.GetPrefab(TemplatePrefab);
                Pickup sourcePickup = source != null ? source.GetComponent<Pickup>() : null;
                Item sourceItem = sourcePickup != null ? sourcePickup.Item : null;
                if (source == null || source.name != TemplateName || sourceItem == null || sourceItem.Hash != TemplateItem)
                {
                    for (int i = 0; i < Catalog.Count; i++) Refused[i] = "template " + TemplatePrefab + " is not " + TemplateName + " here";
                    Mod.Log.Error("template " + TemplatePrefab + " is not " + TemplateName + " with Item " + TemplateItem + "; no armor registered");
                    return;
                }

                _holder = new GameObject("ArmorRestored Templates");
                _holder.SetActive(false);
                UnityEngine.Object.DontDestroyOnLoad(_holder);

                int ok = 0;
                for (int i = 0; i < Catalog.Count; i++)
                {
                    try
                    {
                        Refused[i] = Build(i, source, sourceItem, items, addToMap, prefabHash, itemHash);
                        if (Refused[i] == null) ok++;
                        else Mod.Log.Error(Catalog.Id(i) + " refused: " + Refused[i]);
                    }
                    catch (Exception e)
                    {
                        Refused[i] = "threw " + e.Message;
                        Mod.Log.Error(Catalog.Id(i) + ": " + e);
                    }
                }
                FieldInfo byName = typeof(HashedGeneralValue<Item>).GetField("itemsByName", Stat);
                if (byName != null) byName.SetValue(null, null);
                Mod.Log.Msg(ok + "/" + Catalog.Count + " armor pieces registered via " + seam + " (numbers " + Catalog.FirstNumber + "-" + (Catalog.FirstNumber + Catalog.Count - 1) + ").");
                if (Mod.IsServer && Settings.Loot) Loot.Add();
            }
            catch (Exception e)
            {
                Done = true;
                Mod.Log.Error("registration failed: " + e);
            }
            finally
            {
                _busy = false;
            }
        }

        static string Build(int piece, NetworkPrefab source, Item sourceItem, Dictionary<uint, Item> items, MethodInfo addToMap, FieldInfo prefabHash, FieldInfo itemHash)
        {
            uint number = (uint)Catalog.Number(piece);
            NetworkPrefab taken = PrefabManager.GetPrefab(number);
            if (taken != null) return "prefab " + number + " is already '" + taken.name + "'";
            Item takenItem;
            if (items.TryGetValue(number, out takenItem)) return "Item " + number + " is already '" + takenItem.name + "'";

            PieceDef def = Pack.Pieces[piece];
            GameObject copy = UnityEngine.Object.Instantiate(source.gameObject, _holder.transform);
            copy.name = Catalog.Title(piece);
            NetworkPrefab np = copy.GetComponent<NetworkPrefab>();
            prefabHash.SetValue(np, (int)number);
            Transform root = copy.transform;

            Transform grip = root.Find("Tight Grip");
            Transform mesh = root.Find("Mesh");
            Transform physics = root.Find("Collision");
            if (grip == null || mesh == null || physics == null)
            {
                UnityEngine.Object.DestroyImmediate(copy);
                return "template has no Tight Grip, Mesh or Collision child";
            }
            UnityEngine.Object.DestroyImmediate(grip.gameObject);
            for (int i = physics.childCount - 1; i >= 0; i--) UnityEngine.Object.DestroyImmediate(physics.GetChild(i).gameObject);
            for (int i = 0; i < def.Colliders.Count; i++) AddCollider(physics, "Collider " + i, def.Colliders[i]);

            LODGroup lods = mesh.GetComponent<LODGroup>();
            if (lods != null) UnityEngine.Object.DestroyImmediate(lods);
            for (int i = mesh.childCount - 1; i >= 0; i--) UnityEngine.Object.DestroyImmediate(mesh.GetChild(i).gameObject);
            if (!Mod.IsServer)
            {
                for (int i = 0; i < def.Pickup.Count; i++)
                {
                    PickupPart part = def.Pickup[i];
                    GameObject go = new GameObject(Pack.Meshes[part.Mesh].Name);
                    go.transform.SetParent(mesh, false);
                    Place(go.transform, part.Local);
                    go.AddComponent<MeshFilter>().sharedMesh = Pack.Meshes[part.Mesh].Get();
                    go.AddComponent<MeshRenderer>().sharedMaterials = Pack.Mats(part.Mats);
                }
            }

            Pickup pickup = copy.GetComponent<Pickup>();
            if (pickup == null) { UnityEngine.Object.DestroyImmediate(copy); return "template has no Pickup"; }
            SetField(pickup, "isTightGrab", false);
            SetField(pickup, "grabPoints", new GrabPoint[0]);
            Collider[] all = pickup.GetComponentsInChildren<Collider>(true);
            SetField(pickup, "childrenColliders", all);
            HashSet<Collider> live = pickup.Colliders;
            if (live != null) { live.Clear(); foreach (Collider c in all) live.Add(c); }

            Item item = UnityEngine.Object.Instantiate(sourceItem);
            item.name = Catalog.Title(piece);
            itemHash.SetValue(item, (int)number);
            item.Prefab = np;
            foreach (Pickup p in copy.GetComponentsInChildren<Pickup>(true))
                if (ReferenceEquals(p.Item, sourceItem)) p.Item = item;

            addToMap.Invoke(null, new object[] { new NetworkPrefab[] { np } });
            if (!ReferenceEquals(PrefabManager.GetPrefab(number), np)) return "the prefab map did not take it";
            items[number] = item;
            ByPiece[piece] = np;
            Items[piece] = item;
            return null;
        }

        static void SetField(object o, string name, object value)
        {
            for (Type t = o.GetType(); t != null; t = t.BaseType)
            {
                FieldInfo f = t.GetField(name, Inst | BindingFlags.DeclaredOnly);
                if (f == null) continue;
                f.SetValue(o, value);
                return;
            }
            throw new MissingFieldException(o.GetType().Name, name);
        }

        internal static void Place(Transform t, Matrix4x4 m)
        {
            t.localPosition = m.GetColumn(3);
            Vector3 fwd = m.GetColumn(2), up = m.GetColumn(1);
            Vector3 right = m.GetColumn(0);
            Quaternion rot = fwd.sqrMagnitude > 1e-8f && up.sqrMagnitude > 1e-8f ? Quaternion.LookRotation(fwd, up) : Quaternion.identity;
            t.localRotation = rot;
            float sx = right.magnitude * (Vector3.Dot(right, rot * Vector3.right) < 0f ? -1f : 1f);
            t.localScale = new Vector3(sx, up.magnitude, fwd.magnitude);
        }

        static void AddCollider(Transform parent, string name, ColliderSpec c)
        {
            GameObject go = new GameObject(name);
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            Place(go.transform, c.Local);
            switch (c.Shape)
            {
                case 0:
                    BoxCollider b = go.AddComponent<BoxCollider>();
                    b.center = c.Center;
                    b.size = c.Size;
                    break;
                case 1:
                    CapsuleCollider cap = go.AddComponent<CapsuleCollider>();
                    cap.center = c.Center;
                    cap.radius = c.Radius;
                    cap.height = c.Height;
                    cap.direction = c.Direction;
                    break;
                default:
                    SphereCollider s = go.AddComponent<SphereCollider>();
                    s.center = c.Center;
                    s.radius = c.Radius;
                    break;
            }
        }
    }

    [HarmonyPatch(typeof(PrefabManager), "PrepareSpawnSetups")]
    static class PrefabSeam
    {
        [HarmonyPriority(Priority.First)]
        static void Postfix()
        {
            try { Prefabs.Register("PrepareSpawnSetups"); }
            catch (Exception e) { Mod.Log.Error("prefab seam: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(NetworkScene), "FirstInitialize", new Type[] { typeof(bool) })]
    static class ServerSeam
    {
        static bool _once;

        [HarmonyPriority(Priority.First)]
        static void Postfix(bool isServer)
        {
            if (!isServer) return;
            try
            {
                Prefabs.Register("FirstInitialize");
                if (_once || !Mod.IsServer) return;
                _once = true;
                Main.RegisterCommands();
                Net.InstallJoinStep();
            }
            catch (Exception e) { Mod.Log.Error("server seam: " + e); }
        }
    }
}
