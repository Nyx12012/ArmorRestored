using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Alta.Networking;
using Alta.Networking.Scripts.Player;
using Alta.Networking.Servers;
using Alta.Serialization;
using HarmonyLib;

namespace ArmorRestored
{
    internal static class Net
    {
        internal const MessageType Message = (MessageType)137;
        internal const byte OldHello = 1, Equip = 2, Unequip = 3, Ack = 4, Hello = 5;
        internal const byte Table = 10, Took = 11, Note = 12, Here = 20;
        internal const int Wire = 1;

        internal static Connection ToServer;

        internal static void SendHere(Connection c)
        {
            c.Send(null, Message, delegate (Connection conn, Stream s)
            {
                byte k = Here;
                s.SerializeByte(ref k);
                string version = Mod.Version;
                s.SerializeString(ref version, Stream.StringEncoding.UTF8);
                int wire = Wire;
                s.SerializeInteger(ref wire);
            });
        }

        internal static void SendHello(Connection c)
        {
            c.Send(null, Message, delegate (Connection conn, Stream s)
            {
                byte k = Hello;
                s.SerializeByte(ref k);
                int wire = Wire;
                s.SerializeInteger(ref wire);
                string version = Mod.Version, refused = Prefabs.RefusedLine();
                s.SerializeString(ref version, Stream.StringEncoding.UTF8);
                s.SerializeString(ref refused, Stream.StringEncoding.UTF8);
            });
        }

        internal static void SendAck(Connection c)
        {
            c.Send(null, Message, delegate (Connection conn, Stream s)
            {
                byte k = Ack;
                s.SerializeByte(ref k);
            });
        }

        internal static void SendEquip(uint entity)
        {
            Connection c = ToServer;
            if (c == null || c.IsDisposed) return;
            c.Send(null, Message, delegate (Connection conn, Stream s)
            {
                byte k = Equip;
                s.SerializeByte(ref k);
                uint id = entity;
                s.SerializeBits(ref id, 32);
            });
        }

        internal static void SendUnequip(int slot, int hand)
        {
            Connection c = ToServer;
            if (c == null || c.IsDisposed) return;
            c.Send(null, Message, delegate (Connection conn, Stream s)
            {
                byte k = Unequip, sl = (byte)slot, h = (byte)hand;
                s.SerializeByte(ref k);
                s.SerializeByte(ref sl);
                s.SerializeByte(ref h);
            });
        }

        internal static void ServerRead(Connection connection, Stream s)
        {
            byte kind = 0;
            s.SerializeByte(ref kind);
            if (kind == Hello || kind == OldHello)
            {
                int wire = 1;
                if (kind == Hello) s.SerializeInteger(ref wire);
                string version = null, refused = null;
                s.SerializeString(ref version, Stream.StringEncoding.UTF8);
                s.SerializeString(ref refused, Stream.StringEncoding.UTF8);
                lock (Hellos) Hellos[connection.Identifier] = new Greeting { Wire = wire, Version = version ?? "", Refused = refused ?? "" };
                return;
            }
            if (kind == Ack)
            {
                lock (Hellos) Acks.Add(connection.Identifier);
                return;
            }
            Player p = connection.Player as Player;
            if (kind == Equip)
            {
                uint id = 0;
                s.SerializeBits(ref id, 32);
                if (p != null) ServerArmor.OnEquip(p, id);
            }
            else if (kind == Unequip)
            {
                byte slot = 0, hand = 0;
                s.SerializeByte(ref slot);
                s.SerializeByte(ref hand);
                if (p != null) ServerArmor.OnUnequip(p, slot, hand);
            }
        }

        internal static void SendTable(Player to, List<KeyValuePair<int, int[]>> rows)
        {
            if (to == null || to.ConnectionToRemotePlayer == null) return;
            to.ConnectionToRemotePlayer.Send(null, Message, delegate (Connection conn, Stream s)
            {
                byte k = Table;
                s.SerializeByte(ref k);
                int n = rows.Count;
                s.SerializeInteger(ref n);
                foreach (KeyValuePair<int, int[]> row in rows)
                {
                    int user = row.Key;
                    s.SerializeInteger(ref user);
                    for (int i = 0; i < Catalog.SlotCount; i++)
                    {
                        byte b = (byte)(row.Value[i] + 1);
                        s.SerializeByte(ref b);
                    }
                }
            });
        }

        internal static void SendTook(Player to, uint entity, int hand)
        {
            if (to == null || to.ConnectionToRemotePlayer == null) return;
            to.ConnectionToRemotePlayer.Send(null, Message, delegate (Connection conn, Stream s)
            {
                byte k = Took, h = (byte)hand;
                s.SerializeByte(ref k);
                uint id = entity;
                s.SerializeBits(ref id, 32);
                s.SerializeByte(ref h);
            });
        }

        internal static void SendNote(Player to, string text)
        {
            if (to == null || to.ConnectionToRemotePlayer == null) return;
            to.ConnectionToRemotePlayer.Send(null, Message, delegate (Connection conn, Stream s)
            {
                byte k = Note;
                s.SerializeByte(ref k);
                string t = text;
                s.SerializeString(ref t, Stream.StringEncoding.UTF8);
            });
        }

