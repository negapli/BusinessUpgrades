using System.Collections.Generic;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Levelling;
using Il2CppScheduleOne.Money;
using Il2CppScheduleOne.Property;
using Il2CppScheduleOne.UI;
using S1API.Internal.Abstraction;
using S1API.Saveables;

namespace BusinessUpgrades;

public enum CartelPath { Any, Defeated, Truced }

/// Capacity = bonus as a multiple of the business's base capacity; Minutes = laundering time (lowest owned wins).
public record Node(string Id, string Name, string Desc, float Cost, float Capacity = 0, int Minutes = 0, CartelPath Cartel = CartelPath.Any);
public record Tier(string Name, float Cost, ERank Rank, string Quest, Node[] Nodes);

public static class Trees
{
    // Keyed by Property.PropertyCode. Numbers: idea.md. "{cap}" in a Desc = the node's capacity bonus in dollars.
    public static readonly Dictionary<string, Tier[]> All = new()
    {
        ["laundromat"] = new Tier[]
        {
            new("Back room", 2500, ERank.Street_Rat, null, new Node[]
            {
                new("extra_drum", "Extra drum", "Capacity +{cap}", 1500, Capacity: 0.5f),
                new("fast_cycle", "Fast cycle", "Laundering takes 18 h", 2000, Minutes: 1080),
            }),
            new("The cousin", 7500, ERank.Street_Rat, "20 meth, Standard quality or better", new Node[]
            {
                new("coin_tray", "Coin tray", "Cash left in the tray launders itself at 08:00", 5000),
                new("bigger_drums", "Bigger drums", "Capacity +{cap}", 6000, Capacity: 1f),
                new("supply_closet", "Supply closet", "10 Mouth Wash a day", 4000),
            }),
            new("Night shift", 20000, ERank.Bagman, "15 of a product with the Energizing effect", new Node[]
            {
                new("night_shift", "Night shift", "Laundering takes 12 h", 12000, Minutes: 720),
                new("two_lines", "Two lines", "A 2nd load runs at the same time", 15000),
                new("bulk_supplier", "Bulk supplier", "40 Mouth Wash a day", 12000),
            }),
            new("Industrial", 75000, ERank.Block_Boss, null, new Node[]
            {
                new("industrial", "Industrial machines", "Capacity +{cap}, laundering takes 8 h", 40000, Capacity: 3f, Minutes: 480, Cartel: CartelPath.Defeated),
                new("cartel_contract", "Cartel contract", "The cartel pays $1,500 a day", 30000, Cartel: CartelPath.Truced),
                new("wholesale", "Wholesale account", "120 Mouth Wash a day + a 2nd shelf", 30000),
            }),
        },
    };

    static string TierId(int i) => "t" + (i + 1);
    public static bool HasTier(string biz, int i) => Upgrades.Has(biz, TierId(i));

    /// Why tier i can't be unlocked yet, or null.
    public static string TierLock(string biz, int i)
    {
        var t = All[biz][i];
        if (i > 0 && !HasTier(biz, i - 1)) return $"Unlock tier {i} first";
        var lm = NetworkSingleton<LevelManager>.Instance;
        if (lm != null && lm.Rank < t.Rank) return $"Needs rank {t.Rank.ToString().Replace('_', ' ')}";
        if (t.Quest != null) return $"Quest: {t.Quest} (coming soon)"; // milestone 4
        return null;
    }

    public static string NodeLock(string biz, int i, Node n)
    {
        if (!HasTier(biz, i)) return "Unlock this tier first";
        if (n.Cartel != CartelPath.Any) return $"Cartel {n.Cartel.ToString().ToLower()} (coming soon)"; // milestone 6
        return null;
    }

    public static void Own(string biz, string id) { Upgrades.Instance.Owned.Add(biz + ":" + id); Effects.ApplyAll(); }
    public static void OwnTier(string biz, int i) => Own(biz, TierId(i));
}

// One per save file (S1API). Owned entries look like "laundromat:t1" / "laundromat:extra_drum".
public class Upgrades : Saveable
{
    public static Upgrades Instance { get; private set; }
    [SaveableField("owned")] public List<string> Owned = new();
    public Upgrades() => Instance = this;

    public static bool Has(string biz, string id) => Instance != null && Instance.Owned.Contains(biz + ":" + id);

    // S1API keeps one instance per game session: a save without data must not inherit the last one's.
    protected override void OnCreated() { Owned = new(); Effects.ApplyAll(); }
    protected override void OnLoaded()
    {
        Owned ??= new();
        Effects.ApplyAll();
        Mod.Log($"loaded {Owned.Count} upgrade(s)");
    }
}

/// Re-applied on every load and purchase: the game saves neither capacity nor duration.
public static class Effects
{
    // Base capacity per Business object (keyed by native pointer: a reload makes new objects, so no stale bases).
    static readonly Dictionary<System.IntPtr, float> Base = new();
    static readonly Dictionary<string, LaunderingInterface> Screens = new();

    public static void Register(Business b, LaunderingInterface ui)
    {
        Base[b.Pointer] = b.LaunderCapacity; // vanilla or another mod's value: we add on top
        Screens[b.PropertyCode] = ui;
        Apply(b);
    }

    public static float BaseOf(Business b) => Base.TryGetValue(b.Pointer, out var c) ? c : b.LaunderCapacity;

    public static void ApplyAll()
    {
        foreach (var b in Business.Businesses) Apply(b);
    }

    static void Apply(Business b)
    {
        if (!Base.TryGetValue(b.Pointer, out var c) || !Trees.All.TryGetValue(b.PropertyCode, out var tiers)) return;
        float bonus = 0;
        foreach (var t in tiers) foreach (var n in t.Nodes) if (Upgrades.Has(b.PropertyCode, n.Id)) bonus += n.Capacity;
        b.LaunderCapacity = c * (1 + bonus);
        if (!Screens.TryGetValue(b.PropertyCode, out var ui) || ui == null) return;
        ui.launderCapacityLabel.text = MoneyManager.FormatAmount(b.LaunderCapacity);
        var stamps = ui.launderButton.transform.parent.Find("Timestamps"); // "START", "12 Hrs", "24 Hrs" under the timeline
        int hours = Minutes(b.PropertyCode) / 60;
        if (stamps != null && stamps.childCount == 3)
        {
            stamps.GetChild(1).GetComponent<Il2CppTMPro.TextMeshProUGUI>().text = $"{hours / 2f:0.#} Hrs";
            stamps.GetChild(2).GetComponent<Il2CppTMPro.TextMeshProUGUI>().text = $"{hours} Hrs";
        }
    }

    public static int Minutes(string biz)
    {
        int m = 1440;
        if (Trees.All.TryGetValue(biz, out var tiers))
            foreach (var t in tiers) foreach (var n in t.Nodes)
                if (n.Minutes > 0 && n.Minutes < m && Upgrades.Has(biz, n.Id)) m = n.Minutes;
        return m;
    }
}
