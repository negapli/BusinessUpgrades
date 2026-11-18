using Il2CppFishNet;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.GameTime;
using Il2CppScheduleOne.Levelling;
using Il2CppScheduleOne.Money;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.Product;
using Il2CppScheduleOne.Property;
using S1API.Entities;
using S1API.Entities.Appearances.AccessoryFields;
using S1API.Entities.Appearances.BodyLayerFields;
using S1API.Entities.Appearances.CustomizationFields;
using S1API.Entities.Appearances.FaceLayerFields;
using UnityEngine;

namespace BusinessUpgrades;

// Saul Mediuham, the Laundromat's old owner (the feds took it). He texts the player once they own the Laundromat and
// reach Hustler, then gives the T1 quest in person: 10 weed + $2,500 cash "for his lawyer" installs his Upgrades app.
// State lives in the host's Upgrades list ("laundromat:saul_met", "laundromat:q1"/"t1"), synced like the upgrades.
public sealed class Saul : NPC
{
    const string Biz = "laundromat", Talk = "SaulTalk";
    public const int WeedUnits = 10;
    public const float LawyerCash = 2500f;
    // ponytail: placeholder until the user picks his spot in game (F8 logs the floor under the player)
    internal static Vector3 Spot = new(0f, 0f, 0f);
    internal static Quaternion Facing = Quaternion.identity;

    public override bool IsPhysical => true;

    protected override void ConfigurePrefab(NPCPrefabBuilder builder)
    {
        builder.WithIdentity("businessupgrades_saul", "Saul", "Mediuham") // stable save ID: never change it
            .WithSpawnPosition(Spot, Facing)
            .WithRelationshipDefaults(r => r.WithDelta(1.0f).SetUnlocked(true).SetUnlockType(NPCRelationship.UnlockType.DirectApproach));
    }

    protected override void OnCreated()
    {
        base.OnCreated();
        Dress(Appearance).Build();
        Schedule.Disable(); // he hangs around his old laundromat

        Dialogue.BuildAndRegisterContainer(Talk, c =>
        {
            c.AddNode("ENTRY", "...", ch => ch
                .Add("ABOUT", "About the laundromat...", "ASK1")
                .Add("LEAVE", "Nevermind", "EXIT"));
            c.AddNode("STRANGER", "Do I know you? Beat it, I'm busy doing nothing.");
            c.AddNode("ASK1", $"So you came. Here's the deal: {WeedUnits} weed to take the edge off, and ${LawyerCash:N0} cash for my lawyer. " +
                              "Do that, and I put my app on the old computer.", ch => ch
                .Add("GIVE1", $"Here: {WeedUnits} weed and ${LawyerCash:N0}", "THANKS1")
                .Add("LATER1", "Not now", "EXIT"));
            c.AddNode("THANKS1", "Pleasure doing business. The app's on the computer now: hit \"Upgrades\" up top. Don't break it.");
            c.AddNode("NOWEED", $"That's not {WeedUnits} weed. I can count, you know.");
            c.AddNode("NOCASH", $"My lawyer doesn't take IOUs. ${LawyerCash:N0}, cash.");
            c.AddNode("NOHOST", "Hold on, something's off. Try again in a moment."); // co-op: can't reach the host
            c.AddNode("DONE1", "The app's working? Good. I might have more for you later, if you keep being kind.");
            c.AddNode("EXIT", "Yeah, yeah.");
        });
        Dialogue.OnChoiceSelected("ABOUT", Route);
        Dialogue.OnChoiceSelected("GIVE1", Give1);
        Dialogue.UseContainerOnInteract(Talk);
    }

    void Route()
    {
        if (!Upgrades.Has(Biz, "saul_met")) Dialogue.JumpTo(Talk, "STRANGER");
        else if (Upgrades.Has(Biz, "t1")) Dialogue.JumpTo(Talk, "DONE1");
    }

    // Runs on the player's own machine: their inventory and cash are local. The host then records the quest.
    void Give1()
    {
        if (Net.IsClient && !Net.CanReachHost) { Dialogue.JumpTo(Talk, "NOHOST"); return; } // before taking anything
        var money = NetworkSingleton<MoneyManager>.Instance;
        if (money.cashBalance < LawyerCash) { Dialogue.JumpTo(Talk, "NOCASH"); return; }
        if (WeedCount() < WeedUnits) { Dialogue.JumpTo(Talk, "NOWEED"); return; }
        TakeWeed(WeedUnits);
        money.ChangeCashBalance(-LawyerCash, true, true);
        var why = Net.Buy(Biz, "q1"); // host: unlocks T1 now; client: asks the host
        Mod.Log($"gave Saul {WeedUnits} weed + ${LawyerCash}: {why ?? "ok"}");
    }

    static int Units(Il2CppScheduleOne.ItemFramework.ItemSlot s, out ProductItemInstance p)
    {
        p = s?.ItemInstance?.TryCast<ProductItemInstance>();
        var def = p?.Definition?.TryCast<ProductDefinition>();
        return def != null && def.DrugType == EDrugType.Marijuana ? p.Amount : 0; // units per package (baggie 1, jar 5...)
    }

