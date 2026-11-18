using HarmonyLib;
using Il2CppScheduleOne;
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

// Milestone 1: log real business numbers + Mouth Wash's ID, add a placeholder "Upgrades" button.
[HarmonyPatch(typeof(LaunderingInterface), nameof(LaunderingInterface.Initialize))]
static class LaunderingInterfaceInit
{
    static void Postfix(LaunderingInterface __instance, Business bus)
    {
        Mod.Log($"business {bus.PropertyCode} \"{bus.PropertyName}\": Price={bus.Price} LaunderCapacity={bus.LaunderCapacity}");
        LogMouthWashOnce();

        var src = __instance.launderButton;
        var btn = Object.Instantiate(src.gameObject, src.transform.parent).GetComponent<Button>();
        btn.name = "BusinessUpgradesButton";
        var rt = btn.GetComponent<RectTransform>();
        rt.anchoredPosition -= new Vector2(0, rt.rect.height * 1.2f);
        btn.onClick = new Button.ButtonClickedEvent(); // drop the cloned Launder listeners
        btn.onClick.AddListener((UnityAction)(() => Mod.Log($"Upgrades clicked: {bus.PropertyName}")));
        btn.interactable = true;
        var label = btn.GetComponentInChildren<TextMeshProUGUI>();
        if (label != null) label.text = "Upgrades";
    }

    static bool mouthWashLogged;
    static void LogMouthWashOnce()
    {
        if (mouthWashLogged) return;
        mouthWashLogged = true;
        foreach (var reg in Registry.Instance.ItemRegistry)
            if ((reg.ID + " " + reg.name).ToLower().Contains("mouth"))
                Mod.Log($"item: ID={reg.ID} name={reg.name}");
    }
}
