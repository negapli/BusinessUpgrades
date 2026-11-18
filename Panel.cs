using System;
using System.Collections.Generic;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Money;
using Il2CppScheduleOne.Property;
using Il2CppScheduleOne.UI;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace BusinessUpgrades;

/// The upgrades screen, laid over the laundering computer's screen: tier tabs, the tier's unlock row, its nodes.
public class Panel
{
    public static readonly Dictionary<string, Panel> ByBusiness = new();

    readonly LaunderingInterface ui;
    readonly Business bus;
    readonly string biz;
    readonly GameObject root;
    readonly TextMeshProUGUI header, tierStatus;
    readonly Button tierButton;
    readonly List<(GameObject row, TextMeshProUGUI text, Button button)> nodeRows = new();
    readonly List<Button> tabs = new();
    int tier;
    string message; // last "not enough cash" etc., cleared on the next refresh after a click elsewhere

    public Panel(LaunderingInterface ui, Business bus)
    {
        this.ui = ui; this.bus = bus; biz = bus.PropertyCode;

        root = new GameObject("BusinessUpgradesPanel");
        var rt = root.AddComponent<RectTransform>();
        rt.SetParent(ui.container, false);
        // The container is mirrored/scaled differently from where the game's button lives: give the panel that place's
        // world rotation and scale, so cloned buttons and labels render like the originals; then cover the container.
        var home = ui.launderButton.transform.parent; // where the game's own button lives (and renders right)
        var c = ui.container.lossyScale; var b = home.lossyScale;
        var s = new Vector3(b.x / c.x, b.y / c.y, 1);
        rt.localScale = s;
        rt.rotation = home.rotation; // the container is turned around compared to the button's parent
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(ui.container.rect.width / Mathf.Abs(s.x), ui.container.rect.height / Mathf.Abs(s.y));
        Mod.Log($"panel scale {s} (container {c.ToString("F5")} rot {ui.container.eulerAngles}, home {b.ToString("F5")} rot {home.eulerAngles}, button local {ui.launderButton.transform.localScale}), size {rt.sizeDelta}");
        root.AddComponent<Image>().color = new Color(0.17f, 0.26f, 0.36f, 1f); // opaque: blocks clicks to the screen below
        var v = root.AddComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(24, 24, 20, 20); v.spacing = 10;
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = true; v.childForceExpandHeight = false;

        var tabRow = Row(root.transform);
        for (int i = 0; i < Trees.All[biz].Length; i++)
        {
            int t = i;
            tabs.Add(MakeButton(tabRow, "T" + (i + 1), 90, () => { tier = t; message = null; Refresh(); }));
        }
        Text(tabRow, "", 1); // spacer
        MakeButton(tabRow, "Back", 120, () => root.SetActive(false));

        header = Text(Row(root.transform), "", 1, 30);
        var tr = Row(root.transform);
        tierStatus = Text(tr, "", 1);
        tierButton = MakeButton(tr, "Unlock", 160, BuyTier);
        for (int i = 0; i < 3; i++)
        {
            var r = Row(root.transform);
            int k = i;
            nodeRows.Add((r.gameObject, Text(r, "", 1), MakeButton(r, "Buy", 160, () => BuyNode(k))));
        }
        root.SetActive(false);
        ByBusiness[biz] = this;
    }

    public void Show() { message = null; root.SetActive(true); root.transform.SetAsLastSibling(); Refresh(); }
    public void Hide() => root.SetActive(false);

    void Refresh()
    {
        var t = Trees.All[biz][tier];
        for (int i = 0; i < tabs.Count; i++) tabs[i].interactable = i != tier;
        header.text = $"Tier {tier + 1}: {t.Name}";
        bool owned = Trees.HasTier(biz, tier);
        var tlock = owned ? null : Trees.TierLock(biz, tier);
        tierStatus.text = owned ? "Unlocked" : $"Unlock for {MoneyManager.FormatAmount(t.Cost)}" + (tlock != null ? $"\n<color=#ffb347>{tlock}</color>" : "");
        if (message != null) tierStatus.text += $"\n<color=#ff6b6b>{message}</color>";
        tierButton.gameObject.SetActive(!owned);
        tierButton.interactable = tlock == null;

        for (int i = 0; i < nodeRows.Count; i++)
        {
            var (row, text, button) = nodeRows[i];
            row.SetActive(i < t.Nodes.Length);
            if (i >= t.Nodes.Length) continue;
            var n = t.Nodes[i];
            bool has = Upgrades.Has(biz, n.Id);
            var nlock = has ? null : Trees.NodeLock(biz, tier, n);
            string desc = n.Desc.Replace("{cap}", MoneyManager.FormatAmount(Effects.BaseOf(bus) * n.Capacity));
            text.text = $"<b>{n.Name}</b>  {MoneyManager.FormatAmount(n.Cost)}\n<size=80%>{desc}</size>"
                        + (nlock != null && Trees.HasTier(biz, tier) ? $"\n<size=80%><color=#ffb347>{nlock}</color></size>" : "");
            button.interactable = !has && nlock == null;
            SetLabel(button, has ? "Owned" : "Buy");
        }
    }

