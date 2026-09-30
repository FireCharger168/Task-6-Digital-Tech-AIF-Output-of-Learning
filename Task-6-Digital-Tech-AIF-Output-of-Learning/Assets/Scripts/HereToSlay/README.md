# Here to Slay (Unity 2D)

A digital version of the Here to Slay card game built in C# for Unity 6 (URP 2D).

## Play
Open the project in Unity and press **Play** in any scene (e.g. `Assets/Scenes/SampleScene`).
The game boots itself (`HereToSlayGame` has a `RuntimeInitializeOnLoadMethod`).
Optionally use the menu **Here To Slay > Create Game Scene** to make a dedicated scene.

On the start menu set each of the 4 seats to **Human**, **AI** or **Empty** (2-4 players; several humans = hot-seat).

## Code layout
| Folder | What it is |
|---|---|
| `Core/` | Pure C# rules engine (no Unity types): card database, game state, turn flow, challenges, modifiers, every Hero/Magic/Item/Monster effect, and the AI heuristics. |
| `Unity/` | Presentation: layered 2D scene (sky → parallax mountains → table → cards → hand → focus), card views, runtime UI and input. |
| `Editor/` | Creates the sorting layers and the optional game scene. |

The engine is a set of nested `IEnumerator` coroutines driven by `EngineRunner`. Whenever a player must decide something the
engine yields a `ChoiceRequest`; humans answer through the UI, AI seats through `AIBrain`. Because the engine has no Unity
dependency it can be simulated headlessly (thousands of AI-vs-AI games were run to test it).

## Sorting layers (back to front)
`Background` (sky) · `Scenery` (parallax ridges, clouds) · `Table` (table, felt, zone mats) · `Board` (cards in play) · `Hand` · `Focus` (hovered / played card)

Card art lives in `Assets/Resources/here to slay card assets`, scenery art in `Assets/Resources/Backgrounds`.
