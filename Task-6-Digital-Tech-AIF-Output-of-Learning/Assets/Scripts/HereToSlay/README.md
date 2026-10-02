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

## Build the .exe
Menu **Here To Slay > Build Windows EXE** → `Builds/HereToSlay/HereToSlay.exe` (the `Builds` folder is git-ignored;
copy the whole `Builds/HereToSlay` folder to share the game). Creating an empty file `Builds/build.request` while the
editor is open does the same without touching the editor.

## Code layout
| Folder | What it is |
|---|---|
| `Core/` | Pure C# rules engine (no Unity types): card database, game state, turn flow, challenges, modifiers, every Hero/Magic/Item/Monster effect, and the AI heuristics. |
| `Unity/` | Presentation: layered 2D board (table → cards in play → hand → focus), Slay-the-Spire style hand and energy, card views, runtime UI (title, game setup, settings, pause, hot-seat screens) and input. |
| `Editor/` | Creates the sorting layers and the optional game scene. |

The engine is a set of nested `IEnumerator` coroutines driven by `EngineRunner`. Whenever a player must decide something the
engine yields a `ChoiceRequest`; humans answer through the UI, AI seats through `AIBrain`. Because the engine has no Unity
dependency it can be simulated headlessly (thousands of AI-vs-AI games were run to test it).

## Sorting layers (back to front)
`Table` (board, felt, party zones) · `Board` (cards in play) · `Hand` (your fanned hand, energy orb) · `Focus` (hovered / dragged / played card). `Background` and `Scenery` are reserved and currently empty.

Card art lives in `Assets/Resources/here to slay card assets`, UI and table art in `Assets/Resources/Backgrounds`.
