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

/// The upgrades view, built from the laundering screen's own parts and swapped in for them:
/// - the timeline area ("CurrentOperations") becomes a header: title, tier tabs, Back, and Unlock where Launder sits;
/// - the "All Active Laundering Operations" box is cloned into the tier's upgrade list (Upgrade / Effect / Cost).
/// Screen layout (400 x 268 units): see the "ui" dump in git history (commit 0310640).
public class Panel
{
    public static readonly Dictionary<string, Panel> ByBusiness = new();

    static readonly Color Orange = new(1f, 0.637f, 0.241f), Muted = new(0.239f, 0.294f, 0.341f),
        Blue = new(0.184f, 0.591f, 0.906f), Red = new(0.906f, 0.254f, 0.184f),
        Green = new(0.33f, 0.85f, 0.33f), Warn = new(1f, 0.392f, 0.392f);

    readonly LaunderingInterface ui;
    readonly Business bus;
    readonly string biz;
    readonly GameObject timeline, opList, header, list;
    readonly TextMeshProUGUI listTitle, status;
    readonly Button unlock;
    readonly List<Button> tabs = new();
    readonly List<(GameObject go, TextMeshProUGUI name, TextMeshProUGUI effect, TextMeshProUGUI cost, Button buy)> nodeRows = new();
    int tier;
    string message;

    public Panel(LaunderingInterface ui, Business bus)
    {
        this.ui = ui; this.bus = bus; biz = bus.PropertyCode;
        timeline = ui.launderButton.transform.parent.gameObject;   // "CurrentOperations"
        var screen = timeline.transform.parent;                       // "Container"
        opList = screen.Find("OperationList").gameObject;

        // Header: an empty copy of the timeline area's rect.
        header = new GameObject("UpgradesHeader");
        var hrt = header.AddComponent<RectTransform>();
        var trt = timeline.GetComponent<RectTransform>();
        hrt.SetParent(screen, false);
        hrt.anchorMin = trt.anchorMin; hrt.anchorMax = trt.anchorMax; hrt.pivot = trt.pivot;
        hrt.anchoredPosition = trt.anchoredPosition; hrt.sizeDelta = trt.sizeDelta;

        var title = Clone(timeline.transform.Find("Text (TMP)").gameObject, header.transform).GetComponent<TextMeshProUGUI>();
        title.text = $"{bus.PropertyName} upgrades";

        for (int i = 0; i < Trees.All[biz].Length; i++)
        {
            int t = i;
            var b = MakeButton(header.transform, "Tier " + (i + 1), () => { tier = t; message = null; Refresh(); });
            Place(b, new Vector2(0, 1), new Vector2(10 + i * 62, -38), new Vector2(58, 15));
            tabs.Add(b);
        }
        var back = MakeButton(header.transform, "Back", Hide);
        back.GetComponent<Image>().color = Red;
        Place(back, new Vector2(1, 1), new Vector2(-60, -38), new Vector2(50, 15));

        unlock = MakeButton(header.transform, "Unlock", BuyTier); // exactly where "Launder" sits
        var urt = unlock.GetComponent<RectTransform>(); var lrt = ui.launderButton.GetComponent<RectTransform>();
        urt.anchorMin = lrt.anchorMin; urt.anchorMax = lrt.anchorMax; urt.pivot = lrt.pivot;
        urt.anchoredPosition = lrt.anchoredPosition; urt.sizeDelta = lrt.sizeDelta;

        status = Clone(ui.insufficientCashLabel.gameObject, header.transform).GetComponent<TextMeshProUGUI>(); // the red hint next to Launder
        status.richText = true;

        // List: a clone of the operations box, emptied.
        list = Clone(opList, screen);
        list.name = "UpgradesList";
        listTitle = list.transform.Find("Title").GetComponent<TextMeshProUGUI>();
        list.transform.Find("BusinessLabel").GetComponent<TextMeshProUGUI>().text = "Upgrade";
        list.transform.Find("AmountLabel").GetComponent<TextMeshProUGUI>().text = "Effect";
        list.transform.Find("TimeLabel").GetComponent<TextMeshProUGUI>().text = "Cost";
        var rows = list.transform.Find("Scroll View/Viewport/Content");
        for (int i = rows.childCount - 1; i >= 0; i--) rows.GetChild(i).gameObject.SetActive(false); // cloned entries
        var rowStyle = opList.transform.Find("BusinessLabel").gameObject; // grey column-header text: our row text style
        for (int i = 0; i < 3; i++)
        {
            int k = i;
            var row = new GameObject("UpgradeRow");
            row.AddComponent<RectTransform>().SetParent(rows, false);
            var h = row.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 6; h.padding = new RectOffset(8, 4, 0, 0); h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = h.childControlHeight = true; h.childForceExpandWidth = h.childForceExpandHeight = false;
            row.AddComponent<LayoutElement>().preferredHeight = 26;
            var name = Label(rowStyle, row.transform, 95, 0, Color.white, TextAlignmentOptions.MidlineLeft);
            var effect = Label(rowStyle, row.transform, 0, 1, Color.white, TextAlignmentOptions.MidlineLeft);
            var cost = Label(rowStyle, row.transform, 50, 0, Green, TextAlignmentOptions.MidlineRight);
            var buy = MakeButton(row.transform, "Buy", () => BuyNode(k));
            var le = buy.gameObject.AddComponent<LayoutElement>(); le.preferredWidth = 44; le.preferredHeight = 15;
            nodeRows.Add((row, name, effect, cost, buy));
        }

        Hide();
        ByBusiness[biz] = this;
    }

