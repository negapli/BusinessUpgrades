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
}

// Each business's laundering computer: record its base capacity, apply upgrades, add the "Upgrades" button + panel.
[HarmonyPatch(typeof(LaunderingInterface), nameof(LaunderingInterface.Initialize))]
static class LaunderingInterfaceInit
{
    static void Postfix(LaunderingInterface __instance, Business bus)
    {
        Effects.Register(bus, __instance);
        if (!Trees.All.ContainsKey(bus.PropertyCode)) return; // only businesses with a tree get the button

        if (!dumped) { dumped = true; Dump(__instance.canvas.transform, 0); } // dev: screen layout for the UI rework
        var panel = new Panel(__instance, bus);
        var src = __instance.launderButton;
        var btn = Object.Instantiate(src.gameObject, src.transform.parent).GetComponent<Button>();
        btn.name = "BusinessUpgradesButton";
        var rt = btn.GetComponent<RectTransform>();
        rt.anchoredPosition += new Vector2(rt.rect.width * 1.1f, 0); // right of Launder (below it overlaps the operations panel)
        btn.onClick = new Button.ButtonClickedEvent(); // drop the cloned Launder listeners
        btn.onClick.AddListener((UnityAction)panel.Show);
        btn.interactable = true;
        var label = btn.GetComponentInChildren<TextMeshProUGUI>();
        if (label != null) label.text = "Upgrades";
    }

    static bool dumped;
    static void Dump(Transform t, int depth)
    {
        var rt = t.GetComponent<RectTransform>();
        var comps = new System.Collections.Generic.List<string>();
        foreach (var c in t.GetComponents<Component>()) { var n = c.GetIl2CppType().Name; if (n != "RectTransform" && n != "CanvasRenderer") comps.Add(n); }
        var tmp = t.GetComponent<TextMeshProUGUI>();
        var img = t.GetComponent<Image>();
        Mod.Log($"ui {new string(' ', depth * 2)}{t.name}{(t.gameObject.activeSelf ? "" : " (off)")} [{string.Join(",", comps)}]"
            + (rt != null ? $" pos {rt.anchoredPosition} size {rt.rect.size} anchors {rt.anchorMin}-{rt.anchorMax}" : "")
            + (tmp != null ? $" text \"{tmp.text}\" size {tmp.fontSize} color {tmp.color}" : "")
            + (img != null ? $" img {(img.sprite != null ? img.sprite.name : "-")} color {img.color} type {img.type}" : ""));
        for (int i = 0; i < t.childCount; i++) Dump(t.GetChild(i), depth + 1);
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
