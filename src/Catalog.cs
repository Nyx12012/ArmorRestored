using System;
using System.Reflection;
using Alta.Customization;
using Alta.Impact;
using UnityEngine;

namespace ArmorRestored
{
    internal sealed class ArmorSet
    {
        public string Id, Title, LootRarity;
        public float Pierce = 1f, Slash = 1f, Blunt = 1f, Caustic = 1f, Explosive = 1f, Conductivity = 1f;
        public ArmorDefinition Definition;
    }

    internal sealed class Slot
    {
        public string Id, Title;
        public EquipmentSlot Game;
        public string Anchor;
        public int RemovedBy;
    }

    internal static class Catalog
    {
        internal const int FirstNumber = 52830;
        internal const int SlotCount = 8;

        internal static readonly ArmorSet[] Sets =
        {
            new ArmorSet { Id = "leather", Title = "Leather", LootRarity = "3. Rare", Pierce = 0.7f, Slash = 0.7f, Blunt = 0.85f, Conductivity = 0.2f },
            new ArmorSet { Id = "metal", Title = "Metal", LootRarity = "4. Epic", Pierce = 0.5f, Slash = 0.4f, Blunt = 0.9f, Caustic = 0.6f, Explosive = 0.4f, Conductivity = 1.2f },
            new ArmorSet { Id = "wyrm", Title = "Wyrm", LootRarity = null, Pierce = 1f, Slash = 0.8f, Blunt = 0.6f, Caustic = 0.1f, Conductivity = 0.8f },
        };

        internal static readonly Slot[] Slots =
        {
            new Slot { Id = "helmet", Title = "Helmet", Game = EquipmentSlot.Head, Anchor = "head_jnt" },
            new Slot { Id = "chest", Title = "Chest Plate", Game = EquipmentSlot.Chest, Anchor = "chest_jnt" },
            new Slot { Id = "shoulder_l", Title = "Left Shoulder Pad", Game = EquipmentSlot.LeftShoulder, Anchor = "shoulder_L_jnt" },
            new Slot { Id = "shoulder_r", Title = "Right Shoulder Pad", Game = EquipmentSlot.RightShoulder, Anchor = "shoulder_R_jnt" },
            new Slot { Id = "gauntlet_l", Title = "Left Gauntlet", Game = EquipmentSlot.LeftArm, Anchor = "forearm_L_jnt", RemovedBy = 2 },
            new Slot { Id = "gauntlet_r", Title = "Right Gauntlet", Game = EquipmentSlot.RightArm, Anchor = "forearm_R_jnt", RemovedBy = 1 },
            new Slot { Id = "tasset_l", Title = "Left Tasset", Game = EquipmentSlot.LeftTasset, Anchor = "hipBase_jnt" },
            new Slot { Id = "tasset_r", Title = "Right Tasset", Game = EquipmentSlot.RightTasset, Anchor = "hipBase_jnt" },
        };

        internal static int Count { get { return Sets.Length * SlotCount; } }

        internal static int Number(int piece) { return FirstNumber + piece; }

        internal static int Piece(int set, int slot) { return set * SlotCount + slot; }

        internal static int PieceOf(uint number)
        {
            int p = (int)number - FirstNumber;
            return p >= 0 && p < Count ? p : -1;
        }

        internal static ArmorSet SetOf(int piece) { return Sets[piece / SlotCount]; }

        internal static Slot SlotOf(int piece) { return Slots[piece % SlotCount]; }

        internal static string Title(int piece)
        {
            return SetOf(piece).Title + " " + SlotOf(piece).Title;
        }

        internal static string Id(int piece)
        {
            return SetOf(piece).Id + "." + SlotOf(piece).Id;
        }

        internal static int FindSet(string s)
        {
            for (int i = 0; i < Sets.Length; i++)
                if (string.Equals(Sets[i].Id, s, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        internal static int FindSlot(string s)
        {
            for (int i = 0; i < Slots.Length; i++)
                if (string.Equals(Slots[i].Id, s, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        internal static void Check()
        {
            if (Pack.Pieces.Count != Count) throw new InvalidOperationException("the armor pack has " + Pack.Pieces.Count + " pieces, expected " + Count);
            for (int i = 0; i < Count; i++)
                if (Pack.Pieces[i].Id != Id(i) || Pack.Pieces[i].Slot != SlotOf(i).Game.ToString())
                    throw new InvalidOperationException("armor pack piece " + i + " is " + Pack.Pieces[i].Id + "/" + Pack.Pieces[i].Slot + ", expected " + Id(i));
        }

        internal static ArmorDefinition Definition(int set)
        {
            ArmorSet s = Sets[set];
            if (s.Definition != null) return s.Definition;
            const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            FieldInfo list = typeof(ArmorDefinition).GetField("attackTypeMultipliers", F);
            FieldInfo cond = typeof(ArmorDefinition).GetField("conductivityMultiplier", F);
            FieldInfo type = typeof(AttackTypeMultiplier).GetField("type", F);
            FieldInfo mult = typeof(AttackTypeMultiplier).GetField("multiplier", F);
            if (list == null || cond == null || type == null || mult == null)
                throw new MissingFieldException("ArmorDefinition or AttackTypeMultiplier lost a field");
            ArmorDefinition d = ScriptableObject.CreateInstance<ArmorDefinition>();
            d.name = s.Title + "Armor";
            UnityEngine.Object.DontDestroyOnLoad(d);
            ImpactType[] types = { ImpactType.Pierce, ImpactType.Slash, ImpactType.Blunt, ImpactType.Caustic, ImpactType.Explosive };
            float[] values = { s.Pierce, s.Slash, s.Blunt, s.Caustic, s.Explosive };
            System.Collections.Generic.List<AttackTypeMultiplier> entries = new System.Collections.Generic.List<AttackTypeMultiplier>();
            for (int i = 0; i < types.Length; i++)
            {
                if (values[i] == 1f) continue;
                AttackTypeMultiplier m = new AttackTypeMultiplier();
                type.SetValue(m, types[i]);
                mult.SetValue(m, values[i]);
                entries.Add(m);
            }
            list.SetValue(d, entries.ToArray());
            cond.SetValue(d, s.Conductivity);
            s.Definition = d;
            return d;
        }
    }
}
