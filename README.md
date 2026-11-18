# BusinessUpgrades

A Schedule I mod: upgrade your businesses through their laundering computer. Each business gets its own upgrade tree, unlocked with money, your rank and quests from the people around it. Higher tiers launder more money, faster, pay better, and even stock supplies for you.

**Pre-release: the Laundromat is the first business with a tree.** The Post Office, Car Wash, Taco Ticklers and Hyland Medical come later.

## The Laundromat
Once you own the Laundromat and reach **Hustler**, its old owner **Saul Mediuham** texts you one morning. The feds took everything from him... almost. He lives in the **Corner Store** next door: knock on its door and he comes out.

Do him a favor and he installs his app on the Laundromat's computer: an **Upgrades** button appears in its title bar. Each tier needs one of Saul's favors (he texts you when he's ready), and the tiers after the first also cost money from your bank.

| Tier | Saul wants | Unlock | Upgrades (bank) |
|---|---|---|---|
| 1. Back room | 10 weed and $2,500 cash (rank Hustler) | the cash for Saul | **Extra drum**: capacity +$1,000 ($1,500) · **Fast cycle**: loads take 18 h ($2,000) |
| 2. Old friends | 20 meth, Standard quality or better | $7,500 | **Coin tray**: cash left in your racks there starts laundering at 08:00 ($5,000) · **Bigger drums**: capacity +$2,000 ($6,000) · **Supply closet**: 10 Mouth Wash a day ($4,000) |
| 3. Night shift | 15 of anything Energizing (rank Bagman) | $20,000 | **Night shift**: loads take 12 h ($12,000) · **Cleaner books**: laundered money +10% ($15,000) · **Bulk supplier**: 40 Mouth Wash a day ($12,000) |
| 4. Industrial | 50 Heavenly cocaine, Energizing and Long faced (rank Block Boss) | $75,000 | **Industrial machines**: capacity +$6,000, loads take 8 h ($40,000) · **Offshore account**: laundered money +10% more ($50,000) · **Wholesale account**: 120 Mouth Wash a day ($30,000) |

- Capacity bonuses are added on top of the Laundromat's capacity ($2,000 in vanilla, or whatever another mod sets).
- Mouth Wash and the Coin tray use the **storage racks you place inside the Laundromat**: any size, as many as you like. Mouth Wash that doesn't fit is lost for the day.
- Laundering bonuses (Cleaner books, Offshore account) are paid to your bank with each finished load.

## Requirements
- Schedule I **0.4.7** (IL2CPP, the default Steam branch)
- [MelonLoader](https://github.com/LavaGang/MelonLoader) 0.7.x
- [S1API](https://thunderstore.io/c/schedule-i/p/ifBars/S1API_Forked/) 3.2.3 (IL2CPP)
- [SteamNetworkLib (Il2Cpp)](https://thunderstore.io/c/schedule-i/p/ifBars/SteamNetworkLib_Il2Cpp/) 1.6.0

## Install
1. Install MelonLoader, S1API and SteamNetworkLib (a mod manager like r2modman does this for you).
2. Put `BusinessUpgrades.dll` in `<Schedule I>/Mods/`.

Uninstall: delete `Mods/BusinessUpgrades.dll`. Your upgrades are stored in the save's `Modded` folder and are ignored without the mod; the Laundromat goes back to vanilla.

## Multiplayer (untested)
Built for co-op from the start: every player needs the same BusinessUpgrades version, S1API and SteamNetworkLib. The host owns the upgrades and Saul's progress; any player can talk to Saul, hand over his favors and buy upgrades (the shared bank pays). **Not tested with two players yet**: please report anything odd.

## Compatibility
Other mods that change laundering capacity or time (MoreRealisticLaundering, LaundryApp, BusinessEmployment, capacity/speed tweaks) may overlap: this mod adds its capacity bonus on top of the current value and sets the duration of every load.

## Troubleshooting
The log is `<Schedule I>/MelonLoader/Latest.log`; BusinessUpgrades lines start with `[BusinessUpgrades]`. Please attach it to bug reports.

## Building from source
.NET 6 SDK, a Schedule I install with MelonLoader (run the game once so `MelonLoader/Il2CppAssemblies` exists), S1API and SteamNetworkLib installed.
```
dotnet build -c Release -p:GameDir="/path/to/Schedule I"
```
The build copies `BusinessUpgrades.dll` into `<game>/Mods/`.

## Credits
- [MelonLoader](https://github.com/LavaGang/MelonLoader) (LavaGang)
- [S1API](https://github.com/ifBars/S1API) and [SteamNetworkLib](https://github.com/ifBars/SteamNetworkLib) by ifBars
- No game files or game assets are included; Saul is built from the game's own avatar parts at runtime, and the upgrades screen from the computer's own interface.

## AI disclosure
- **Code and text** (including Saul's dialogue and texts): written by an AI coding agent (Claude Code, Anthropic's Claude Opus) under the direction of pligane, who designed the mod and play-tested every feature in game.
- **Saul's look**: built at runtime from the game's own avatar parts. No AI-generated images or audio.

## License
MIT, see [LICENSE](LICENSE).
