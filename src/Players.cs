using System;
using Alta.Networking.Scripts.Player;

namespace ArmorRestored
{
    internal static class Players
    {
        internal static Player Find(string who, out string why)
        {
            why = null;
            string w = (who ?? "").Trim();
            if (w.Length == 0) { why = "name a player"; return null; }
            uint id;
            bool numeric = uint.TryParse(w, out id);
            foreach (IPlayer ip in Player.AllPlayers)
            {
                Player p = ip as Player;
                if (p == null || p.UserInfo == null) continue;
                if (numeric && (uint)p.UserInfo.Identifier == id) return p;
                if (string.Equals(p.UserInfo.Username, w, StringComparison.OrdinalIgnoreCase)) return p;
            }
            why = "no connected player called " + w;
            return null;
        }

        internal static string Name(Player p)
        {
            if (p == null) return "?";
            return p.UserInfo == null ? "player " + p.Identifier : p.UserInfo.Username;
        }
    }
}
