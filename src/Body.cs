using System;
using System.Collections.Generic;
using System.Reflection;
using Alta.Heat;
using Alta.Impact;
using UnityEngine;

namespace ArmorRestored
{
    internal sealed class Wiring
    {
        public Transform Root;
        public EquipmentReference[] Zones;
        public int Detectors, Heat;
        public readonly Dictionary<string, Transform> Bones = new Dictionary<string, Transform>();
        public readonly List<ImpactDetector>[] ZoneDetectors = new List<ImpactDetector>[Body.ZoneCount];
    }

    internal static class Body
    {
        internal const int ZoneCount = 7;
        internal static readonly string[] ZoneNames = { "Head", "Chest", "Hips", "LeftShoulder", "RightShoulder", "LeftArm", "RightArm" };
        const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        static readonly FieldInfo DetectorSlot = typeof(ImpactDetector).GetField("equipmentSlot", Inst);
        static readonly FieldInfo HeatSlot = typeof(HeatPoint).GetField("equipmentSlot", Inst);
        static readonly MethodInfo HeatChanged = typeof(HeatPoint).GetMethod("EuipmentChanged", Inst);

        internal static bool Ready { get { return DetectorSlot != null && HeatSlot != null && HeatChanged != null; } }

        static int ZoneOfDetector(Transform t)
        {
            string n = t.name, p = t.parent != null ? t.parent.name : "";
            if (p == "Head") return 0;
            if (p == "Chest") return 1;
            if (p == "Cog") return 2;
            if (n.EndsWith("shoulder_L_mov")) return 3;
            if (n.EndsWith("shoulder_R_mov")) return 4;
            if (n.EndsWith("elbow_L_mov") || n.EndsWith("wrist_L_mov")) return 5;
            if (n.EndsWith("elbow_R_mov") || n.EndsWith("wrist_R_mov")) return 6;
            return -1;
        }

        static int ZoneOfHeat(Transform t)
        {
            string n = t.name;
            if (n.EndsWith("Head_Heat Point")) return 0;
            if (n.EndsWith("Chest_Heat Point")) return 1;
            if (n.EndsWith("Shoulder_L_Heat Point")) return 3;
            if (n.EndsWith("Shoulder_R_Heat Point")) return 4;
            if (n.EndsWith("Forearm_L_Heat Point") || n.EndsWith("Wrist_L_Heat Point")) return 5;
            if (n.EndsWith("Forearm_R_Heat Point") || n.EndsWith("Wrist_R_Heat Point")) return 6;
            return -1;
        }

        internal static Wiring Wire(Transform root, bool zones)
        {
            Wiring w = new Wiring { Root = root, Zones = new EquipmentReference[ZoneCount] };
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name.EndsWith("_jnt") && !w.Bones.ContainsKey(t.name)) w.Bones[t.name] = t;
            if (!zones || !Ready) return w;
            GameObject holder = new GameObject("ArmorRestored Zones");
            holder.transform.SetParent(root, false);
            for (int z = 0; z < ZoneCount; z++)
            {
                EquipmentReference r = holder.AddComponent<EquipmentReference>();
                r.Changed += Noop;
                w.Zones[z] = r;
            }
            foreach (ImpactDetector d in root.GetComponentsInChildren<ImpactDetector>(true))
            {
                int z = ZoneOfDetector(d.transform);
                if (z < 0 || DetectorSlot.GetValue(d) != null) continue;
                DetectorSlot.SetValue(d, w.Zones[z]);
                if (w.ZoneDetectors[z] == null) w.ZoneDetectors[z] = new List<ImpactDetector>();
                w.ZoneDetectors[z].Add(d);
                w.Detectors++;
            }
            foreach (HeatPoint h in root.GetComponentsInChildren<HeatPoint>(true))
            {
                int z = ZoneOfHeat(h.transform);
                if (z < 0 || HeatSlot.GetValue(h) != null) continue;
                HeatSlot.SetValue(h, w.Zones[z]);
                w.Zones[z].Changed += (EquipmentReference.EquipmentChangedHandler)Delegate.CreateDelegate(typeof(EquipmentReference.EquipmentChangedHandler), h, HeatChanged);
                w.Heat++;
            }
            return w;
        }

        static void Noop(ArmorDefinition a) { }

        internal static HealthObject Health(Wiring w)
        {
            foreach (List<ImpactDetector> list in w.ZoneDetectors)
                if (list != null) foreach (ImpactDetector d in list) if (d != null && d.HealthTarget != null) return d.HealthTarget;
            return null;
        }

        internal static string ZoneOf(Wiring w, ImpactDetector d)
        {
            if (d == null) return "?";
            for (int z = 0; z < ZoneCount; z++) if (w.ZoneDetectors[z] != null && w.ZoneDetectors[z].Contains(d)) return ZoneNames[z];
            return d.name;
        }

        internal static void Apply(Wiring w, int[] worn)
        {
            if (w == null || w.Zones[0] == null) return;
            int hips = worn[6] >= 0 ? worn[6] : worn[7];
            int[] pieces = { worn[0], worn[1], hips, worn[2], worn[3], worn[4], worn[5] };
            for (int z = 0; z < ZoneCount; z++)
            {
                ArmorDefinition want = pieces[z] >= 0 ? Catalog.Definition(pieces[z] / Catalog.SlotCount) : null;
                if (!ReferenceEquals(w.Zones[z].Equipment, want)) w.Zones[z].Equipment = want;
            }
        }

        internal static string Describe(Wiring w)
        {
            if (w == null) return "not wired";
            List<string> parts = new List<string>();
            for (int z = 0; z < ZoneCount; z++)
            {
                ArmorDefinition a = w.Zones[z] != null ? w.Zones[z].Equipment : null;
                parts.Add(ZoneNames[z] + "=" + (a == null ? "-" : a.name));
            }
            return w.Detectors + " hit zones, " + w.Heat + " heat points wired; " + string.Join(" ", parts.ToArray());
        }

        internal static Vector3 Anchor(Wiring w, int slot)
        {
            Transform t;
            string bone = Catalog.Slots[slot].Anchor;
            if (w == null || !w.Bones.TryGetValue(bone, out t) || t == null) return w != null && w.Root != null ? w.Root.position : Vector3.zero;
            Vector3 p = t.position;
            if (slot == 0) p += Vector3.up * 0.08f;
            if (slot == 6 || slot == 7)
            {
                Transform l, r;
                if (w.Bones.TryGetValue("shoulder_L_jnt", out l) && w.Bones.TryGetValue("shoulder_R_jnt", out r))
                {
                    Vector3 side = (l.position - r.position);
                    side.y = 0f;
                    if (side.sqrMagnitude > 1e-4f) p += side.normalized * (slot == 6 ? 0.12f : -0.12f);
                }
            }
            return p;
        }
    }
}
