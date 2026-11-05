using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Alta.Inventory;
using Alta.Loot;
using Alta.Networking;
using UnityEngine;

namespace ArmorRestored
{
    internal static class Loot
    {
        const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        internal static string Report = "not added";

        internal static void Add()
        {
            FieldInfo listEntries = typeof(LootTable.RarityList).GetField("entries", Inst);
            FieldInfo entryItem = typeof(LootTable.Entry).GetField("item", Inst);
            if (listEntries == null || entryItem == null)
            {
                Report = "LootTable.RarityList.entries or LootTable.Entry.item moved";
                Mod.Log.Error("loot: " + Report);
                return;
            }
            LootTable global = null;
            List<string> names = new List<string>();
            foreach (LootTable t in Resources.LoadAll<LootTable>("Items/Loot Tables/"))
            {
                names.Add(t.name);
                if (t.name == "[Global]") global = t;
            }
            if (global == null)
            {
                Report = "no [Global] loot table (found: " + string.Join(", ", names.ToArray()) + ")";
                Mod.Log.Warning("loot: " + Report);
                return;
            }
            List<string> done = new List<string>();
            for (int s = 0; s < Catalog.Sets.Length; s++)
            {
                string rarity = Catalog.Sets[s].LootRarity;
                if (rarity == null && s == 2 && Settings.WyrmLoot) rarity = "4. Epic";
                if (rarity == null) continue;
                LootTable.RarityList list = null;
                foreach (LootTable.RarityList l in global.Entries)
                    if (l != null && l.Rarity != null && l.Rarity.name == rarity) list = l;
                if (list == null)
                {
                    done.Add(Catalog.Sets[s].Title + ": no " + rarity + " list");
                    continue;
                }
                List<LootTable.Entry> entries = new List<LootTable.Entry>(list.Entries ?? new LootTable.Entry[0]);
                int added = 0;
                for (int slot = 0; slot < Catalog.SlotCount; slot++)
                {
                    int piece = Catalog.Piece(s, slot);
                    if (Prefabs.Items[piece] == null) continue;
                    bool present = false;
                    foreach (LootTable.Entry e in entries) if (e != null && ReferenceEquals(e.Item, Prefabs.Items[piece])) present = true;
                    if (present) continue;
                    LootTable.Entry entry = new LootTable.Entry();
                    entryItem.SetValue(entry, Prefabs.Items[piece]);
                    entries.Add(entry);
                    added++;
                }
                listEntries.SetValue(list, entries.ToArray());
                done.Add(Catalog.Sets[s].Title + " +" + added + " in " + rarity + " (" + entries.Count + " entries)");
            }
            Report = "[Global]: " + string.Join("; ", done.ToArray());
            Mod.Log.Msg("loot: " + Report);
        }

        internal static string Check(int rolls)
        {
            StringBuilder sb = new StringBuilder();
            for (int s = 0; s < Catalog.Sets.Length; s++)
            {
                Item it = Prefabs.Items[Catalog.Piece(s, 1)];
                if (it == null) continue;
                List<string> tags = new List<string>();
                foreach (PickupTag t in it.Tags) if (t != null) tags.Add(t.name);
                sb.Append(Catalog.Sets[s].Title).Append(": category ").Append(it.LootCategory != null ? it.LootCategory.name + " x" + it.LootCategory.Weighting : "none")
                    .Append(", tags ").Append(string.Join(",", tags.ToArray())).Append('\n');
            }
            FieldInfo prosperity = typeof(LootModule).GetField("prosperity", Inst);
            Dictionary<string, int> picked = new Dictionary<string, int>();
            Dictionary<string, int> refused = new Dictionary<string, int>();
            int modules = 0, docks = 0, taking = 0, total = 0, armor = 0;
            foreach (LootModule m in UnityEngine.Object.FindObjectsOfType<LootModule>())
            {
                if (m.Group == null) continue;
                modules++;
                float power = (float)prosperity.GetValue(m);
                foreach (PickupDock d in m.Group)
                {
                    if (d == null) continue;
                    docks++;
                    PickupDock.AllowedResult r = d.CheckAllowed(Prefabs.Items[Catalog.Piece(0, 1)]);
                    if (r == PickupDock.AllowedResult.Ok) taking++;
                    else
                    {
                        string k = r.ToString();
                        refused[k] = refused.ContainsKey(k) ? refused[k] + 1 : 1;
                    }
                    for (int i = 0; i < rolls; i++)
                    {
                        int count;
                        Item got = LootTable.GetLoot(d, new LootTable[0], power, out count);
                        total++;
                        if (got == null || !Prefabs.IsArmor(got.Hash)) continue;
                        armor++;
                        string k = got.name + " x" + count;
                        picked[k] = picked.ContainsKey(k) ? picked[k] + 1 : 1;
                    }
                }
            }
            if (modules == 0)
            {
                List<string> with = new List<string>();
                foreach (NetworkPrefab np in PrefabManager.AllPrefabs)
                    if (np != null && np.GetComponentInChildren<LootModule>(true) != null && with.Count < 20) with.Add(np.name + " " + np.Hash);
                sb.Append("prefabs with a loot module: ").Append(string.Join(", ", with.ToArray())).Append('\n');
            }
            sb.Append(modules).Append(" loot modules, ").Append(docks).Append(" docks, ").Append(taking).Append(" take leather");
            foreach (KeyValuePair<string, int> kv in refused) sb.Append("; ").Append(kv.Value).Append(' ').Append(kv.Key);
            sb.Append('\n').Append(total).Append(" rolls, ").Append(armor).Append(" armor");
            foreach (KeyValuePair<string, int> kv in picked) sb.Append("; ").Append(kv.Key).Append(" ").Append(kv.Value).Append(" times");
            return sb.ToString();
        }
    }
}
