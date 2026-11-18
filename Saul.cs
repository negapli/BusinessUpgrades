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

// Saul Mediuham, the Laundromat's old owner (the feds took it). He lives in the Corner Store next door (knock: he
// comes out). He texts the player once they own the Laundromat and reach Hustler, and gives one quest per tier
// (Trees: Tier.Request): T1's handover (with his lawyer's cash) installs the Upgrades app and unlocks T1; later
// handovers open the tier for its bank unlock. He texts each next quest in the morning once it's available.
// State lives in the host's Upgrades list ("laundromat:saul_met", "offerN", "qN"), synced like the upgrades.
public sealed class Saul : NPC
{
    const string Biz = "laundromat", Talk = "SaulTalk";
    internal static readonly Vector3 Spot = new(-130.33f, -3.90f, 81.54f); // spawn: outside the Corner Store's door (picked with F8)
    internal static readonly Quaternion Facing = Quaternion.Euler(0f, 223f, 0f);

    // What he says, per quest (index = tier): the ask, the thanks, and the text that offers it (T1's offer = first contact).
    static readonly string[] Ask =
    {
        "So you came. Here's the deal: {req} for my lawyer and my nerves. Do that, and I put my app on the old computer.",
        "My nerves are shot, the feds are still sniffing around. {req}. Then I'll open up the next part of the app.",
        "Long nights, short money. I need something to keep me going: {req}.",
        "My old contacts are back in town, and they're thirsty. {req}. Do this and I'll show you how the big boys wash money.",
    };
    static readonly string[] Thanks =
    {
        "Pleasure doing business. The app's on the computer now: hit \"Upgrades\" up top. Don't break it.",
        "That's the stuff. Tier 2's open in the app; the unlock's on you.",
        "Now we're talking. Tier 3's open in the app.",
        "You're the real deal. Tier 4's open in the app. Don't get greedy.",
    };
    static readonly string[] Offer =
    {
        null,
        "The app's treating you well? Good. I need another favor. Knock when you have time.",
        "You've come a long way. I've got something else for you. Corner Store, you know where.",
        "Big things are moving. Come see me, we need to talk.",
    };

    public override bool IsPhysical => true;

    protected override void ConfigurePrefab(NPCPrefabBuilder builder)
    {
        builder.WithIdentity("businessupgrades_saul", "Saul", "Mediuham") // stable save ID: never change it
            .WithSpawnPosition(Spot, Facing)
            .WithRelationshipDefaults(r => r.WithDelta(1.0f).SetUnlocked(true).SetUnlockType(NPCRelationship.UnlockType.DirectApproach))
            // Inside the Corner Store all day; knocking on its door calls him out (the game's StaticDoor knock -> summon, ~8 s).
            .WithSchedule(plan => plan.StayInBuilding(S1API.Map.Building.Get<S1API.Map.Buildings.CornerStore>(), 0, 1439));
    }

    protected override void OnCreated()
    {
        base.OnCreated();
        Dress(Appearance).Build();

        var tiers = Trees.All[Biz];
        Dialogue.BuildAndRegisterContainer(Talk, c =>
        {
            c.AddNode("ENTRY", "...", ch => ch
                .Add("ABOUT", "About the laundromat...", "STRANGER")
                .Add("LEAVE", "Nevermind", "EXIT"));
            c.AddNode("STRANGER", "Do I know you? Beat it, I'm busy doing nothing.");
            for (int i = 0; i < tiers.Length; i++)
            {
                var r = tiers[i].Request;
                if (r == null) continue;
                c.AddNode("ASK" + i, Ask[i].Replace("{req}", r.Text), ch => ch
                    .Add("GIVE" + i, "Here: " + r.Text, "THANKS" + i)
                    .Add("LATER" + i, "Not now", "EXIT"));
                c.AddNode("THANKS" + i, Thanks[i]);
                c.AddNode("SHORT" + i, $"That's not what I asked for. {r.Text}. I can count, you know.");
            }
            c.AddNode("NOCASH", "My lawyer doesn't take IOUs. Cash.");
            c.AddNode("NOHOST", "Hold on, something's off. Try again in a moment."); // co-op: can't reach the host
            c.AddNode("WAIT", "Nothing for now. Keep the app running, I'll text you.");
            c.AddNode("ALLDONE", "We're square. Go wash your money.");
            c.AddNode("EXIT", "Yeah, yeah.");
        });
        Dialogue.OnChoiceSelected("ABOUT", Route);
        for (int i = 0; i < tiers.Length; i++)
        {
            int k = i;
            if (tiers[i].Request != null) Dialogue.OnChoiceSelected("GIVE" + i, () => Give(k));
        }
        Dialogue.UseContainerOnInteract(Talk);
    }

    /// The tier whose quest is open now (offered, not done), -1 = none yet, Length = all done.
    static int OpenQuest()
    {
        var tiers = Trees.All[Biz];
        for (int i = 0; i < tiers.Length; i++)
        {
            if (tiers[i].Request == null || Upgrades.Has(Biz, "q" + (i + 1))) continue;
            return Upgrades.Has(Biz, i == 0 ? "saul_met" : "offer" + (i + 1)) ? i : -1;
        }
        return tiers.Length;
    }

    void Route()
    {
        if (!Upgrades.Has(Biz, "saul_met")) return; // STRANGER
        int q = OpenQuest();
        Dialogue.JumpTo(Talk, q < 0 ? "WAIT" : q >= Trees.All[Biz].Length ? "ALLDONE" : "ASK" + q);
    }

