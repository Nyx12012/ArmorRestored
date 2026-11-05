using System;
using System.Collections.Generic;
using System.Reflection;
using Alta.Networking;
using Alta.Networking.Scripts.Player;
using Alta.Utilities;
using UnityEngine;

namespace ArmorRestored
{
    internal static class ClientArmor
    {
        sealed class Dressed
        {
            public Player Player;
            public Transform Root;
            public Transform Rig;
            public Wiring Bones;
            public int[] Shown;
            public GameObject[] Slots = new GameObject[Catalog.SlotCount];
        }

        static readonly Dictionary<int, int[]> Table = new Dictionary<int, int[]>();
        static readonly Dictionary<int, Dressed> Bodies = new Dictionary<int, Dressed>();
        static readonly Pickup[] Held = new Pickup[2];
        static readonly bool[] Closed = new bool[2];
        static float _nextDress, _cooldown, _tookUntil;
        static uint _tookId;
        static int _tookHand;
        static bool _tableDirty;
        static object _soundOn, _soundOff;
        static bool _soundsLooked;

        internal static void OnTable(Dictionary<int, int[]> rows)
        {
            Table.Clear();
            foreach (KeyValuePair<int, int[]> kv in rows) Table[kv.Key] = kv.Value;
            _tableDirty = true;
        }

        internal static void OnTook(uint id, int hand)
        {
            _tookId = id;
            _tookHand = hand;
            _tookUntil = Time.unscaledTime + 3f;
            Sound(false);
        }

        static float _nextCensus;
        static string _lastCensus;

        internal static void Clear()
        {
            foreach (Dressed d in Bodies.Values)
                foreach (GameObject g in d.Slots)
                    if (g != null) UnityEngine.Object.Destroy(g);
            Bodies.Clear();
            Table.Clear();
            Held[0] = Held[1] = null;
            Closed[0] = Closed[1] = false;
            PendingSlot[0] = PendingSlot[1] = -1;
            Debugged[0] = Debugged[1] = null;
            _tookId = 0;
            _tableDirty = false;
            _lastCensus = null;
        }

        internal static void Tick()
        {
            if (!Gate.IsOpen || !Prefabs.Done) return;
            if (Settings.Debug && Time.unscaledTime >= _nextCensus) Census();
            if (_tableDirty || Time.unscaledTime >= _nextDress)
            {
                _tableDirty = false;
                _nextDress = Time.unscaledTime + 0.5f;
                Dress();
            }
            Hands();
            if (_tookId != 0) GrabTaken();
        }

        static void Census()
        {
            _nextCensus = Time.unscaledTime + 3f;
            int n = 0, drawn = 0;
            string first = "";
            foreach (Pickup p in UnityEngine.Object.FindObjectsOfType<Pickup>())
            {
                int piece = PieceOf(p);
                if (piece < 0) continue;
                n++;
                foreach (Renderer r in p.GetComponentsInChildren<Renderer>(true)) if (r.enabled && r.gameObject.activeInHierarchy) { drawn++; break; }
                if (first.Length == 0) first = Catalog.Id(piece) + " " + p.Entity.Identifier + " at " + p.transform.position.ToString("F2") + " active " + p.gameObject.activeInHierarchy;
            }
            string line = n + " armor pickup(s), " + drawn + " with a renderer on" + (first.Length > 0 ? "; first " + first : "");
            if (line == _lastCensus) return;
            _lastCensus = line;
            Mod.Log.Msg("census: " + line);
        }

