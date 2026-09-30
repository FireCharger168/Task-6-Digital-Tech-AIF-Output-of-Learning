# Here to Slay - Unity 2D (Task 6 Digital Tech AIF Output of Learning)

A digital, playable version of the **Here to Slay** card game written in **C#** for **Unity 6 (6000.3, URP 2D)**.

- Unity project: `Task-6-Digital-Tech-AIF-Output-of-Learning/`
- Open it in Unity Hub, open `Assets/Scenes/SampleScene` and press **Play** (the game boots itself).
- 2-4 seats, each Human, AI or Empty (several humans = hot-seat on one screen).
- Full turn loop: action points, draw, play Heroes / Items / Cursed Items / Magic, roll 2d6 for Hero effects, attack Monsters,
  Challenge cards, Modifier cards on any roll, hand limit, and both win conditions (3 Monsters or all 6 classes).
- Every card's effect is implemented (48 Heroes, 6 Party Leaders, 15 Monsters, 12 Items, 8 Magic, 5 Modifiers, Challenge).
- Layered 2D scene built with sorting layers: Background (sky) -> Scenery (parallax mountains, forest, clouds) -> Table -> Board -> Hand -> Focus.

See `Task-6-Digital-Tech-AIF-Output-of-Learning/Assets/Scripts/HereToSlay/README.md` for the code layout.