        internal static void ClientRead(Connection connection, Stream s)
        {
            byte kind = 0;
            s.SerializeByte(ref kind);
            if (kind == Here)
            {
                string version = null;
                int wire = 0;
                s.SerializeString(ref version, Stream.StringEncoding.UTF8);
                s.SerializeInteger(ref wire);
                Gate.Open(connection, version, wire);
                return;
            }
            if (!Gate.IsOpen) return;
            if (kind == Table)
            {
                int n = 0;
                s.SerializeInteger(ref n);
                Dictionary<int, int[]> rows = new Dictionary<int, int[]>();
                for (int i = 0; i < n && i < 1024; i++)
                {
                    int user = 0;
                    s.SerializeInteger(ref user);
                    int[] worn = new int[Catalog.SlotCount];
                    for (int k = 0; k < Catalog.SlotCount; k++)
                    {
                        byte b = 0;
                        s.SerializeByte(ref b);
                        worn[k] = b == 0 || b > Catalog.Count ? -1 : b - 1;
                    }
                    rows[user] = worn;
                }
                ClientArmor.OnTable(rows);
            }
            else if (kind == Took)
            {
                uint id = 0;
                byte hand = 0;
                s.SerializeBits(ref id, 32);
                s.SerializeByte(ref hand);
                ClientArmor.OnTook(id, hand);
            }
            else if (kind == Note)
            {
                string t = null;
                s.SerializeString(ref t, Stream.StringEncoding.UTF8);
                Mod.Log.Msg("server: " + t);
            }
        }

        sealed class Greeting
        {
            public int Wire;
            public string Version, Refused;
        }

        static readonly Dictionary<int, Greeting> Hellos = new Dictionary<int, Greeting>();
        static readonly HashSet<int> Acks = new HashSet<int>();
        const int AnswerWait = 5000, LoadWait = 60000;
        static bool _stepInstalled;

        internal static void InstallJoinStep()
        {
            if (_stepInstalled) return;
            ServerHandler server = ServerHandler.Current;
            if (server == null) { Mod.Log.Warning("join step not installed: no ServerHandler yet, so no player gets armor"); return; }
            _stepInstalled = true;
            server.AddConnectionStep(Step);
            Mod.Log.Msg("join step installed on message " + (int)Message + ": every joining player hears that armor is here; join check " + Settings.Join);
        }

        static async Task Step(Connection connection, Action<string> progress)
        {
            SendHere(connection);
            if (Settings.Join == "off") return;
            Greeting hello = null;
            bool acked = false;
            int waited = 0;
            while (!connection.IsDisposed)
            {
                lock (Hellos)
                {
                    Hellos.TryGetValue(connection.Identifier, out hello);
                    acked = acked || Acks.Contains(connection.Identifier);
                }
                if (hello != null || waited >= (acked ? LoadWait : AnswerWait)) break;
                await Task.Delay(100);
                waited += 100;
            }
            lock (Hellos)
            {
                Hellos.Remove(connection.Identifier);
                Acks.Remove(connection.Identifier);
            }
            string problem = null;
            if (hello == null && acked) problem = "Your ArmorRestored did not finish loading in " + (LoadWait / 1000) + " s. Join again.";
            else if (hello == null) problem = "This server needs the ArmorRestored mod " + Mod.Version + ". Install it, then join again.";
            else if (hello.Wire < Wire) problem = "Your ArmorRestored " + hello.Version + " is too old for this server's " + Mod.Version + ". Update it, then join again.";
            else if (hello.Wire > Wire) problem = "Your ArmorRestored " + hello.Version + " is newer than this server's " + Mod.Version + ". The server needs an update first.";
            else if (hello.Refused != Prefabs.RefusedLine()) problem = "ArmorRestored loaded differently here (server refused: " + Or(Prefabs.RefusedLine()) + "; you refused: " + Or(hello.Refused) + "). Check both logs.";
            string who = connection.PlayerName + " (connection " + connection.Identifier + ")";
            if (problem == null)
            {
                Mod.Log.Msg("join check passed for " + who + " after " + waited + " ms (client " + hello.Version + ", wire " + hello.Wire + ")");
                return;
            }
            Mod.Log.Warning("join check: " + who + ": " + problem + (Settings.Join == "refuse" ? " -> refused" : " (log only)"));
            if (Settings.Join != "refuse") return;
            progress(Ascii(problem));
            await Task.Delay(1500);
            connection.Send(null, (MessageType)10, delegate (Connection c, Stream s)
            {
                bool allowed = false, prerequisites = false;
                int code = 0;
                string text = Ascii(problem);
                s.SerializeBool(ref allowed);
                s.SerializeBool(ref prerequisites);
                s.SerializeInteger(ref code);
                s.SerializeString(ref text);
            });
            await Task.Delay(500);
            throw new InvalidOperationException("ArmorRestored refused the join: " + problem);
        }

        static string Or(string s) { return string.IsNullOrEmpty(s) ? "none" : s; }

        static string Ascii(string s)
        {
            StringBuilder b = new StringBuilder(s.Length);
            foreach (char c in s) b.Append(c < 128 ? c : '?');
            return b.ToString();
        }
    }

    [HarmonyPatch(typeof(ServerHandler), "ConnectionCreated")]
    static class ServerConnectionSeam
    {
        static void Postfix(Connection connection)
        {
            if (connection == null || !Mod.IsServer) return;
            try { connection.SetHandler(Net.Message, Net.ServerRead); }
            catch (Exception e) { Mod.Log.Error("server handler: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(JoinServerPipeline), "ConnectionApprovedByServer")]
    static class ClientConnectionSeam
    {
        static void Postfix(Connection connection)
        {
            if (Mod.IsServer || connection == null) return;
            try { Gate.Listen(connection); }
            catch (Exception e) { Mod.Log.Error("client handler: " + e.Message); }
        }
    }
}
