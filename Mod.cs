using HarmonyLib;
using Il2CppScheduleOne.Property;
using Il2CppScheduleOne.UI;
using Il2CppTMPro;
using MelonLoader;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

[assembly: MelonInfo(typeof(BusinessUpgrades.Mod), "BusinessUpgrades", "0.1.0", "pligane")]
[assembly: MelonGame("TVGS", "Schedule I")]

namespace BusinessUpgrades;

public class Mod : MelonMod
{
    public static void Log(string msg) => Melon<Mod>.Logger.Msg(msg);

    public override void OnInitializeMelon() => Log("loaded");
    public override void OnDeinitializeMelon() => Net.Shutdown();
    // Leaving a save: nothing acts on the list until the next one has loaded.
    public override void OnSceneWasUnloaded(int buildIndex, string sceneName) { if (sceneName == "Main") Upgrades.Ready = false; }

    float nextTick;
    public override void OnUpdate()
    {
        try
        {
            Net.Update();
            DevKeys();
            if (UnityEngine.Time.time < nextTick) return;
            nextTick = UnityEngine.Time.time + 1f;
            Net.Tick();
            Saul.Tick();
            Supply.Tick();
        }
        catch (System.Exception e) { Log($"update: {e.Message}"); }
    }

    // ponytail: dev keys for testing Saul; remove before release.
    // F8: log the floor under the player (to place Saul). F9: forget the Laundromat's upgrades and Saul's progress (host).
    // F10: give the player what Saul's open quest asks for. F11: run the Laundromat's 08:00 supply now (host).
    static void DevKeys()
    {
        if (Input.GetKeyDown(KeyCode.F8))
        {
            var p = Il2CppScheduleOne.PlayerScripts.Player.Local;
            if (p == null) return;
            var pos = p.transform.position;
            var floor = Physics.Raycast(pos + Vector3.up * 0.5f, Vector3.down, out var hit, 5f) ? hit.point : pos;
            Log($"dev F8: floor ({floor.x:F2}f, {floor.y:F2}f, {floor.z:F2}f), player {pos}, facing yaw {p.transform.eulerAngles.y:F0}");
            foreach (var st in Object.FindObjectsOfType<Il2CppScheduleOne.Storage.StorageEntity>())
            {
                float d = Vector3.Distance(st.transform.position, pos);
                if (d > 8f) continue;
                Log($"  storage \"{st.StorageEntityName}\" ({st.GetIl2CppType().Name}, object {st.gameObject.name}) slots={st.SlotCount} at {st.transform.position} ({d:F1} m)");
            }
        }
        if (Input.GetKeyDown(KeyCode.F10) && Upgrades.Ready) Saul.DevGive();
        if (Input.GetKeyDown(KeyCode.F11) && Il2CppFishNet.InstanceFinder.IsServer && Upgrades.Ready) // run the 08:00 supply now
            foreach (var b in Il2CppScheduleOne.Property.Business.Businesses)
                if (b.PropertyCode == "laundromat") Supply.Produce(b, "mouthwash", System.Math.Max(Supply.PerDay("laundromat"), 1));
        if (Input.GetKeyDown(KeyCode.F9) && Il2CppFishNet.InstanceFinder.IsServer && Upgrades.Instance != null)
        {
            int n = Upgrades.Instance.Owned.RemoveAll(x => x.StartsWith("laundromat:"));
            Effects.ApplyAll();
            Log($"dev F9: removed {n} Laundromat entries (upgrades + Saul's progress)");
        }
    }
}

// Each business's laundering computer: record its base capacity, apply upgrades, add the "Upgrades" button + panel.
[HarmonyPatch(typeof(LaunderingInterface), nameof(LaunderingInterface.Initialize))]
static class LaunderingInterfaceInit
{
    static void Postfix(LaunderingInterface __instance, Business bus)
    {
        Effects.Register(bus, __instance);
        if (!Trees.All.ContainsKey(bus.PropertyCode)) return; // only businesses with a tree get the button

        var panel = new Panel(__instance, bus);
        var src = __instance.launderButton;
        var topbar = src.transform.parent.parent.Find("Topbar"); // title bar: its right side is empty
        var btn = Object.Instantiate(src.gameObject, topbar).GetComponent<Button>();
        btn.name = "BusinessUpgradesButton";
        var rt = btn.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1, 0.5f);
        rt.anchoredPosition = new Vector2(-8, 0); rt.sizeDelta = new Vector2(70, 18);
        btn.onClick = new Button.ButtonClickedEvent(); // drop the cloned Launder listeners
        btn.onClick.AddListener((UnityAction)panel.Show);
        btn.interactable = true;
        var label = btn.GetComponentInChildren<TextMeshProUGUI>();
        if (label != null) label.text = "Upgrades";
        panel.OpenButton = btn;
        Panel.UpdateButtons();
    }
}

// Opening the computer always starts on the laundering screen.
[HarmonyPatch(typeof(LaunderingInterface), nameof(LaunderingInterface.Open))]
static class LaunderingInterfaceOpen
{
    static void Postfix(LaunderingInterface __instance)
    {
        if (__instance.Business != null && Panel.ByBusiness.TryGetValue(__instance.Business.PropertyCode, out var p)) p.Hide();
    }
}

// Laundering time: every operation (new or loaded from the save) gets the business's upgraded duration.
[HarmonyPatch(typeof(Business), nameof(Business.MinsPass))]
static class BusinessMinsPass
{
    static void Prefix(Business __instance)
    {
        int m = Effects.Minutes(__instance.PropertyCode);
        foreach (var op in __instance.LaunderingOperations) op.completionTime_Minutes = m;
    }
}

// Cleaner books: a finished load pays its bonus share to the bank too (host only, like the game's own payout).
[HarmonyPatch(typeof(Business), nameof(Business.CompleteOperation))]
static class BusinessCompleteOperation
{
    static void Prefix(Business __instance, LaunderingOperation op)
    {
        if (!Il2CppFishNet.InstanceFinder.IsServer || op == null) return;
        float bonus = op.amount * Effects.Payout(__instance.PropertyCode);
        if (bonus < 1f) return;
        Il2CppScheduleOne.DevUtilities.NetworkSingleton<Il2CppScheduleOne.Money.MoneyManager>.Instance
            .CreateOnlineTransaction($"Cleaner books ({__instance.PropertyName})", bonus, 1f, string.Empty);
        Mod.Log($"{__instance.PropertyCode}: payout bonus ${bonus:N0} on a ${op.amount:N0} load");
    }
}
