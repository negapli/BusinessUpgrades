using System.Collections.Generic;
using Il2CppFishNet;
using Il2CppScheduleOne;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.GameTime;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Property;
using Il2CppScheduleOne.Storage;

namespace BusinessUpgrades;

// Supply branch: at 08:00 the host puts the day's items into the storage racks the players placed inside the business
// (user's choice: their own racks, any size; a bigger rack = more room). Highest owned supply node wins. Items that
// don't fit are lost for the day. Slot writes are the game's networked calls, so every player sees them.
static class Supply
{
    // What each business produces (item ID). Business codes: Property.PropertyCode.
    static readonly Dictionary<string, string> Item = new() { ["laundromat"] = "mouthwash" };

    public static int PerDay(string biz)
    {
        int n = 0;
        if (Trees.All.TryGetValue(biz, out var tiers))
            foreach (var t in tiers) foreach (var node in t.Nodes) if (node.Supply > n && Upgrades.Has(biz, node.Id)) n = node.Supply;
        return n;
    }

    static int lastTime = -1;
    public static void Tick()
    {
        var time = NetworkSingleton<TimeManager>.Instance;
        if (!InstanceFinder.IsServer || !Upgrades.Ready || time == null) { lastTime = -1; return; }
        int now = time.CurrentTime;
        bool morning = lastTime >= 0 && lastTime < 800 && now >= 800; // crossing 08:00 while playing (sleep ends at 07:00)
        lastTime = now;
        if (!morning) return;
        foreach (var b in Business.Businesses)
        {
            if (!b.IsOwned) continue;
            if (AutoLaunders(b.PropertyCode)) LaunderRackCash(b); // first: frees rack space for the supplies
            if (Item.TryGetValue(b.PropertyCode, out var id) && PerDay(b.PropertyCode) is int n && n > 0) Produce(b, id, n);
        }
    }

    static bool AutoLaunders(string biz)
    {
        if (Trees.All.TryGetValue(biz, out var tiers))
            foreach (var t in tiers) foreach (var node in t.Nodes) if (node.AutoLaunder && Upgrades.Has(biz, node.Id)) return true;
        return false;
    }

    /// Coin tray: cash in the business's racks goes into a new laundering load, up to the free capacity.
    public static void LaunderRackCash(Business b)
    {
        float room = b.appliedLaunderLimit, taken = 0;
        foreach (var (_, rack) in Racks(b))
            foreach (var slot in rack.ItemSlots)
            {
                if (room - taken < 1f) break;
                var cash = slot?.ItemInstance?.TryCast<CashInstance>();
                if (cash == null || slot.IsLocked || slot.IsRemovalLocked) continue;
                float take = System.Math.Min(cash.Balance, room - taken);
                if (take >= cash.Balance) slot.ClearStoredInstance(); // networked
                else { cash.SetBalance(cash.Balance - take, true); slot.ReplicateStoredInstance(); }
                taken += take;
            }
        taken = (float)System.Math.Floor(taken);
        if (taken >= 1f) b.StartLaunderingOperation(taken); // the game's call: networked, saved, paid at the end
        Mod.Log($"coin tray: {b.PropertyName} started laundering ${taken:N0} from its racks (free capacity was ${room:N0})");
    }

    static List<(string guid, StorageEntity s)> Racks(Business b)
    {
        var racks = new List<(string guid, StorageEntity s)>();
        foreach (var bi in b.BuildableItems)
            if (bi != null && bi.GetComponent<StorageEntity>() is StorageEntity s && s != null) racks.Add((bi.GUID.ToString(), s));
        racks.Sort((x, y) => string.CompareOrdinal(x.guid, y.guid)); // same order on every machine
        return racks;
    }

    public static void Produce(Business b, string id, int amount)
    {
        var def = Registry.GetItem(id);
        if (def == null) { Mod.Log($"supply: unknown item {id}"); return; }
        var racks = Racks(b);
        int left = amount, limit = def.StackLimit;
        foreach (var (_, rack) in racks)
            foreach (var slot in rack.ItemSlots)
            {
                if (left <= 0) break;
                if (slot == null || slot.IsLocked || slot.IsAddLocked) continue;
                var held = slot.ItemInstance;
                if (held == null)
                {
                    int put = System.Math.Min(left, limit);
                    slot.SetStoredItem(def.GetDefaultInstance(put)); // networked
                    left -= put;
                }
                else if (held.ID == id && held.Quantity < limit)
                {
                    int put = System.Math.Min(left, limit - held.Quantity);
                    slot.ChangeQuantity(put); // networked
                    left -= put;
                }
            }
        Mod.Log($"supply: {b.PropertyName} got {amount - left}/{amount} {def.Name} in {racks.Count} rack(s){(left > 0 ? $", {left} didn't fit" : "")}");
    }
}