    public void Show()
    {
        message = null;
        timeline.SetActive(false); opList.SetActive(false);
        header.SetActive(true); list.SetActive(true);
        Refresh();
    }

    public void Hide()
    {
        header.SetActive(false); list.SetActive(false);
        timeline.SetActive(true); opList.SetActive(true);
    }

    void Refresh()
    {
        var t = Trees.All[biz][tier];
        for (int i = 0; i < tabs.Count; i++)
        {
            tabs[i].GetComponent<Image>().color = i == tier ? Orange : Muted;
            SetLabel(tabs[i], (Trees.HasTier(biz, i) ? "" : "<color=#a0a0a0>") + "Tier " + (i + 1));
        }
        listTitle.text = $"Tier {tier + 1}: {t.Name}";

        bool owned = Trees.HasTier(biz, tier);
        var tlock = owned ? null : Trees.TierLock(biz, tier);
        unlock.gameObject.SetActive(!owned);
        unlock.interactable = tlock == null;
        SetLabel(unlock, $"Unlock {MoneyManager.FormatAmount(t.Cost)}");
        status.gameObject.SetActive(true);
        status.color = message != null || tlock != null ? Warn : Green;
        status.text = message ?? (owned ? $"Tier {tier + 1} unlocked" : tlock ?? "Unlock this tier to buy its upgrades");

        for (int i = 0; i < nodeRows.Count; i++)
        {
            var (go, name, effect, cost, buy) = nodeRows[i];
            go.SetActive(i < t.Nodes.Length);
            if (i >= t.Nodes.Length) continue;
            var n = t.Nodes[i];
            bool has = Upgrades.Has(biz, n.Id);
            var nlock = has ? null : Trees.NodeLock(biz, tier, n);
            name.text = n.Name;
            effect.text = n.Desc.Replace("{cap}", MoneyManager.FormatAmount(Effects.BaseOf(bus) * n.Capacity))
                          + (nlock != null && owned ? $"\n<color=#ff6464>{nlock}</color>" : "");
            cost.text = has ? "<color=#a0a0a0>Owned</color>" : MoneyManager.FormatAmount(n.Cost);
            buy.gameObject.SetActive(!has);
            buy.interactable = nlock == null;
            buy.GetComponent<Image>().color = nlock == null ? Blue : Muted;
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

    // --- helpers: everything is a clone of the screen's own objects, so fonts, sprites and styles match ---

    static GameObject Clone(GameObject src, Transform parent)
    {
        var go = UnityEngine.Object.Instantiate(src, parent, false);
        go.SetActive(true);
        return go;
    }

    Button MakeButton(Transform parent, string label, Action onClick)
    {
        var b = Clone(ui.launderButton.gameObject, parent).GetComponent<Button>();
        b.name = "Button";
        b.onClick = new Button.ButtonClickedEvent(); // drop the cloned Launder listeners
        b.onClick.AddListener((UnityAction)onClick); // not S1API ButtonUtils (it ignores a delegate it saw before)
        b.interactable = true;
        var t = b.GetComponentInChildren<TextMeshProUGUI>();
        if (t != null)
        {
            t.richText = true; t.fontSize = 8;
            var rt = t.GetComponent<RectTransform>(); // stretch the label over the whole button (it was 80 wide)
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.sizeDelta = Vector2.zero; rt.anchoredPosition = Vector2.zero;
        }
        SetLabel(b, label);
        return b;
    }

    static void Place(Button b, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        var rt = b.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = anchor; rt.pivot = new Vector2(0, 0.5f);
        rt.anchoredPosition = pos; rt.sizeDelta = size;
    }

    static TextMeshProUGUI Label(GameObject style, Transform parent, float width, float flex, Color color, TextAlignmentOptions align)
    {
        var t = Clone(style, parent).GetComponent<TextMeshProUGUI>();
        t.name = "Label"; t.text = ""; t.color = color; t.alignment = align; t.richText = true; t.enableWordWrapping = true;
        var le = t.gameObject.AddComponent<LayoutElement>();
        if (width > 0) le.preferredWidth = width;
        le.flexibleWidth = flex;
        return t;
    }

    static void SetLabel(Button b, string s)
    {
        var t = b.GetComponentInChildren<TextMeshProUGUI>();
        if (t != null) t.text = s;
    }
}
