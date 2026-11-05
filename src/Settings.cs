using System;
using System.Collections.Generic;
using System.IO;

namespace ArmorRestored
{
    internal static class Settings
    {
        internal static string Join = "refuse";
        internal static bool Loot = true;
        internal static bool WyrmLoot;
        internal static bool OwnHelmet;
        internal static bool Debug;

        static readonly string[] Lines =
        {
            "# own_helmet = hide | show     (client) draw your own helmet; hidden by default so it never blocks a headset view",
            "# join = refuse | log | off   what the server does when a player joins without an ArmorRestored that plays with this one",
            "# loot = on | off              leather (Rare) and metal (Epic) armor in the global loot table, as in 0.0.32",
            "# wyrm_loot = on | off         also put Wyrm armor in the loot table (Epic); 0.0.32 never dropped it",
            "# debug = off | on             client: log every armor grab, let go and near miss; server: log every hit a player takes, with its armor",
        };

        internal static void Load()
        {
            string file = Path.Combine(Mod.DataDir, "settings.txt");
            Dictionary<string, string> found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (File.Exists(file))
            {
                foreach (string raw in File.ReadAllLines(file))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line[0] == '#') continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    found[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                }
            }
            string v;
            if (found.TryGetValue("join", out v) && (v == "refuse" || v == "log" || v == "off")) Join = v;
            if (found.TryGetValue("loot", out v)) Loot = v != "off";
            if (found.TryGetValue("wyrm_loot", out v)) WyrmLoot = v == "on";
            if (found.TryGetValue("own_helmet", out v)) OwnHelmet = v == "show";
            if (found.TryGetValue("debug", out v)) Debug = v == "on";
            if (found.ContainsKey("join") && found.ContainsKey("loot") && found.ContainsKey("wyrm_loot") && found.ContainsKey("own_helmet") && found.ContainsKey("debug")) return;
            List<string> outLines = new List<string>(Lines);
            outLines.Add("join = " + Join);
            outLines.Add("loot = " + (Loot ? "on" : "off"));
            outLines.Add("wyrm_loot = " + (WyrmLoot ? "on" : "off"));
            outLines.Add("own_helmet = " + (OwnHelmet ? "show" : "hide"));
            outLines.Add("debug = " + (Debug ? "on" : "off"));
            File.WriteAllLines(file, outLines.ToArray());
        }
    }
}
