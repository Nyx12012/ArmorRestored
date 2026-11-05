using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Alta.Console;
using Alta.Networking.Scripts.Player;
using UnityEngine;

namespace ArmorRestored
{
    [Module("armor", "ArmorRestored: the 0.0.32 leather, metal and Wyrm armor")]
    public static class ArmorCommands
    {
        [Command("list", "Every armor piece, its number and whether it registered; the loot line")]
        public static string List()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("ArmorRestored ").Append(Mod.Version).Append(", registered via ").Append(Prefabs.Seam ?? "nothing yet").Append('\n');
            for (int i = 0; i < Catalog.Count; i++)
            {
                sb.Append(Catalog.Number(i)).Append("  ").Append(Catalog.Id(i)).Append("  ").Append(Catalog.Title(i));
                if (Prefabs.Refused[i] != null) sb.Append("  REFUSED ").Append(Prefabs.Refused[i]);
                else if (Prefabs.ByPiece[i] == null) sb.Append("  not registered");
                sb.Append('\n');
            }
            sb.Append("loot: ").Append(Loot.Report);
            return sb.ToString();
        }

        [Command("give", "Spawn armor in front of a player: armor give <player> <leather|metal|wyrm> <slot|all> (slots: helmet chest shoulder_l shoulder_r gauntlet_l gauntlet_r tasset_l tasset_r)")]
        public static string Give(string player, string set, string slot)
        {
            return Run(delegate
            {
                string why;
                Player p = Players.Find(player, out why);
                if (p == null) return why;
                List<int> pieces;
                if (!Pieces(set, slot, out pieces, out why)) return why;
                Vector3 at = ServerArmor.Head(p) + ServerArmor.Front(p) * 0.6f + Vector3.down * 0.5f;
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < pieces.Count; i++)
                {
                    Alta.Networking.NetworkEntity e = ServerArmor.Spawn(pieces[i], at + Vector3.up * 0.12f * i, Quaternion.identity);
                    sb.Append(e != null ? "spawned " : "could not spawn ").Append(Catalog.Title(pieces[i])).Append(" for ").Append(Players.Name(p));
                    if (e != null) sb.Append(" as ").Append(e.Identifier).Append(" at ").Append(e.transform.position.ToString("F2"));
                    sb.Append('\n');
                }
                return sb.ToString().TrimEnd('\n');
            });
        }

        [Command("wear", "Put armor straight on a player: armor wear <player> <leather|metal|wyrm> <slot|all>")]
        public static string Wear(string player, string set, string slot)
        {
            return Run(delegate
            {
                string why;
                Player p = Players.Find(player, out why);
                if (p == null) return why;
                List<int> pieces;
                if (!Pieces(set, slot, out pieces, out why)) return why;
                foreach (int piece in pieces) ServerArmor.Wear(p, piece);
                return ServerArmor.Status(p);
            });
        }

        [Command("strip", "Take off everything a player wears and drop it in front of them: armor strip <player>")]
        public static string Strip(string player)
        {
            return Run(delegate
            {
                string why;
                Player p = Players.Find(player, out why);
                if (p == null) return why;
                return Players.Name(p) + ": " + ServerArmor.Strip(p, true) + " piece(s) dropped";
            });
        }

        [Command("status", "What a player wears and which hit zones carry armor now: armor status <player>")]
        public static string Status(string player)
        {
            return Run(delegate
            {
                string why;
                Player p = Players.Find(player, out why);
                if (p == null) return why;
                return ServerArmor.Status(p);
            });
        }

        [Command("testhit", "Hit one of a player's own hit zones through the game's damage code, report the damage, then give the health back: armor testhit <player> <head|chest|hips|leftshoulder|rightshoulder|leftarm|rightarm> <pierce|slash|blunt|caustic|explosive> <damage>")]
        public static string TestHit(string player, string zone, string type, float damage)
        {
            return Run(delegate
            {
                string why;
                Player p = Players.Find(player, out why);
                if (p == null) return why;
                Wiring w = ServerArmor.WiringOf(p);
                if (w == null) return Players.Name(p) + " is not wired yet";
                int z = Array.FindIndex(Body.ZoneNames, n => string.Equals(n, zone, StringComparison.OrdinalIgnoreCase));
                if (z < 0) return "no zone '" + zone + "'";
                Alta.Impact.ImpactType t;
                try { t = (Alta.Impact.ImpactType)Enum.Parse(typeof(Alta.Impact.ImpactType), type, true); }
                catch (Exception) { return "no damage type '" + type + "'"; }
                if (w.ZoneDetectors[z] == null || w.ZoneDetectors[z].Count == 0) return "zone " + zone + " has no hit zone on this body";
                Alta.Impact.ImpactDetector d = w.ZoneDetectors[z][0];
                HealthObject h = d.HealthTarget;
                if (h == null) return "that hit zone has no health target";
                if (damage <= 0f || damage >= h.Health * 0.5f) return "damage must be above 0 and below half of " + Players.Name(p) + "'s health (" + h.Health.ToString("0.00") + ")";
                Collider c = d.GetComponent<Collider>();
                Alta.Impact.ImpactHit hit = new Alta.Impact.ImpactHit(damage, 0f, 1f, 1f, d, t, d.transform.position, Vector3.up, Vector3.down, c, null, null);
                if (!hit.IsValid) return "the game calls this hit invalid";
                string gates = Gates(h, d);
                float before = h.Health;
                d.OnImpactHit(hit);
                float after = h.Health;
                h.Health = before;
                Alta.Impact.ArmorDefinition a = d.CurrentArmor;
                return Players.Name(p) + " " + Body.ZoneNames[z] + " (" + d.name + ") " + t + " " + damage + ": health " + before.ToString("0.00") + " -> " + after.ToString("0.00")
                    + " = " + (before - after).ToString("0.00") + " taken; armor " + (a == null ? "none" : a.name + " x" + a.GetMultiplier(t).ToString("0.00")) + "; health restored to " + h.Health.ToString("0.00")
                    + "\n" + gates;
            });
        }

        const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        static string Gates(HealthObject h, Alta.Impact.ImpactDetector d)
        {
            StringBuilder sb = new StringBuilder("gates:");
            IList listed = typeof(HealthObject).GetField("impactDetectors", Inst).GetValue(h) as IList;
            sb.Append(" listed=").Append(listed != null && listed.Contains(d));
            Delegate hit = typeof(Alta.Impact.ImpactDetector).GetField("Hit", Inst).GetValue(d) as Delegate;
            List<string> subs = new List<string>();
            if (hit != null) foreach (Delegate s in hit.GetInvocationList()) subs.Add(s.Method.DeclaringType.Name + "." + s.Method.Name);
            sb.Append(" hit_subs=[").Append(string.Join(",", subs.ToArray())).Append(']');
            Alta.StatSystem.Stat hs = typeof(HealthObject).GetProperty("HealthStat", Inst).GetValue(h, null) as Alta.StatSystem.Stat;
            if (hs != null) sb.Append(" health base=").Append(hs.Base.ToString("0.00")).Append(" min=").Append(hs.Minimum.ToString("0.00")).Append(" max=").Append(hs.Maximum.ToString("0.00")).Append(" extra=").Append(hs.Extra.ToString("0.00"));
            if ((bool)typeof(HealthObject).GetField("hasDamageProtection", Inst).GetValue(h))
            {
                Alta.StatSystem.Stat dp = typeof(HealthObject).GetField("damageProtectionStat", Inst).GetValue(h) as Alta.StatSystem.Stat;
                sb.Append(" protection=").Append(dp != null ? dp.Value.ToString("0.00") : "?");
            }
            PlayerHealth ph = h as PlayerHealth;
            if (ph == null) return sb.Append(" not PlayerHealth (").Append(h.GetType().Name).Append(')').ToString();
            sb.Append(" alive=").Append(ph.Player.IsAlive).Append(" ignoring=").Append(ph.Player.IsIgnoringDamage);
            sb.Append(" invincible=").Append(typeof(PlayerHealth).GetField("isInvincibleToImpacts", Inst).GetValue(ph));
            IPlayerControllerMultiLockModel locks = typeof(PlayerHealth).GetField("playerControllerMultiLockModel", Inst).GetValue(ph) as IPlayerControllerMultiLockModel;
            sb.Append(" locked=").Append(locks == null ? "null" : locks.IsLocked.ToString());
            sb.Append(" immortals=").Append(PlayerHealth.ArePlayersAvoidingDamage.Value);
            Alta.Utilities.ServerSettings ss = Alta.Networking.Servers.ServerHandler.Current.ServerConfig.Settings;
            sb.Append(" taken_x=").Append(ss.PlayerDamageTakenMultiplier.ToString("0.00"));
            foreach (AttackTypeMultiplier m in h.Multipliers) sb.Append(' ').Append(m.Type).Append("_x=").Append(m.Multiplier.ToString("0.00"));
            return sb.ToString();
        }

        [Command("debug", "Log every hit a player takes, with its armor, until the server restarts: armor debug <on|off>")]
        public static string DebugCmd(string state)
        {
            return Run(delegate
            {
                Settings.Debug = string.Equals(state, "on", StringComparison.OrdinalIgnoreCase);
                return "hit log " + (Settings.Debug ? "on" : "off") + " until the server restarts (settings.txt debug = on keeps it)";
            });
        }

        [Command("loot","Roll the game's own loot code on every loot dock in the world and count the armor: armor loot <rolls per dock>")]
        public static string LootCheck(int rolls)
        {
            return Run(delegate { return Loot.Check(Math.Max(1, Math.Min(rolls, 1000))); });
        }

        static bool Pieces(string set, string slot, out List<int> pieces, out string why)
        {
            pieces = new List<int>();
            why = null;
            int s = Catalog.FindSet(set);
            if (s < 0) { why = "no armor set '" + set + "' (leather, metal, wyrm)"; return false; }
            if (string.Equals(slot, "all", StringComparison.OrdinalIgnoreCase))
            {
                for (int i = 0; i < Catalog.SlotCount; i++) pieces.Add(Catalog.Piece(s, i));
            }
            else
            {
                int sl = Catalog.FindSlot(slot);
                if (sl < 0) { why = "no slot '" + slot + "' (helmet chest shoulder_l shoulder_r gauntlet_l gauntlet_r tasset_l tasset_r, or all)"; return false; }
                pieces.Add(Catalog.Piece(s, sl));
            }
            foreach (int p in pieces)
                if (Prefabs.ByPiece[p] == null) { why = Catalog.Title(p) + " is not registered: " + (Prefabs.Refused[p] ?? "not yet"); return false; }
            return true;
        }

        static string Run(Func<string> f)
        {
            if (!Mod.IsServer) return "server only";
            try { return f(); }
            catch (Exception e)
            {
                Mod.Log.Error("armor command: " + e);
                return "error: " + e.Message;
            }
        }
    }
}