        static void Dress()
        {
            List<int> gone = new List<int>();
            foreach (KeyValuePair<int, Dressed> kv in Bodies) if (kv.Value.Root == null) gone.Add(kv.Key);
            foreach (int k in gone) Bodies.Remove(k);

            Player local = PlayerController.Current != null ? PlayerController.Current.NetworkPlayer : null;
            foreach (IPlayer ip in Player.AllPlayers)
            {
                Player p = ip as Player;
                if (p == null || p.UserInfo == null || p.PlayerCharacter == null) continue;
                int[] worn;
                if (!Table.TryGetValue(p.UserInfo.Identifier, out worn)) worn = ServerArmor.Empty();
                Transform rig = RigOf(p);
                if (rig == null) continue;
                int key = p.PlayerCharacter.GetInstanceID();
                Dressed d;
                if (Bodies.TryGetValue(key, out d) && d.Rig != rig)
                {
                    foreach (GameObject g in d.Slots) if (g != null) UnityEngine.Object.Destroy(g);
                    d = null;
                }
                if (d == null)
                {
                    d = new Dressed { Player = p, Root = p.PlayerCharacter.transform, Rig = rig, Shown = ServerArmor.Empty() };
                    d.Bones = Body.Wire(rig, false);
                    Bodies[key] = d;
                }
                bool isLocal = ReferenceEquals(p, local);
                for (int s = 0; s < Catalog.SlotCount; s++)
                {
                    int want = worn[s];
                    if (isLocal && s == 0 && !Settings.OwnHelmet) want = -1;
                    if (d.Shown[s] == want && (want < 0 || d.Slots[s] != null)) continue;
                    if (d.Slots[s] != null) UnityEngine.Object.Destroy(d.Slots[s]);
                    d.Slots[s] = want >= 0 ? Build(d, want) : null;
                    d.Shown[s] = want;
                    if (Settings.Debug && isLocal && want >= 0) Mod.Log.Msg(Catalog.Title(want) + " sits at " + FromHead(SlotPoint(d, s)).ToString("F2") + " from the head (right, up, forward)");
                }
            }
        }

        static Transform RigOf(Player p)
        {
            try
            {
                Alta.Customization.PlayerCustomizationHandler h = p.PlayerCharacter.CustomizationHandler;
                return h == null || h.IsDestroyed || h.CharacterDummy == null ? null : h.RootBone;
            }
            catch (Exception) { return null; }
        }

        static GameObject Build(Dressed d, int piece)
        {
            PieceDef def = Pack.Pieces[piece];
            GameObject holder = new GameObject("ArmorRestored " + Catalog.Id(piece));
            holder.transform.SetParent(d.Root, false);
            int layer = BodyLayer(d.Root);
            int missing = 0;
            foreach (WornPart part in def.Worn)
            {
                MeshDef md = Pack.Meshes[part.Mesh];
                GameObject go = new GameObject(md.Name);
                go.layer = layer;
                if (!part.Skinned)
                {
                    Transform bone = Bone(d.Bones, part.Bone);
                    if (bone == null) { missing++; UnityEngine.Object.Destroy(go); continue; }
                    go.transform.SetParent(bone, false);
                    Prefabs.Place(go.transform, part.Local);
                    go.AddComponent<MeshFilter>().sharedMesh = md.Get();
                    go.AddComponent<MeshRenderer>().sharedMaterials = Pack.Mats(part.Mats);
                    go.AddComponent<Owner>().Holder = holder;
                }
                else
                {
                    go.transform.SetParent(holder.transform, false);
                    SkinnedMeshRenderer smr = go.AddComponent<SkinnedMeshRenderer>();
                    Transform rootBone = Bone(d.Bones, part.Root);
                    Transform[] bones = new Transform[part.Bones.Length];
                    for (int i = 0; i < bones.Length; i++)
                    {
                        bones[i] = Bone(d.Bones, part.Bones[i]);
                        if (bones[i] == null) { bones[i] = rootBone; missing++; }
                    }
                    smr.bones = bones;
                    smr.rootBone = rootBone;
                    smr.sharedMesh = md.Get();
                    smr.sharedMaterials = Pack.Mats(part.Mats);
                    smr.updateWhenOffscreen = true;
                }
            }
            if (missing > 0) Mod.Log.Warning(Catalog.Id(piece) + " on " + Players.Name(d.Player) + ": " + missing + " bone(s) not found");
            return holder;
        }

        sealed class Owner : MonoBehaviour
        {
            public GameObject Holder;
            void Update() { if (Holder == null) Destroy(gameObject); }
        }

        static Transform Bone(Wiring w, string name)
        {
            Transform t;
            if (name == null) return null;
            if (w.Bones.TryGetValue(name, out t)) return t;
            string trimmed = name.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
            if (trimmed.EndsWith("_jnt") && w.Bones.TryGetValue(trimmed, out t)) return t;
            return null;
        }

