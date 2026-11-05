using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Alta.Chunks;
using Alta.Impact;
using Alta.Networking;
using Alta.Networking.Scripts.Player;
using UnityEngine;

namespace ArmorRestored
{
    internal static class ServerArmor
    {
        const float EquipReach = 2.0f;

        static readonly Dictionary<string, int[]> Worn = new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<int, Wiring> Wired = new Dictionary<int, Wiring>();
        static readonly HashSet<string> Logged = new HashSet<string>();
        static float _next;
        static bool _dirty;

        static string File { get { return Path.Combine(Mod.DataDir, "worn.txt"); } }

        internal static void Load()
        {
            if (!System.IO.File.Exists(File)) return;
            foreach (string line in System.IO.File.ReadAllLines(File))
            {
                string[] parts = line.Split('\t');
                if (parts.Length != 2 || parts[0].Length == 0) continue;
                string[] ids = parts[1].Split(',');
                int[] worn = Empty();
                for (int i = 0; i < ids.Length && i < Catalog.SlotCount; i++) worn[i] = PieceById(ids[i]);
                Worn[parts[0]] = worn;
            }
            Mod.Log.Msg("worn armor loaded for " + Worn.Count + " player(s)");
        }

        static void Save()
        {
            StringBuilder sb = new StringBuilder();
            foreach (KeyValuePair<string, int[]> kv in Worn)
            {
                bool any = false;
                foreach (int p in kv.Value) if (p >= 0) any = true;
                if (!any) continue;
                sb.Append(kv.Key).Append('\t');
                for (int i = 0; i < Catalog.SlotCount; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append(kv.Value[i] >= 0 ? Catalog.Id(kv.Value[i]) : "-");
                }
                sb.Append('\n');
            }
            string tmp = File + ".tmp";
            System.IO.File.WriteAllText(tmp, sb.ToString());
            if (System.IO.File.Exists(File)) System.IO.File.Delete(File);
            System.IO.File.Move(tmp, File);
        }

        static int PieceById(string id)
        {
            for (int i = 0; i < Catalog.Count; i++) if (Catalog.Id(i) == id) return i;
            return -1;
        }

        internal static int[] Empty()
        {
            int[] a = new int[Catalog.SlotCount];
            for (int i = 0; i < a.Length; i++) a[i] = -1;
            return a;
        }

        internal static int[] WornBy(Player p)
        {
            string key = Players.Name(p);
            int[] w;
            if (!Worn.TryGetValue(key, out w)) Worn[key] = w = Empty();
            return w;
        }

        internal static Wiring WiringOf(Player p)
        {
            if (p == null || p.PlayerCharacter == null) return null;
            Wiring w;
            return Wired.TryGetValue(p.PlayerCharacter.GetInstanceID(), out w) ? w : null;
        }

        internal static void Tick()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.5f;
            if (!Prefabs.Done) return;

            List<int> gone = new List<int>();
            foreach (KeyValuePair<int, Wiring> kv in Wired) if (kv.Value.Root == null) gone.Add(kv.Key);
            foreach (int k in gone) Wired.Remove(k);

            foreach (IPlayer ip in Player.AllPlayers)
            {
                Player p = ip as Player;
                if (p == null || p.UserInfo == null || p.PlayerCharacter == null) continue;
                int id = p.PlayerCharacter.GetInstanceID();
                if (Wired.ContainsKey(id)) continue;
                Wiring w = Body.Wire(p.PlayerCharacter.transform, true);
                Wired[id] = w;
                Body.Apply(w, WornBy(p));
                HealthObject health = Body.Health(w);
                if (health != null) health.HealthChanged += delegate(HealthObject h, float change, DamageData data) { Hurt(w, h, change, data); };
                string line = Players.Name(p) + ": " + Body.Describe(w);
                if (Logged.Add(line)) Mod.Log.Msg("wired " + line);
                _dirty = true;
            }
            if (_dirty)
            {
                _dirty = false;
                Broadcast();
            }
        }

        static void Hurt(Wiring w, HealthObject h, float change, DamageData data)
        {
            if (!Settings.Debug || change >= 0f) return;
            PlayerHealth ph = h as PlayerHealth;
            string who = ph != null && ph.Player != null && ph.Player.NetworkPlayer != null ? Players.Name(ph.Player.NetworkPlayer) : h.name;
            ImpactHit hit = data as ImpactHit;
            string what = data == null ? "no hit" : data.GetType().Name;
            if (hit != null)
            {
                ArmorDefinition a = hit.ImpactDetector != null ? hit.ImpactDetector.CurrentArmor : null;
                what = hit.ImpactType + " " + hit.HealthDamage.ToString("0.000") + " on " + Body.ZoneOf(w, hit.ImpactDetector)
                    + ", armor " + (a == null ? "none" : a.name + " x" + a.GetMultiplier(hit.ImpactType).ToString("0.00"))
                    + (hit.Wielder != null ? ", from " + hit.Wielder.name : "");
            }
            Mod.Log.Msg(who + " lost " + (-change).ToString("0.000") + " health (" + what + "), now " + h.Health.ToString("0.00"));
        }

