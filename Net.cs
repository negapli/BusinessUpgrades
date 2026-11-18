using System;
using System.Collections.Generic;
using Il2CppFishNet;
using SteamNetworkLib;

namespace BusinessUpgrades;

// Co-op over the game's Steam lobby, with lobby data only (no P2P: every SteamNetworkLib client reads all P2P packets
// on channels 0-3, so mods using it steal each other's messages).
//  - host -> everyone: lobby data "BusinessUpgrades_state" = the owned list ("laundromat:t1,laundromat:extra_drum").
//  - client -> host:   the client's own player data "BusinessUpgrades_cmd" = "<cmdId>|<business>|<id>" (one at a time).
// The host runs every purchase through Trees.TryBuy (checks, bank payment, save) and publishes the result.
// Singleplayer = host with no lobby.
static class Net
{
    const string StateKey = "BusinessUpgrades_state", CmdKey = "BusinessUpgrades_cmd";

    static SteamNetworkClient steam;
    static bool Online => steam != null && steam.IsInLobby;
    /// A co-op client: in someone else's lobby and not the server. (FishNet alone isn't enough: while a save loads the
    /// host briefly reports IsServer false, and the host then overwrote its loaded list with the lobby's empty copy.)
    public static bool IsClient => Online && !steam.IsHost && !InstanceFinder.IsServer;
    /// A co-op client can ask the host for anything (and should hand over items) only while it sees the host.
    public static bool CanReachHost => Online && steam.GetHostMember() != null;

    // --- setup: the library must start before the game joins a lobby (it watches Steam's lobby-entered event) ---

    static float nextTry;
    public static void Update()
    {
        if (steam != null || UnityEngine.Time.time < nextTry) return;
        nextTry = UnityEngine.Time.time + 2f; // Steam may not be ready during the first frames
        var c = new SteamNetworkClient();
        if (!c.TryInitialize(out var error)) { Mod.Log($"net: SteamNetworkLib not ready ({error?.Message})"); return; }
        steam = c; // no ProcessIncomingMessages: we use no P2P, and reading packets would take other mods' messages
        Mod.Log("net: SteamNetworkLib ready");
    }

    public static void Shutdown() { steam?.Dispose(); steam = null; }

    // --- buying ---

    /// The "biz:id" a client is waiting for the host to confirm, or null.
    public static string Pending { get; private set; }
    static string pendingCmd;
    static float pendingAt;

    /// Returns null when done (host) or sent (client), else why not.
    public static string Buy(string biz, string id)
    {
        if (!IsClient) return Trees.TryBuy(biz, id);
        if (Pending != null) return "Waiting for the host...";
        if (!Online || steam.GetHostMember() == null) return "Not connected to the host";
        pendingCmd = Guid.NewGuid().ToString("N");
        Pending = biz + ":" + id; pendingAt = UnityEngine.Time.time;
        steam.SetMyData(CmdKey, $"{pendingCmd}|{biz}|{id}");
        Mod.Log($"asked the host for {Pending}");
        return null;
    }

    // --- once a second ---

    public static void Tick()
    {
        // A co-op client never loads the save, so S1API never creates Upgrades there: make the mirror of the host's list.
        if (IsClient && !Upgrades.Ready) { if (Upgrades.Instance == null) new Upgrades(); Upgrades.Instance.Owned = new(); lastState = null; Upgrades.Ready = true; } // fresh mirror per join
        if (!Online) { lastPublished = lastState = null; handled.Clear(); return; } // a new lobby must get the list even if unchanged
        if (Upgrades.Instance == null || !Upgrades.Ready) return;
        try
        {
            if (IsClient) Receive();
            else if (InstanceFinder.IsServer) { Publish(); ReadCommands(); }
        }
        catch (Exception e) { Mod.Log($"net: {e.Message}"); }
    }

    // --- host ---

    static string lastPublished;
    static void Publish()
    {
        string data = string.Join(",", Upgrades.Instance.Owned);
        if (data == lastPublished) return;
        lastPublished = data;
        steam.SetLobbyData(StateKey, data);
    }

    static readonly Dictionary<ulong, string> handled = new(); // member -> last command id handled
    static void ReadCommands()
    {
        foreach (var m in steam.GetLobbyMembers())
        {
            if (m.IsLocalPlayer) continue;
            var cmd = steam.GetPlayerData(m.SteamId, CmdKey);
            if (string.IsNullOrEmpty(cmd)) continue;
            var parts = cmd.Split('|');
            ulong who = m.SteamId.m_SteamID;
            if (parts.Length != 3 || handled.TryGetValue(who, out var last) && last == parts[0]) continue;
            handled[who] = parts[0];
            var why = Trees.TryBuy(parts[1], parts[2]); // checks everything: a modified client could send anything
            Mod.Log($"client {who} ({m.DisplayName}): {parts[1]}:{parts[2]} -> {why ?? "bought"}");
        }
    }

    // --- client ---

    static string lastState;
    static void Receive()
    {
        var data = steam.GetLobbyData(StateKey) ?? "";
        if (data != lastState)
        {
            lastState = data;
            Upgrades.Instance.Owned = data == "" ? new() : new List<string>(data.Split(','));
            Mod.Log($"mirror: {Upgrades.Instance.Owned.Count} entries from the host");
            Effects.ApplyAll();
            Panel.RefreshOpen();
        }
        if (Pending == null) return;
        bool done = Upgrades.Instance.Owned.Contains(Pending);
        if (!done && UnityEngine.Time.time - pendingAt < 10f) return;
        if (!done) Mod.Log($"the host didn't confirm {Pending} (refused or not received)");
        Pending = null;
        steam.SetMyData(CmdKey, ""); // so the host never replays it
        Panel.RefreshOpen(done ? null : "The host refused it");
    }
}