    // Runs on the player's own machine: their inventory and cash are local. The host then records the quest.
    void Give(int i)
    {
        var r = Trees.All[Biz][i].Request;
        if (Net.IsClient && !Net.CanReachHost) { Dialogue.JumpTo(Talk, "NOHOST"); return; } // before taking anything
        var money = NetworkSingleton<MoneyManager>.Instance;
        if (money.cashBalance < r.Cash) { Dialogue.JumpTo(Talk, "NOCASH"); return; }
        if (Count(r) < r.Units) { Dialogue.JumpTo(Talk, "SHORT" + i); LogProducts(r); return; }
        Take(r, r.Units);
        if (r.Cash > 0) money.ChangeCashBalance(-r.Cash, true, true);
        var why = Net.Buy(Biz, "q" + (i + 1)); // host: records it now; client: asks the host
        Mod.Log($"gave Saul {r.Text}: {why ?? "ok"}");
    }

    /// Units in one inventory slot's package that match the request (baggie 1, jar 5...), else 0.
    static int Units(Il2CppScheduleOne.ItemFramework.ItemSlot s, Request r)
    {
        var p = s?.ItemInstance?.TryCast<ProductItemInstance>();
        var def = p?.Definition?.TryCast<ProductDefinition>();
        if (def == null) return 0;
        if (r.Drug.HasValue && def.DrugType != r.Drug.Value) return 0;
        if (p.Quality < r.MinQuality) return 0;
        if (r.Effect != null)
        {
            bool has = false;
            foreach (var e in def.Properties) if (e != null && e.Name == r.Effect) has = true;
            if (!has) return 0;
        }
        return p.Amount;
    }

    // Why a handover fell short: what the inventory holds (drug, quality, effect names as the game spells them).
    static void LogProducts(Request r)
    {
        Mod.Log($"Saul: short for \"{r.Text}\" ({Count(r)}/{r.Units} matching units). Inventory products:");
        foreach (var s in PlayerSingleton<PlayerInventory>.Instance.GetAllInventorySlots())
        {
            var p = s?.ItemInstance?.TryCast<ProductItemInstance>();
            var def = p?.Definition?.TryCast<ProductDefinition>();
            if (def == null) continue;
            var fx = new System.Collections.Generic.List<string>();
            foreach (var e in def.Properties) if (e != null) fx.Add(e.Name);
            Mod.Log($"  {s.Quantity} x {def.Name} ({p.Amount}/pack) {def.DrugType} {p.Quality} [{string.Join(", ", fx)}]");
        }
    }

    static int Count(Request r)
    {
        int n = 0;
        foreach (var s in PlayerSingleton<PlayerInventory>.Instance.GetAllInventorySlots())
            n += Units(s, r) * (s?.Quantity ?? 0);
        return n;
    }

    // Smallest packages first; the last package may overshoot (Saul keeps the change).
    static void Take(Request r, int units)
    {
        var slots = new System.Collections.Generic.List<Il2CppScheduleOne.ItemFramework.ItemSlot>();
        foreach (var s in PlayerSingleton<PlayerInventory>.Instance.GetAllInventorySlots()) if (Units(s, r) > 0) slots.Add(s);
        slots.Sort((a, b) => Units(a, r).CompareTo(Units(b, r)));
        foreach (var s in slots)
        {
            int per = Units(s, r);
            while (units > 0 && s.Quantity > 0) { s.ChangeQuantity(-1); units -= per; }
            if (units <= 0) return;
        }
    }

    // --- host, once a second: texts (first contact, then each next quest), mornings only, one per tick ---

    internal static void Tick()
    {
        if (!InstanceFinder.IsServer || !Upgrades.Ready || Get<Saul>() is not Saul s) return;
        var time = NetworkSingleton<TimeManager>.Instance;
        var lm = NetworkSingleton<LevelManager>.Instance;
        if (time == null || lm == null || time.CurrentTime < 700 || time.CurrentTime >= 1200) return;
        var tiers = Trees.All[Biz];
        if (!Upgrades.Has(Biz, "saul_met"))
        {
            bool owned = false;
            foreach (var b in Business.Businesses) if (b.PropertyCode == Biz && b.IsOwned) owned = true;
            if (!owned || lm.Rank < tiers[0].Rank) return;
            Upgrades.Instance.Owned.Add(Biz + ":saul_met");
            s.SendTextMessage("Hello, you don't know me, but I'm starting to know you. I hear you bought my old laundromat. Freaking feds took everything from me.");
            s.SendTextMessage("Well... not everything. I might still have the USB stick for the old computer. If you're kind to me, I may or may not install the app I made. I crash at the Corner Store next to the laundromat: knock.");
            Mod.Log("Saul: first contact sent");
            return;
        }
        for (int i = 1; i < tiers.Length; i++)
        {
            if (tiers[i].Request == null || Upgrades.Has(Biz, "offer" + (i + 1))) continue;
            if (!Trees.HasTier(Biz, i - 1) || lm.Rank < tiers[i].Rank) return; // in order: the next one isn't due yet
            Upgrades.Instance.Owned.Add(Biz + ":offer" + (i + 1));
            s.SendTextMessage(Offer[i]);
            Mod.Log($"Saul: quest {i + 1} offered");
            return;
        }
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