        static void Broadcast()
        {
            List<KeyValuePair<int, int[]>> rows = new List<KeyValuePair<int, int[]>>();
            List<Player> all = new List<Player>();
            foreach (IPlayer ip in Player.AllPlayers)
            {
                Player p = ip as Player;
                if (p == null || p.UserInfo == null) continue;
                all.Add(p);
                rows.Add(new KeyValuePair<int, int[]>(p.UserInfo.Identifier, WornBy(p)));
            }
            foreach (Player p in all) Net.SendTable(p, rows);
        }

        static void Changed(Player p)
        {
            Save();
            Body.Apply(WiringOf(p), WornBy(p));
            _dirty = true;
        }

        internal static void OnEquip(Player p, uint entityId)
        {
            NetworkEntity e = NetworkSceneManager.Current != null ? NetworkSceneManager.Current.GetEntity(entityId) : null;
            if (e == null || e.Prefab == null || !Prefabs.IsArmor(e.Prefab.Hash)) { Net.SendNote(p, "equip " + entityId + ": not an armor piece"); return; }
            int piece = Catalog.PieceOf(e.Prefab.Hash);
            Vector3 at = e.transform.position;
            Vector3 head = Head(p);
            if ((at - head).magnitude > EquipReach) { Net.SendNote(p, "equip: " + Catalog.Title(piece) + " is " + (at - head).magnitude.ToString("0.0") + " m away"); return; }
            int slot = piece % Catalog.SlotCount;
            int[] worn = WornBy(p);
            int old = worn[slot];
            e.SceneDestroy();
            if (old >= 0) Spawn(old, at, Quaternion.identity);
            worn[slot] = piece;
            Mod.Log.Msg(Players.Name(p) + " put on " + Catalog.Title(piece) + (old >= 0 ? " (dropped " + Catalog.Title(old) + ")" : ""));
            Changed(p);
        }

        internal static void OnUnequip(Player p, int slot, int hand)
        {
            if (slot < 0 || slot >= Catalog.SlotCount) return;
            int[] worn = WornBy(p);
            int piece = worn[slot];
            if (piece < 0) return;
            int rule = Catalog.Slots[slot].RemovedBy;
            if (rule == 1 && hand != 0 || rule == 2 && hand != 1) { Net.SendNote(p, "the " + Catalog.Slots[slot].Title + " comes off with the other hand"); return; }
            Vector3 at = Hand(p, hand);
            NetworkEntity spawned = Spawn(piece, at, Quaternion.identity);
            if (spawned == null) { Net.SendNote(p, "could not spawn " + Catalog.Title(piece)); return; }
            worn[slot] = -1;
            Mod.Log.Msg(Players.Name(p) + " took off " + Catalog.Title(piece));
            Changed(p);
            Net.SendTook(p, spawned.Identifier, hand);
        }

        internal static NetworkEntity Spawn(int piece, Vector3 at, Quaternion rot)
        {
            NetworkPrefab np = Prefabs.ByPiece[piece];
            if (np == null) return null;
            NetworkEntity e = SpawnHelper.Spawn(np, SpawnData.Default, (Chunk)null, at, rot);
            if (e != null) e.Chunk = LocationChunkHelper.GetBestContaining(at);
            return e;
        }

        internal static void Wear(Player p, int piece)
        {
            WornBy(p)[piece % Catalog.SlotCount] = piece;
            Changed(p);
        }

        internal static int Strip(Player p, bool drop)
        {
            int[] worn = WornBy(p);
            int n = 0;
            Vector3 at = Head(p) + Front(p) * 0.6f + Vector3.down * 0.6f;
            for (int i = 0; i < worn.Length; i++)
            {
                if (worn[i] < 0) continue;
                if (drop) Spawn(worn[i], at + Vector3.up * 0.15f * n, Quaternion.identity);
                worn[i] = -1;
                n++;
            }
            if (n > 0) Changed(p);
            return n;
        }

        internal static string Status(Player p)
        {
            int[] worn = WornBy(p);
            List<string> on = new List<string>();
            for (int i = 0; i < worn.Length; i++) if (worn[i] >= 0) on.Add(Catalog.Title(worn[i]));
            return Players.Name(p) + " wears: " + (on.Count == 0 ? "nothing" : string.Join(", ", on.ToArray())) + "\n" + Body.Describe(WiringOf(p));
        }

        internal static Vector3 Head(Player p)
        {
            if (p.PlayerController != null && p.PlayerController.Head != null) return p.PlayerController.Head.position;
            return p.PlayerCharacter != null ? p.PlayerCharacter.transform.position + Vector3.up * 1.6f : Vector3.zero;
        }

        internal static Vector3 Front(Player p)
        {
            Vector3 f = p.PlayerController != null && p.PlayerController.Head != null ? p.PlayerController.Head.forward : Vector3.forward;
            f.y = 0f;
            return f.sqrMagnitude > 1e-4f ? f.normalized : Vector3.forward;
        }

        static Vector3 Hand(Player p, int hand)
        {
            PlayerController pc = p.PlayerController;
            Controller c = pc == null ? null : hand == 0 ? pc.LeftController : pc.RightController;
            if (c != null) return c.transform.position;
            return Head(p) + Front(p) * 0.4f + Vector3.down * 0.3f;
        }
    }
}
