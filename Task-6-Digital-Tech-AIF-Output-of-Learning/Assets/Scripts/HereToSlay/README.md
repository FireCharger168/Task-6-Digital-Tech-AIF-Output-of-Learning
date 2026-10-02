# Here to Slay (Unity 2D)

A digital version of the Here to Slay card game built in C# for Unity 6 (URP 2D).

## Play
Open the project in Unity and press **Play** in any scene (e.g. `Assets/Scenes/SampleScene`).
The game boots itself (`HereToSlayGame` has a `RuntimeInitializeOnLoadMethod`).
Optionally use the menu **Here To Slay > Create Game Scene** to make a dedicated scene.

Title screen → **New Game**: set up to 6 seats (Human / AI / Closed, with names), choose randomised or drafted Party Leaders
and who goes first. Several humans = hot-seat: a "pass the device" screen hides each player's cards from the others.

**Settings** (saved between sessions): game speed, AI thinking time, turn timer (per action), choice timer,
Challenge/Modifier timer, fullscreen, hover zoom, action feed. When your turn timer runs out, your remaining energy is
spent drawing cards and your turn ends; other timers pass or make a sensible choice for you.

## Online multiplayer (host + join code)
Title screen → **Play Online**.
- **Host Game**: this computer runs the game and shows a 10-character join code (e.g. `60N00-H87K1`, which is the
  host's network address and port). Add AI bots with **+ / -**, choose the Party Leader mode, then **Start Game**.
- **Join**: type the host's code (or an address like `192.168.1.20:7777`), then wait in the lobby for the host to start.
- Each player only ever receives their own hand; other hands arrive face-down. Timers come from the host's settings.
  If someone disconnects, a bot takes over their seat. After a game everyone returns to the lobby.
- Same Wi-Fi / LAN works out of the box (allow the game through Windows Firewall). Over the internet the host forwards
  TCP port 7777 on their router, or everyone uses a VPN such as Tailscale / ZeroTier / Radmin VPN and joins with the
  host's VPN address (`100.x.y.z:7777`).
- Code: `Core/Net` — `JoinCode` (code ⇄ address), `NetTransport` (TCP, length-prefixed messages), `NetProtocol`
  (binary messages and per-player state snapshots), `OnlineSessions` (`HostSession` runs the real engine and forwards
  each remote player's questions; `ClientSession` mirrors the table into a local `GameEngine` that the normal board draws).

## Build the .exe
Menu **Here To Slay > Build Windows EXE** → `Builds/HereToSlay/HereToSlay.exe` (the `Builds` folder is git-ignored;
copy the whole `Builds/HereToSlay` folder to share the game). Creating an empty file `Builds/build.request` while the
editor is open does the same without touching the editor.

## Code layout
| Folder | What it is |
|---|---|
| `Core/` | Pure C# rules engine (no Unity types): card database, game state, turn flow, challenges, modifiers, every Hero/Magic/Item/Monster effect, and the AI heuristics. |
| `Unity/` | Presentation: layered 2D board (table → cards in play → hand → focus), Slay-the-Spire style hand and energy, card views, runtime UI (title, game setup, settings, pause, hot-seat screens) and input. |
| `Core/Net/` | Online play: join codes, TCP transport, message protocol, host and client sessions. |
| `Editor/` | Creates the sorting layers and the optional game scene. |

The engine is a set of nested `IEnumerator` coroutines driven by `EngineRunner`. Whenever a player must decide something the
engine yields a `ChoiceRequest`; humans answer through the UI, AI seats through `AIBrain`. Because the engine has no Unity
dependency it can be simulated headlessly (thousands of AI-vs-AI games were run to test it).

## Sorting layers (back to front)
`Table` (board, felt, party zones) · `Board` (cards in play) · `Hand` (your fanned hand, energy orb) · `Focus` (hovered / dragged / played card). `Background` and `Scenery` are reserved and currently empty.

Card art lives in `Assets/Resources/here to slay card assets`, UI and table art in `Assets/Resources/Backgrounds`.