    void BuyTier()
    {
        if (Trees.HasTier(biz, tier) || Trees.TierLock(biz, tier) != null) return;
        if (!Pay(Trees.All[biz][tier].Cost, $"Tier {tier + 1}: {Trees.All[biz][tier].Name}")) return;
        Trees.OwnTier(biz, tier);
        Mod.Log($"{biz}: unlocked tier {tier + 1}");
        Refresh();
    }

    void BuyNode(int k)
    {
        var n = Trees.All[biz][tier].Nodes[k];
        if (Upgrades.Has(biz, n.Id) || Trees.NodeLock(biz, tier, n) != null) return;
        if (!Pay(n.Cost, n.Name)) return;
        Trees.Own(biz, n.Id);
        Mod.Log($"{biz}: bought {n.Id}; capacity {bus.LaunderCapacity}, laundering {Effects.Minutes(biz)} min");
        Refresh();
    }

    bool Pay(float cost, string what)
    {
        // ponytail: single player only; milestone 3 routes purchases through the host
        if (Upgrades.Instance == null) { message = "No save loaded"; Refresh(); return false; }
        var money = NetworkSingleton<MoneyManager>.Instance;
        // Paid from the bank (like property purchases): networked, and listed in the phone's banking app.
        if (money.sync___get_value_onlineBalance() < cost) { message = "Not enough money in the bank"; Refresh(); return false; }
        money.CreateOnlineTransaction($"{bus.PropertyName} upgrade", -cost, 1f, what);
        message = null;
        return true;
    }

    // --- UI helpers: clone the screen's own button and label so fonts and styles match ---

    Transform Row(Transform parent)
    {
        var go = new GameObject("Row");
        go.AddComponent<RectTransform>().SetParent(parent, false);
        var h = go.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 12; h.childAlignment = TextAnchor.MiddleLeft;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = h.childForceExpandHeight = false;
        go.AddComponent<LayoutElement>().minHeight = 44;
        return go.transform;
    }

    TextMeshProUGUI Text(Transform parent, string s, float flex, float size = 0)
    {
        var go = UnityEngine.Object.Instantiate(ui.launderCapacityLabel.gameObject, parent, false);
        go.name = "Text";
        var t = go.GetComponent<TextMeshProUGUI>();
        t.text = s; t.alignment = TextAlignmentOptions.MidlineLeft; t.enableWordWrapping = true; t.color = Color.white;
        if (size > 0) t.fontSize = size;
        var le = go.GetComponent<LayoutElement>(); if (le == null) le = go.AddComponent<LayoutElement>(); // Unity null: no ??
        le.flexibleWidth = flex;
        return t;
    }

    Button MakeButton(Transform parent, string label, float width, Action onClick)
    {
        var b = UnityEngine.Object.Instantiate(ui.launderButton.gameObject, parent, false).GetComponent<Button>();
        b.name = "Button";
        b.onClick = new Button.ButtonClickedEvent(); // drop the cloned listeners
        b.onClick.AddListener((UnityAction)onClick); // not S1API ButtonUtils (it ignores a delegate it saw before)
        var le = b.GetComponent<LayoutElement>(); if (le == null) le = b.gameObject.AddComponent<LayoutElement>();
        le.preferredWidth = width; le.preferredHeight = 44;
        SetLabel(b, label);
        return b;
    }

    static void SetLabel(Button b, string s)
    {
        var t = b.GetComponentInChildren<TextMeshProUGUI>();
        if (t != null) t.text = s;
    }
}