        static int BodyLayer(Transform root)
        {
            foreach (SkinnedMeshRenderer r in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (r.transform.parent != null && r.transform.parent.name == "Baked") return r.gameObject.layer;
            return LayerMask.NameToLayer("Player") >= 0 ? LayerMask.NameToLayer("Player") : 0;
        }

        static Dressed LocalBody()
        {
            PlayerController pc = PlayerController.Current;
            Player p = pc != null ? pc.NetworkPlayer : null;
            if (p == null || p.PlayerCharacter == null) return null;
            Dressed d;
            return Bodies.TryGetValue(p.PlayerCharacter.GetInstanceID(), out d) ? d : null;
        }

        static Interactor HandInteractor(int hand)
        {
            PlayerController pc = PlayerController.Current;
            if (pc == null) return null;
            Controller c = hand == 0 ? pc.LeftController : pc.RightController;
            return c != null ? c.Interactor : null;
        }

        static float Reach(int slot)
        {
            return slot == 1 || slot >= 6 ? 0.4f : 0.3f;
        }

        static void Hands()
        {
            Dressed me = LocalBody();
            int[] worn = null;
            if (me != null && me.Player.UserInfo != null) Table.TryGetValue(me.Player.UserInfo.Identifier, out worn);
            for (int hand = 0; hand < 2; hand++)
            {
                Interactor it = HandInteractor(hand);
                if (it == null) continue;
                Pickup held = it.IsInteracting ? it.InteractingWith as Pickup : null;
                if (held != null && PieceOf(held) < 0) held = null;
                Pickup was = Held[hand];
                if (Settings.Debug && held != null && !ReferenceEquals(was, held)) Mod.Log.Msg("holding " + Catalog.Title(PieceOf(held)) + " in the " + HandName(hand) + " hand");
                if (Settings.Debug && it.IsInteracting && held == null && it.InteractingWith != null && Debugged[hand] != it.InteractingWith)
                    Mod.Log.Msg(HandName(hand) + " hand holds " + it.InteractingWith.GetType().Name + " " + it.InteractingWith.name);
                Debugged[hand] = it.IsInteracting ? it.InteractingWith : null;
                if (was != null && !ReferenceEquals(was, held) && me != null) LetGo(me, was);
                Held[hand] = held;

                bool closed = false;
                try { closed = it.Controller != null && it.Controller.PlayerInput != null && it.Controller.PlayerInput.RawInput.GrabAxis > 0.8f; }
                catch (Exception) { }
                bool edge = closed && !Closed[hand];
                Closed[hand] = closed;
                if (!closed || it.IsInteracting) { PendingSlot[hand] = -1; continue; }
                if (worn == null || me == null || Time.unscaledTime < _cooldown) continue;
                if (edge)
                {
                    PendingSlot[hand] = TouchedSlot(me, worn, it.transform.position, hand);
                    PendingAt[hand] = Time.unscaledTime;
                    if (Settings.Debug) Mod.Log.Msg(HandName(hand) + " hand closed empty " + (PendingSlot[hand] >= 0 ? "on " + Catalog.Title(worn[PendingSlot[hand]]) : "on no worn piece") + " (" + Distances(me, worn, it.transform.position) + ")");
                    continue;
                }
                int slot = PendingSlot[hand];
                if (slot < 0 || Time.unscaledTime - PendingAt[hand] < HoldToRemove) continue;
                PendingSlot[hand] = -1;
                if (worn[slot] < 0 || TouchedSlot(me, worn, it.transform.position, hand) != slot) continue;
                _cooldown = Time.unscaledTime + 1f;
                Net.SendUnequip(slot, hand);
                Mod.Log.Msg("take off " + Catalog.Title(worn[slot]) + " with the " + HandName(hand) + " hand");
            }
        }

        const float HoldToRemove = 0.35f;
        static readonly int[] PendingSlot = { -1, -1 };
        static readonly float[] PendingAt = new float[2];
        static readonly Interactable[] Debugged = new Interactable[2];

        static string HandName(int hand)
        {
            return hand == 0 ? "left" : "right";
        }

        static string Distances(Dressed me, int[] worn, Vector3 hand)
        {
            List<string> parts = new List<string>();
            for (int s = 0; s < Catalog.SlotCount; s++)
            {
                if (worn[s] < 0) continue;
                float d = Vector3.Distance(hand, SlotPoint(me, s));
                parts.Add(Catalog.Slots[s].Id + " " + d.ToString("0.00"));
            }
            return parts.Count == 0 ? "nothing worn" : string.Join(", ", parts.ToArray()) + " m";
        }

        static Vector3 FromHead(Vector3 p)
        {
            PlayerController pc = PlayerController.Current;
            if (pc == null || pc.Head == null) return p;
            return Quaternion.Inverse(Quaternion.Euler(0f, pc.Head.eulerAngles.y, 0f)) * (p - pc.Head.position);
        }

        static Vector3 SlotPoint(Dressed me, int s)
        {
            return s == 0 || me.Slots[s] == null || NoRenderers(me.Slots[s]) ? Body.Anchor(me.Bones, s) : Center(me.Slots[s].transform);
        }

        static int PieceOf(Pickup p)
        {
            if (p == null || p.Entity == null || p.Entity.Prefab == null) return -1;
            uint h = p.Entity.Prefab.Hash;
            return Prefabs.IsArmor(h) ? Catalog.PieceOf(h) : -1;
        }

        static void LetGo(Dressed me, Pickup was)
        {
            if (was == null || was.Entity == null) return;
            int piece = PieceOf(was);
            if (piece < 0) return;
            int slot = piece % Catalog.SlotCount;
            float d = Vector3.Distance(Center(was.transform), Body.Anchor(me.Bones, slot));
            if (d > Reach(slot))
            {
                if (Settings.Debug) Mod.Log.Msg("let go of " + Catalog.Title(piece) + " " + d.ToString("0.00") + " m from its place (needs " + Reach(slot).ToString("0.0") + ")");
                return;
            }
            Net.SendEquip(was.Entity.Identifier);
            Sound(true);
            Mod.Log.Msg("put on " + Catalog.Title(piece) + " (" + d.ToString("0.00") + " m from its place)");
        }

        static Vector3 Center(Transform t)
        {
            Renderer[] rs = t.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return t.position;
            Bounds b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            return b.center;
        }

        static int TouchedSlot(Dressed me, int[] worn, Vector3 hand, int handIndex)
        {
            int best = -1;
            float bestD = 0.2f;
            for (int s = 0; s < Catalog.SlotCount; s++)
            {
                if (worn[s] < 0) continue;
                int rule = Catalog.Slots[s].RemovedBy;
                if (rule == 1 && handIndex != 0 || rule == 2 && handIndex != 1) continue;
                float d = Vector3.Distance(hand, SlotPoint(me, s));
                if (d < bestD) { bestD = d; best = s; }
            }
            return best;
        }

        static bool NoRenderers(GameObject g)
        {
            return g.GetComponentsInChildren<Renderer>().Length == 0;
        }

        static void GrabTaken()
        {
            if (Time.unscaledTime > _tookUntil) { _tookId = 0; return; }
            NetworkEntity e = NetworkSceneManager.Current != null ? NetworkSceneManager.Current.GetEntity(_tookId) : null;
            if (e == null) return;
            Pickup p = e.GetComponent<Pickup>();
            Interactor it = HandInteractor(_tookHand);
            _tookId = 0;
            if (p == null || it == null) return;
            try { it.SwapInteract(p, true); }
            catch (Exception ex) { Mod.Log.Warning("could not put the piece in the hand: " + ex.Message); }
        }

        static void Sound(bool on)
        {
            try
            {
                if (!_soundsLooked)
                {
                    _soundsLooked = true;
                    FieldInfo f = typeof(SoundEffectSet).GetField("soundEffectType", BindingFlags.Instance | BindingFlags.NonPublic);
                    foreach (SoundEffectSet s in Resources.FindObjectsOfTypeAll<SoundEffectSet>())
                    {
                        string t = f != null ? Convert.ToString(f.GetValue(s)) : s.name;
                        if (t == "EquipmentApplied") _soundOn = s;
                        if (t == "EquipmentTakenOff") _soundOff = s;
                    }
                    Mod.Log.Msg("equipment sounds: " + (_soundOn != null ? "on" : "-") + "/" + (_soundOff != null ? "off" : "-"));
                }
                SoundEffectSet set = (on ? _soundOn : _soundOff) as SoundEffectSet;
                PlayerController pc = PlayerController.Current;
                if (set == null || pc == null || SingletonBehaviour<AudioManager>.Instance == null) return;
                GameObject target = pc.Head != null ? pc.Head.gameObject : pc.gameObject;
                SingletonBehaviour<AudioManager>.Instance.PlaySoundEffectOnObject(set, target, target);
            }
            catch (Exception e)
            {
                Mod.Log.Warning("sound: " + e.Message);
            }
        }
    }
}
