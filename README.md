<p align="center">
  <img width="128" src=".github/assets/logo.png" alt="SCSFix logo">
</p>
<h1 align="center">SCSFix</h1>
<p align="center">
  <strong>Shader Compilation Stutter Fix.</strong> SCSFix compiles your games' shaders into your GPU driver's
  cache before you play, so the game doesn't stop to compile them mid-game. Free and open source, for Windows.
</p>

> [!NOTE]
> SCSFix is a fork of [SCSKiller](https://github.com/BlueHeisenberg/SCSKiller) by BlueHeisenberg (GPL-3.0-or-later),
> made to cover games SCSKiller can't compile yet. It has its own name, data folder (`%LOCALAPPDATA%\SCSFix\`) and
> recorder file names, so it installs beside SCSKiller, and it never updates itself from SCSKiller's releases.
> The community database and Patreon sign-in belong to SCSKiller's author; SCSFix has no connection to them
> beyond the code it shares.
> Releases of this fork: [Releases](https://github.com/faisalkindi/SCSFix/releases). Bugs and games it can't do:
> [Issues](https://github.com/faisalkindi/SCSFix/issues).

<p align="center">
  <img src=".github/assets/library.webp" alt="SCSFix's library: games grouped by store, with shader and pipeline counts, cache size, compile time, a status such as Needs rebuilding or Warmed, and Play buttons.">
</p>

## Why

When a game meets a new effect, your GPU driver has to compile its shader right then, and the frame waits for it.
That's the hitch you feel as shader compilation stutter.

SCSFix reads the shaders a game ships with, works out the pipelines it will build, and compiles them into your
driver's shader cache ahead of time, from its own process and with the game closed. When you launch the game, they're
already there.

## Results

About 5 minutes of play per game, cold driver cache → compiled with SCSFix. A stutter is a shader compile the game had to wait 20 ms or more for.

<table>
  <tr><th>Game</th><th>GPU</th><th>Measurement</th><th>Cold</th><th>With SCSFix</th></tr>
  <tr><td rowspan="4">Final Fantasy VII Rebirth</td><td rowspan="2">NVIDIA<br>RTX 5090</td><td>Stutters (≥ 20 ms)</td><td>19</td><td><b>0</b></td></tr>
  <tr><td>Worst stall</td><td>76 ms</td><td><b>5 ms</b></td></tr>
  <tr><td rowspan="2">AMD<br>Ryzen AI Max+ 395 (Strix Halo)</td><td>Stutters (≥ 20 ms)</td><td>162</td><td><b>0</b></td></tr>
  <tr><td>Worst stall</td><td>291 ms</td><td><b>7 ms</b></td></tr>
  <tr><td rowspan="2">Silent Hill: Townfall</td><td rowspan="2">NVIDIA<br>RTX 5090</td><td>Stutters (≥ 20 ms)</td><td>24</td><td><b>0</b></td></tr>
  <tr><td>Worst stall</td><td>347 ms</td><td><b>8 ms</b></td></tr>
  <tr><td rowspan="4">Hogwarts Legacy</td><td rowspan="2">NVIDIA<br>RTX 5090</td><td>Stutters (≥ 20 ms)</td><td>1,014</td><td><b>87</b></td></tr>
  <tr><td>Worst stall</td><td>2,400 ms</td><td><b>100 ms</b></td></tr>
  <tr><td rowspan="2">AMD<br>Ryzen AI Max+ 395 (Strix Halo)</td><td>Stutters (≥ 20 ms)</td><td>27,743</td><td><b>206</b></td></tr>
  <tr><td>Worst stall</td><td>2,146 ms</td><td><b>104 ms</b></td></tr>
  <tr><td rowspan="2">Tiny Tina's Wonderlands</td><td rowspan="2">NVIDIA<br>RTX 5090</td><td>Stutters (≥ 20 ms)</td><td>583</td><td><b>0</b></td></tr>
  <tr><td>Worst stall</td><td>315 ms</td><td><b>13 ms</b></td></tr>
  <tr><td>Star Wars Jedi: Survivor</td><td>NVIDIA<br>RTX 5090</td><td>Stutters (≥ 20 ms)</td><td>9,038</td><td><b>11</b></td></tr>
</table>

Measured with SCSFix's pipeline recorder. Your numbers will vary by game, GPU and driver.

- **Fixes:** shader compilation stutter.
- **Can't fix:** traversal and streaming stutter, or hitches with other causes.
- **Now and then:** a shader it couldn't find or record can still compile in game.

## Features

- **NVIDIA and AMD.** DirectX 12 games, and DirectX 11 games on NVIDIA.
- **Finds your games** in Steam, Epic Games, EA app, GOG, Ubisoft Connect, Xbox (PC), Battle.net, PURPLE, HoYoPlay and Gaijin.
- **Knows which games stutter:** the library puts known offenders on top, with the reason.
- **Recompiles after driver updates.** A driver update clears the shader cache; SCSFix notices and compiles again,
  on its own if you let it.
- **Optional recorder** for games whose pipelines can't be worked out from their files: play a few minutes with it on,
  then compile. It can also record alongside a mod that replaces shaders.
- **Games SCSKiller can't read** (encrypted, packed, a package format it doesn't know) aren't a dead end here: they
  go to Needs recording, and the recorder, for DirectX 12 and on NVIDIA also DirectX 11 (it goes in as `d3d11.dll`),
  captures what the game creates while you play. Games with anti-cheat still can't be recorded.
- **Frame times per session.** With the recorder on, a game's page graphs your last session and tells the shader
  stutters apart from other hitches. Frame times stay on your PC.
- **Play button** in the Library and on a game's page: starts the game through its store.
- **Community shader hash database** for Patreon supporters: other players' recordings, so your games are covered
  without recording them first. Sharing your own is opt-in and anonymous: shader hashes only.
- **Never touches anti-cheat games** beyond reading their files: no launching, no injecting, no recorder. The one
  exception is opt-in, per game and confirmed every time: an offline session without EasyAntiCheat for ELDEN RING and
  ARMORED CORE VI, at your own risk.

## Install

| Requirement | |
|---|---|
| OS | Windows 10 (version 2004) or 11, 64-bit |
| GPU | NVIDIA or AMD |
| Games | DirectX 12; DirectX 11 on NVIDIA |

Intel GPUs aren't supported yet: there is no Intel GPU to test on.

Download `SCSFix-Setup.exe` from [Releases](https://github.com/faisalkindi/SCSFix/releases/latest)
and run it. It installs for your user only and needs no admin rights. SCSFix doesn't update itself: download a new
release to update.

Or take `SCSFix-<version>-Portable.zip`, unzip it to any folder you can write to and run `SCSFix.exe`. Before deleting a portable folder, turn recording off for your games, so the recorder leaves the
game folders. A portable folder keeps its settings in `%LOCALAPPDATA%\SCSFix\` like the installed app; to keep them in the folder
instead, put an empty file named `portable.txt` next to `SCSFix.exe` (they go in a `data` folder there; the recorder's small
ledger stays in `%LOCALAPPDATA%\SCSFix\armed`).

<!-- UNSIGNED NOTICE: delete this block once releases are signed. -->
> [!IMPORTANT]
> **Releases aren't code-signed yet**, so Windows SmartScreen will probably show "Windows protected your PC" the first
> time you run it. Click **More info**, then **Run anyway**. You see it once for each download.
>
> To check a download, compare its SHA-256 with the one in
> the release notes (PowerShell: `Get-FileHash <file>`). Smart App Control (Windows 11) blocks unsigned apps outright;
> if it's on, wait for a signed release.
<!-- END UNSIGNED NOTICE -->

## How to use it

1. **Open SCSFix.** It finds your games and the shaders they ship with.
2. **Queue games and press Compile.** "Add all recommended" is a good start. Keep the game closed while it compiles.
3. **Play**, from your store or with the game's Play button, which starts it through its store. You can close
   SCSFix: the game reads the driver's cache on its own. Or leave it open while you play:
   it then checks that the compile reached the cache the game really uses, and with the recorder on, the game's page
   shows afterwards the frame times of your session and whether anything still compiled during it.

If a game says it needs a recording, turn on **Record**, play it for a few minutes, then compile it.

<p align="center">
  <img src=".github/assets/queue.webp" width="49%" alt="The compile queue: one game compiling with pipelines per second, games waiting, one finished.">
  <img src=".github/assets/detail.webp" width="49%" alt="A game's page: the frame times of the last session, with shader compile stutters marked apart from other hitches and loading, and the list of slow frames.">
</p>

## FAQ

**Is it safe with anti-cheat games?** SCSFix never launches them, injects into them or gives them the recorder. It
only reads their files. The one exception is an offline session, for games that run offline without EasyAntiCheat when
their exe is started directly (ELDEN RING, ARMORED CORE VI): allowed per game on its page, confirmed before every launch,
and at your own risk. SCSFix adds `d3d12.dll`, `scsfix.ini`, `scsfix.armed` and `steam_appid.txt` to the game's
folder, starts the game's exe itself (offline, no EasyAntiCheat), and the recorder records only that process: started any
other way, through Steam with EasyAntiCheat too, it records nothing. Steam must be running (offline mode is fine). The
moment the game exits, SCSFix takes those files out again, keeps the recording and the session's frame times, and
checks the folder is as it was, also when SCSFix itself was closed meanwhile; after a crash or power loss it does so
at the next logon or start. If its files are still there when you start the game online, you could be banned.

**What does it write, and where?** Its settings and per-game plans go in `%LOCALAPPDATA%\SCSFix\` (`crash.log` there when the app
can't start or hits an error: attach it to an issue), and the compiled
pipelines go in your driver's own shader cache. Game files are never modified. The one exception is the optional
recorder: it adds `d3d12.dll`, `scsfix.ini` and `scsfix.armed` to the folder of the game you turn it on for, and
turning it off removes exactly those files. `scsfix.armed` says the game's install was checked for anti-cheat; SCSFix
deletes it as soon as anything in the install changes. The recorder decides when the game starts: it records that session
only if the file is there then, and a session already running keeps recording until the game exits. Uninstalling SCSFix
removes these files, and the recording it wrote there, from every game folder.

**Does it touch my drivers or their settings?** No. It compiles through DirectX, the way the game would.

**Do I need an account?** No. Everything the app does on your PC is free. Signing in with Patreon only adds the
supporter features.

**A game says it is running and it isn't.** The note on the game names the processes that hold its exe name ("Running:
Game.exe (process 1234)"). End that one in Task Manager (a leftover, a launcher's helper) and the compile continues.

**Borderlands 4 (or another huge game) is very large to compile.** SCSFix warns in the queue when a compile would add more than
16 GB to the driver's cache. Such a plan is built from every shader in the files; a recording of play plans from what the game
really creates, which is usually far smaller.

**My game isn't detected, or something broke.** Open an [issue](https://github.com/faisalkindi/SCSFix/issues/new/choose)
with the bug or game request template.

## Build from source

Visual Studio 2022 (C++ desktop workload with CMake) and the .NET 10 SDK:

```
cmake -S proxy -B proxy/build -A x64
cmake --build proxy/build --config Release
dotnet build SCSFix.slnx -c Release
```

`build/publish.ps1` builds the whole distribution into `dist\`. How it works in depth: [ARCHITECTURE.md](ARCHITECTURE.md).
How to contribute: [CONTRIBUTING.md](CONTRIBUTING.md). Changes: [CHANGELOG.md](CHANGELOG.md). Security issues:
[SECURITY.md](SECURITY.md), never a public issue.

## Licence

GPL-3.0-or-later ([LICENSE](LICENSE)), with an additional permission for Oodle, the Windows App SDK and graphics driver
libraries ([LICENSE-EXCEPTION.txt](LICENSE-EXCEPTION.txt)). Third-party components:
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

Not affiliated with any GPU maker, engine maker or game publisher. All product names are trademarks of their owners.