    static int WeedCount()
    {
        int n = 0;
        foreach (var s in PlayerSingleton<PlayerInventory>.Instance.GetAllInventorySlots())
            n += Units(s, out _) * (s?.Quantity ?? 0);
        return n;
    }

    // Smallest packages first; the last package may overshoot (Saul keeps the change).
    static void TakeWeed(int units)
    {
        var slots = new System.Collections.Generic.List<Il2CppScheduleOne.ItemFramework.ItemSlot>();
        foreach (var s in PlayerSingleton<PlayerInventory>.Instance.GetAllInventorySlots()) if (Units(s, out _) > 0) slots.Add(s);
        slots.Sort((a, b) => Units(a, out _).CompareTo(Units(b, out _)));
        foreach (var s in slots)
        {
            int per = Units(s, out _);
            while (units > 0 && s.Quantity > 0) { s.ChangeQuantity(-1); units -= per; }
            if (units <= 0) return;
        }
    }

    // --- host, once a second: first contact ---

    internal static void Tick()
    {
        if (!InstanceFinder.IsServer || Upgrades.Instance == null || Upgrades.Has(Biz, "saul_met")) return;
        var time = NetworkSingleton<TimeManager>.Instance;
        var lm = NetworkSingleton<LevelManager>.Instance;
        if (time == null || lm == null || lm.Rank < ERank.Hustler || time.CurrentTime < 700 || time.CurrentTime >= 1200) return;
        bool owned = false;
        foreach (var b in Business.Businesses) if (b.PropertyCode == Biz && b.IsOwned) owned = true;
        if (!owned || Get<Saul>() is not Saul s) return;
        Upgrades.Instance.Owned.Add(Biz + ":saul_met");
        s.SendTextMessage("Hello, you don't know me, but I'm starting to know you. I hear you bought my old laundromat. Freaking feds took everything from me.");
        s.SendTextMessage("Well... not everything. I might still have the USB stick for the old computer. If you're kind to me, I may or may not install the app I made. Come see me at the laundromat.");
        Mod.Log("Saul: first contact sent");
    }

    // Host, every 0.5 s: keep him at his spot (S1API spawns him at 0,0,0 until finalized; the navmesh may move him).
    internal static void KeepInPlace()
    {
        if (!InstanceFinder.IsServer || Spot == Vector3.zero || Get<Saul>() is not Saul m || m.gameObject == null) return;
        if (m.Schedule.IsEnabled) m.Schedule.Disable();
        if (Vector3.Distance(m.Position, Spot) < 1.5f) return;
        var npc = m.gameObject.GetComponent<Il2CppScheduleOne.NPCs.NPC>();
        var agent = npc?.Movement._agent;
        if (agent != null) agent.enabled = false; // the agent snaps him back to the nearest navmesh point otherwise
        m.Transform.SetPositionAndRotation(Spot, Facing);
        Mod.Log($"Saul placed at {Spot}");
    }

    // Only paths that exist in the game's Resources (checked in resources.assets).
    static S1API.Entities.NPCAppearance Dress(S1API.Entities.NPCAppearance a) => a
            .Set<Gender>(0f)
            .Set<Height>(1.0f)
            .Set<Weight>(0.55f)
            .Set<SkinColor>(new Color32(224, 182, 150, 255))
            .Set<HairStyle>("Avatar/Hair/Receding/Receding")
            .Set<HairColor>(new Color(0.35f, 0.32f, 0.3f))
            .Set<EyeBallTint>(Color.white)
            .Set<PupilDilation>(0.5f)
            .Set<EyeLidRestingStateLeft>((0.4f, 0.4f))
            .Set<EyeLidRestingStateRight>((0.4f, 0.4f))
            .Set<EyebrowScale>(1.0f)
            .Set<EyebrowThickness>(0.8f)
            .WithFaceLayer<Face>("Avatar/Layers/Face/Face_SlightFrown", Color.black)
            .WithFaceLayer<Eyes>("Avatar/Layers/Face/TiredEyes", new Color(0.3f, 0.2f, 0.2f))
            .WithFaceLayer<FacialHair>("Avatar/Layers/Face/FacialHair_Stubble", new Color(0.3f, 0.28f, 0.26f))
            .WithBodyLayer<Shirts>("Avatar/Layers/Top/RolledButtonup", new Color(0.75f, 0.72f, 0.62f))
            .WithBodyLayer<Pants>("Avatar/Layers/Bottom/Jeans", new Color(0.2f, 0.24f, 0.32f))
            .WithAccessoryLayer<FacialHairAccessory>("Avatar/Accessories/FacialHair/Chevron/Chevron", new Color(0.3f, 0.28f, 0.26f))
            .WithAccessoryLayer<Feet>("Avatar/Accessories/Feet/DressShoes/DressShoes", new Color(0.15f, 0.1f, 0.07f));
}
