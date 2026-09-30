using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace HereToSlay.View
{
    public struct WorldLabel
    {
        public string text;
        public Vector3 position;
        public int fontSize;
        public Color color;
    }

    /// <summary>
    /// Lays out every visible card on the layered table each frame.
    /// Cards keep one CardView per CardInstance so they glide smoothly between zones.
    /// </summary>
    public sealed class BoardView : MonoBehaviour
    {
        private const float CardH = 1.4f;

        private Camera cam;
        private readonly Dictionary<CardInstance, CardView> views = new Dictionary<CardInstance, CardView>();
        private readonly List<CardView> backs = new List<CardView>();
        private readonly HashSet<CardInstance> placedThisFrame = new HashSet<CardInstance>();
        private int backsUsed;

        private SpriteRenderer table;
        private SpriteRenderer opponentBanner;
        private SpriteRenderer monsterMat;
        private SpriteRenderer partyMat;
        private SpriteRenderer handMat;
        private SpriteRenderer stageMat;
        private CardView deckView;

        public readonly List<WorldLabel> labels = new List<WorldLabel>();
        public CardView Hovered { get; private set; }

        private float left;
        private float right;
        private Vector3 deckPosition;
        private Vector3 discardPosition;
        private Vector3 stagePosition;

        public void Build(Camera targetCamera)
        {
            cam = targetCamera;
            table = MakeRenderer("Table", Art.Load(Art.BackgroundFolder + "table"), Layers.Table, Layers.TableBase);
            opponentBanner = MakeSliced("Opponent Banner", new Color(0.05f, 0.04f, 0.12f, 0.55f), Layers.Table, Layers.TableBase + 1);
            monsterMat = MakeSliced("Monster Mat", new Color(0.35f, 0.05f, 0.08f, 0.35f), Layers.Table, Layers.TableBase + 2);
            partyMat = MakeSliced("Party Mat", new Color(0.02f, 0.18f, 0.16f, 0.45f), Layers.Table, Layers.TableBase + 2);
            handMat = MakeSliced("Hand Mat", new Color(0f, 0f, 0f, 0.25f), Layers.Table, Layers.TableBase + 2);
            stageMat = MakeSliced("Play Zone", new Color(1f, 0.9f, 0.5f, 0.12f), Layers.Table, Layers.TableBase + 2);

            deckView = CardView.Create(transform, "Deck");
            deckView.Show(null, false);
            deckView.SetSorting(Layers.Board, Layers.BoardBase + 5);
        }

        private SpriteRenderer MakeRenderer(string name, Sprite sprite, string layer, int order)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(transform, false);
            SpriteRenderer renderer = Art.AddRenderer(go);
            renderer.sprite = sprite;
            renderer.sortingLayerName = layer;
            renderer.sortingOrder = order;
            return renderer;
        }

        private SpriteRenderer MakeSliced(string name, Color color, string layer, int order)
        {
            SpriteRenderer renderer = MakeRenderer(name, Art.Panel, layer, order);
            renderer.drawMode = SpriteDrawMode.Sliced;
            renderer.color = color;
            return renderer;
        }

        private static void Rect(SpriteRenderer renderer, float x0, float y0, float x1, float y1)
        {
            renderer.transform.localPosition = new Vector3((x0 + x1) / 2f, (y0 + y1) / 2f, 0f);
            renderer.size = new Vector2(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0));
        }

        private void ComputeBounds(float panelPixels)
        {
            float halfH = cam.orthographicSize;
            float halfW = halfH * cam.aspect;
            float unitsPerPixel = 2f * halfH / Mathf.Max(1, Screen.height);
            left = cam.transform.position.x - halfW + 0.25f;
            right = cam.transform.position.x + halfW - panelPixels * unitsPerPixel - 0.25f;
        }

        private void LayoutTable()
        {
            // Table sprite covers the lower part of the screen; scenery stays visible above it.
            float top = 3.25f;
            float bottom = -cam.orthographicSize - 0.2f;
            Vector2 size = table.sprite.bounds.size;
            table.transform.localPosition = new Vector3((left + right) / 2f, (top + bottom) / 2f, 0f);
            table.transform.localScale = new Vector3((right - left + 0.4f) / size.x, (top - bottom) / size.y, 1f);

            Rect(opponentBanner, left - 0.1f, 3.3f, right + 0.1f, cam.orthographicSize - 0.05f);
            Rect(monsterMat, left + 2.65f, 0.55f, left + 6.95f, 2.75f);
            Rect(stageMat, right - 1.95f, 0.55f, right - 0.15f, 2.75f);
            Rect(partyMat, left + 0.05f, -1.95f, right - 0.05f, 0.1f);
            Rect(handMat, left + 0.4f, -5.3f, right - 0.4f, -2.85f);

            deckPosition = new Vector3(left + 0.75f, 1.65f, 0f);
            discardPosition = new Vector3(left + 1.95f, 1.65f, 0f);
            stagePosition = new Vector3(right - 1.05f, 1.65f, 0f);
        }

        /// <summary>Lay out the empty table (used behind the start menu).</summary>
        public void SyncEmpty(float panelPixels)
        {
            ComputeBounds(panelPixels);
            LayoutTable();
            deckView.Place(deckPosition, 0.8f, true);
            labels.Clear();
        }

        /// <summary>Rebuild every card's target slot from the current game state.</summary>
        public void Sync(GameEngine game, PlayerState viewer, bool handVisible, HashSet<CardInstance> selectable, float panelPixels)
        {
            ComputeBounds(panelPixels);
            LayoutTable();
            labels.Clear();
            placedThisFrame.Clear();
            backsUsed = 0;

            // ----- piles
            deckView.gameObject.SetActive(game.deck.Count > 0);
            deckView.Place(deckPosition, 0.8f, true);
            AddLabel($"Deck\n{game.deck.Count}", deckPosition + new Vector3(0, -0.8f, 0), 13, Color.white);

            if (game.discardPile.Count > 0)
            {
                CardInstance top = game.discardPile[game.discardPile.Count - 1];
                Place(top, discardPosition, 0.8f, Layers.Board, Layers.BoardBase + 5, true, selectable);
            }

            AddLabel($"Discard\n{game.discardPile.Count}", discardPosition + new Vector3(0, -0.8f, 0), 13, Color.white);

            // ----- monsters
            for (int i = 0; i < game.activeMonsters.Count; i++)
            {
                CardInstance monster = game.activeMonsters[i];
                Vector3 position = new Vector3(left + 3.45f + i * 1.35f, 1.75f, 0f);
                Place(monster, position, 0.75f, Layers.Board, Layers.BoardBase + 10 + i * 4, true, selectable);
                AddLabel($"{HereToSlayCardDatabase.DescribeRequirements(monster.def)}\n{monster.def.slayRoll}+ slay  {monster.def.failRoll}- fail",
                    position + new Vector3(0, -0.85f, 0), 11, new Color(1f, 0.85f, 0.8f));
            }

            AddLabel("MONSTERS", new Vector3(left + 4.8f, 2.95f, 0), 13, new Color(1f, 0.75f, 0.7f));
            AddLabel($"Monster deck: {game.monsterDeck.Count}", new Vector3(left + 4.8f, 0.45f, 0), 11, new Color(1f, 0.8f, 0.8f, 0.8f));

            // ----- card being played
            if (game.stagedCard != null)
            {
                Place(game.stagedCard, stagePosition, 1.05f, Layers.Focus, Layers.FocusBase + 10, true, selectable);
                AddLabel($"{game.stagedBy?.name} plays", stagePosition + new Vector3(0, 0.95f, 0), 13, new Color(1f, 0.95f, 0.6f));
            }
            else
            {
                AddLabel("Play zone", stagePosition, 12, new Color(1f, 1f, 1f, 0.35f));
            }

            if (viewer == null)
            {
                viewer = game.players.FirstOrDefault();
            }

            if (viewer != null)
            {
                LayoutOwnArea(game, viewer, handVisible, selectable);
                LayoutOpponents(game, viewer, selectable);
            }

            // ----- remove views for cards that are no longer visible
            List<CardInstance> stale = views.Keys.Where(c => !placedThisFrame.Contains(c)).ToList();
            foreach (CardInstance card in stale)
            {
                Destroy(views[card].gameObject);
                views.Remove(card);
            }

            for (int i = backsUsed; i < backs.Count; i++)
            {
                backs[i].gameObject.SetActive(false);
            }

            UpdateHover(viewer, handVisible);
        }

        private void LayoutOwnArea(GameEngine game, PlayerState me, bool handVisible, HashSet<CardInstance> selectable)
        {
            float rowY = -0.85f;
            string turnMark = game.Current == me ? "  - YOUR TURN -" : "";
            AddLabel($"{me.name}{turnMark}   |   Party classes: {me.DistinctClassCount()}/6   |   Monsters slain: {me.slainMonsters.Count}/3",
                new Vector3((left + right) / 2f, 0.3f, 0), 15, new Color(0.85f, 1f, 0.95f));

            if (me.leader != null)
            {
                Vector3 leaderPos = new Vector3(left + 0.75f, rowY, 0);
                Place(me.leader, leaderPos, 0.78f, Layers.Board, Layers.BoardBase + 40, true, selectable);
                AddLabel("Leader", leaderPos + new Vector3(0, -0.72f, 0), 11, Art.ClassColor(me.leader.def.heroClass));
            }

            float slainWidth = me.slainMonsters.Count * 0.7f;
            float heroStart = left + 2.05f;
            float heroEnd = right - 0.6f - slainWidth;
            int count = me.party.Count;
            float step = count <= 1 ? 1.05f : Mathf.Min(1.05f, (heroEnd - heroStart) / (count - 1));
            for (int i = 0; i < count; i++)
            {
                CardInstance hero = me.party[i];
                Vector3 position = new Vector3(heroStart + i * step, rowY + 0.12f, 0);
                int order = Layers.BoardBase + 60 + i * 4;
                Place(hero, position, 0.72f, Layers.Board, order, true, selectable);
                if (hero.equippedItem != null)
                {
                    Place(hero.equippedItem, position + new Vector3(0.22f, -0.42f, 0), 0.5f, Layers.Board, order - 3, true, selectable);
                }

                string used = me.heroesUsedThisTurn.Contains(hero.uid) ? " (used)" : "";
                AddLabel($"{hero.EffectiveClass} {hero.def.rollRequirement}+{used}", position + new Vector3(0, -0.68f, 0), 10, Art.ClassColor(hero.EffectiveClass));
            }

            for (int i = 0; i < me.slainMonsters.Count; i++)
            {
                Vector3 position = new Vector3(right - 0.45f - i * 0.7f, rowY, 0);
                Place(me.slainMonsters[i], position, 0.48f, Layers.Board, Layers.BoardBase + 30 + i * 2, true, selectable);
            }

            // ----- hand
            int n = me.hand.Count;
            float width = right - left - 1.4f;
            float spacing = n <= 1 ? 1.1f : Mathf.Min(1.1f, width / (n - 1));
            float center = (left + right) / 2f;
            for (int i = 0; i < n; i++)
            {
                CardInstance card = me.hand[i];
                float x = center + (i - (n - 1) / 2f) * spacing;
                float tilt = (i - (n - 1) / 2f);
                Vector3 position = new Vector3(x, -4.05f - Mathf.Abs(tilt) * 0.03f, 0);
                int order = Layers.HandBase + i * 4;
                bool lifted = Hovered != null && Hovered.card == card && handVisible;
                if (lifted)
                {
                    position += new Vector3(0, 0.4f, 0);
                    Place(card, position, 1.35f, Layers.Focus, Layers.FocusBase + 50, handVisible, selectable);
                }
                else
                {
                    Place(card, position, 0.98f, Layers.Hand, order, handVisible, selectable);
                }
            }

            AddLabel($"Hand ({n})", new Vector3(left + 0.9f, -2.65f, 0), 12, new Color(1, 1, 1, 0.7f));
        }

        private void LayoutOpponents(GameEngine game, PlayerState viewer, HashSet<CardInstance> selectable)
        {
            List<PlayerState> others = new List<PlayerState>();
            for (int k = 1; k < game.players.Count; k++)
            {
                others.Add(game.players[(viewer.index + k) % game.players.Count]);
            }

            if (others.Count == 0)
            {
                return;
            }

            float areaWidth = (right - left) / others.Count;
            float y = 4.15f;
            for (int k = 0; k < others.Count; k++)
            {
                PlayerState p = others[k];
                float x0 = left + k * areaWidth;
                float x1 = x0 + areaWidth;
                string turnMark = game.Current == p ? "  [TURN]" : "";
                Color nameColor = game.Current == p ? new Color(1f, 0.9f, 0.4f) : Color.white;
                AddLabel($"{p.name}{(p.isHuman ? "" : " (AI)")}{turnMark}   Classes {p.DistinctClassCount()}/6 · Slain {p.slainMonsters.Count}/3 · Hand {p.hand.Count}",
                    new Vector3((x0 + x1) / 2f, 5.15f, 0), 12, nameColor);

                if (p.leader != null)
                {
                    Place(p.leader, new Vector3(x0 + 0.45f, y, 0), 0.5f, Layers.Board, Layers.BoardBase + 200 + k * 60, true, selectable);
                }

                float slainWidth = p.slainMonsters.Count * 0.42f;
                float start = x0 + 1.1f;
                float end = x1 - 0.35f - slainWidth;
                int count = p.party.Count;
                float step = count <= 1 ? 0.62f : Mathf.Min(0.62f, (end - start) / (count - 1));
                for (int i = 0; i < count; i++)
                {
                    CardInstance hero = p.party[i];
                    Vector3 position = new Vector3(start + i * step, y + 0.08f, 0);
                    int order = Layers.BoardBase + 210 + k * 60 + i * 3;
                    Place(hero, position, 0.46f, Layers.Board, order, true, selectable);
                    if (hero.equippedItem != null)
                    {
                        Place(hero.equippedItem, position + new Vector3(0.14f, -0.3f, 0), 0.32f, Layers.Board, order - 2, true, selectable);
                    }
                }

                for (int i = 0; i < p.slainMonsters.Count; i++)
                {
                    Place(p.slainMonsters[i], new Vector3(x1 - 0.3f - i * 0.42f, y, 0), 0.32f, Layers.Board, Layers.BoardBase + 205 + k * 60 + i, true, selectable);
                }

                // Face-down hand, fanned just under the banner.
                int handCount = Mathf.Min(p.hand.Count, 10);
                for (int i = 0; i < handCount; i++)
                {
                    CardView back = NextBack();
                    Vector3 position = new Vector3(x0 + 0.35f + i * 0.13f, 3.45f, 0);
                    back.Place(position, 0.22f);
                    back.SetSorting(Layers.Board, Layers.BoardBase + 190 + k * 60 + i);
                }
            }
        }

        private CardView NextBack()
        {
            if (backsUsed >= backs.Count)
            {
                CardView view = CardView.Create(transform, "Hidden Card");
                view.Show(null, false);
                view.SpawnAt(deckPosition, 0.3f);
                backs.Add(view);
            }

            CardView back = backs[backsUsed++];
            back.gameObject.SetActive(true);
            back.highlighted = false;
            return back;
        }

        private void Place(CardInstance card, Vector3 position, float scale, string layer, int order, bool faceUp, HashSet<CardInstance> selectable)
        {
            if (card == null || placedThisFrame.Contains(card))
            {
                return;
            }

            placedThisFrame.Add(card);
            if (!views.TryGetValue(card, out CardView view))
            {
                view = CardView.Create(transform, card.Name);
                view.SpawnAt(deckPosition, 0.6f);
                views[card] = view;
            }

            view.Show(card, faceUp);
            view.Place(position, scale);
            view.SetSorting(layer, order);
            view.highlighted = selectable != null && selectable.Contains(card);
        }

        private void AddLabel(string text, Vector3 position, int size, Color color)
        {
            labels.Add(new WorldLabel { text = text, position = position, fontSize = size, color = color });
        }

        private void UpdateHover(PlayerState viewer, bool handVisible)
        {
            Hovered = null;
            Vector3 world = PointerWorld();
            if (float.IsNaN(world.x) || HereToSlayGame.PointerOverUI())
            {
                return;
            }

            Collider2D[] hits = Physics2D.OverlapPointAll(world);
            int bestOrder = int.MinValue;
            foreach (Collider2D hit in hits)
            {
                CardView view = hit.GetComponent<CardView>();
                SpriteRenderer renderer = hit.GetComponent<SpriteRenderer>();
                if (view == null || view.card == null || renderer == null || !view.faceUp)
                {
                    continue;
                }

                int order = SortingLayer.GetLayerValueFromID(renderer.sortingLayerID) * 100000 + renderer.sortingOrder;
                if (order > bestOrder)
                {
                    bestOrder = order;
                    Hovered = view;
                }
            }
        }

        public Vector3 PointerWorld()
        {
            if (UnityEngine.InputSystem.Mouse.current == null)
            {
                return new Vector3(float.NaN, 0, 0);
            }

            Vector2 pixel = UnityEngine.InputSystem.Mouse.current.position.ReadValue();
            Vector3 world = cam.ScreenToWorldPoint(new Vector3(pixel.x, pixel.y, -cam.transform.position.z));
            world.z = 0f;
            return world;
        }

        public Vector3 DeckPosition => deckPosition;
    }
}
