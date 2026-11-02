# Tech-Forge's Modlist Rescue

**One-click Skyrim Special Edition version downgrader / upgrader for modders.**

A Steam update changes your Skyrim SE version and suddenly SKSE and half your modlist stop
loading. **Modlist Rescue** puts Skyrim back on the exact version your mods need —
automatically. Pick a version, confirm, and it finds Steam and your Skyrim install,
downloads the correct game files straight from Steam with SteamCMD, and copies them into
your game folder.

No manual console commands, no hunting for depot IDs, no dragging files around.

---

## Download

**➡ Get the latest `ModlistRescue.exe` from the [Releases page](../../releases/latest).**

> **Heads up — SmartScreen / antivirus warning is expected.**
> This is a free, unsigned tool that downloads official game files through Steam, so Windows
> SmartScreen may show "Windows protected your PC" (click **More info → Run anyway**) and some
> antivirus may flag it. That's a **false positive** caused by the tool downloading and running
> SteamCMD. The full source is right here in this repo, and every release is built automatically
> by GitHub Actions from that source — so you can verify exactly what you're running.

---

## Features

- **Choose your version** — install Skyrim SE **1.5.97**, **1.6.640**, or **1.6.1170** from a simple menu.
- **Fully automatic** — auto-detects your Steam path and your Skyrim Special Edition folder across every Steam library on your PC.
- **Official files only** — downloads from Steam itself via SteamCMD. Nothing is bundled, cracked, or redistributed.
- **Ownership checked** — verifies the signed-in Steam account actually owns Skyrim SE before doing anything.
- **Automatic overwrite** — replaces the needed game files with no "replace this file?" prompts.
- **Remembers your login** — SteamCMD asks for your Steam password only the first time (plus one Steam Guard code if you use it). Every run after that needs nothing.
- **Full logging** — writes a timestamped log into your Skyrim folder so you can see exactly what happened.
- **Single portable EXE** — no installer. Run it from any folder and delete it when done.
- **Admin only if needed** — if your game lives in a protected folder, it re-launches with the rights to finish, reusing the files it already downloaded.

---

## Which version should I pick?

| Version | What it is |
|---------|------------|
| **1.5.97** | "Special Edition", the classic pre-Anniversary build. The most widely supported modding target; most older guides, SKSE builds and mods assume this version. |
| **1.6.640** | "Anniversary Edition", a long-stable AE build. A common target for AE modlists not updated for the newest patches. |
| **1.6.1170** | "Anniversary Edition", a recent build. Use if your modlist specifically targets current AE. |

*Not sure? Check your modlist's requirements — it will almost always name the game version it
needs. If you're starting fresh, 1.5.97 is the safest bet.*

---

## How to use

1. Make sure Skyrim Special Edition is installed through Steam and you're logged in to the account that owns it.
2. Close Skyrim.
3. Run **ModlistRescue.exe**.
4. Pick the version you want and confirm.
5. On the first run, type your Steam password (and Steam Guard code if asked) into SteamCMD when prompted.
6. Wait for it to download and copy the files. Done.

**Stop Steam re-updating it:** in Steam, right-click Skyrim Special Edition → Properties →
Updates, and set it to **"Only update this game when I launch it"**. Then always start the game
through SKSE / your mod manager, not the Steam Play button. (Running "Verify integrity of game
files" in Steam will undo the change and restore the newest version.)

---

## Requirements

- Windows 10 or 11 (older Windows works too, with plain console colours).
- A legitimate Steam copy of Skyrim Special Edition.
- An internet connection (downloads several GB from Steam).

SteamCMD is fetched automatically from Valve on first run — nothing extra to install.

---

## Credits & Disclaimer

Heads up: this is just a fan-made tool, not official and nothing to do with Valve, Bethesda or
[SteamDB](https://steamdb.info). It doesn't come with any game files — it grabs them from Steam
through your own account for a copy you actually own. Thanks to SteamDB for being a handy place
to find the version IDs (which are factual Steam identifiers, also obtainable via DepotDownloader
or SteamKit). "Steam" and "Skyrim" belong to their respective owners. Provided as-is, no warranty —
use at your own risk and back up your game files before changing versions.

## License

[MIT](LICENSE) © 2026 Alex Morton (Tech-Forge)
