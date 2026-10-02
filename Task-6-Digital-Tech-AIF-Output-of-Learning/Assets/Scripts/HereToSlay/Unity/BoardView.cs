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
        public bool badge;
    }

    public enum SeatSide
    {
        Top,
        Left,
        Right
    }

    /// <summary>An opponent's seat around the table (UNO style): name plate plus their face-down hand.</summary>
    public struct SeatInfo
    {
        public PlayerState player;
        public Vector3 plateWorld;
        public SeatSide side;
        public bool isCurrent;
    }

    /// <summary>Everything the board needs to draw one frame.</summary>
    public sealed class BoardFrame
    {
        public GameEngine game;
        public PlayerState viewer;
        public bool handFaceUp;
        public readonly HashSet<CardInstance> selectable = new HashSet<CardInstance>();
        public bool deckSelectable;
        public bool hoverZoom = true;
        public CardInstance dragging;
        public Vector3 dragWorld;
    }

    /// <summary>
    /// Lays out the layered table every frame:
    /// centre board = monsters, deck, discard, play zone and every opponent's Party;
    /// opponents sit around the edge with their face-down hands (UNO style);
    /// your Party sits in front of you and your hand is fanned Slay-the-Spire style.
    /// </summary>
    public sealed class BoardView : MonoBehaviour
    {
        // ----- layout constants (world units, 19.2 x 10.8 visible at 16:9)
        public const float BoardLeft = -9.45f;
        public const float BoardRight = 9.45f;
        public const float BoardTop = 4.0f;
        public const float BoardBottom = -1.55f;
        /// <summary>Drag a hand card above this line and release to play it.</summary>
        public const float PlayLineY = -1.9f;

        private const float HandY = -4.75f;
        private const float HandScale = 2.1f;
        private const float HandHoverScale = 2.7f;
        private const float PartyY = -2.3f;
        private const float PartyScale = 0.98f;
        private const float OpponentCardScale = 1.0f;
        private const float MonsterScale = 1.4f;
        private const float MiddleY = 1.05f;

        private Camera cam;
        private readonly Dictionary<CardInstance, CardView> views = new Dictionary<CardInstance, CardView>();
        private readonly Dictionary<CardInstance, bool> faceUpThisFrame = new Dictionary<CardInstance, bool>();
        private readonly List<CardView> backs = new List<CardView>();
        private readonly List<SpriteRenderer> zonePanels = new List<SpriteRenderer>();
        private int backsUsed;
        private int zonesUsed;

        private SpriteRenderer table;
        private SpriteRenderer myZone;
        private SpriteRenderer playZone;
        private SpriteRenderer orb;
        private CardView deckView;
        private CardView inspector;

        public readonly List<WorldLabel> labels = new List<WorldLabel>();
        public readonly List<SeatInfo> seats = new List<SeatInfo>();

        public CardView Hovered { get; private set; }
        public bool HoveredInHand { get; private set; }
        public bool HoveringDeck { get; private set; }
        public bool HoveringDiscard { get; private set; }
        public Vector3 OrbPosition { get; private set; }
        public Vector3 DeckPosition { get; private set; }
        public Vector3 DiscardPosition { get; private set; }
        public Vector3 PlayZonePosition { get; private set; }
        /// <summary>World rect of the big hovered card (inspector or lifted hand card) for tooltips.</summary>
        public Rect ZoomRect { get; private set; }
        public CardInstance ZoomCard { get; private set; }

        private readonly List<CardInstance> handOrder = new List<CardInstance>();

        public void Build(Camera targetCamera)
        {
            cam = targetCamera;
            table = MakeRenderer("Board (table)", Art.Load(Art.BackgroundFolder + "table", 300f, new Vector4(150, 150, 150, 150)), Layers.Table, Layers.TableBase);
            table.drawMode = SpriteDrawMode.Sliced;
            myZone = MakeSliced("My Party Mat", new Color(0f, 0f, 0f, 0.35f), Layers.Table, Layers.TableBase + 1);
            playZone = MakeSliced("Play Zone", new Color(1f, 0.9f, 0.5f, 0.12f), Layers.Table, Layers.TableBase + 3);

            orb = MakeRenderer("Energy Orb", Art.EnergyOrb, Layers.Hand, Layers.HandBase + 900);

            deckView = CardView.Create(transform, "Deck");
            deckView.Show(null, false);
            deckView.SetSorting(Layers.Board, Layers.BoardBase + 5);

            inspector = CardView.Create(transform, "Inspector");
            inspector.SetSorting(Layers.Focus, Layers.FocusBase + 500);
            inspector.followSpeed = 30f;
            inspector.RemoveHitBox();
            inspector.gameObject.SetActive(false);
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

        private static void SetRect(SpriteRenderer renderer, Rect rect)
        {
            renderer.transform.localPosition = new Vector3(rect.center.x, rect.center.y, 0f);
            renderer.size = rect.size;
        }

        public void SetVisible(bool visible)
        {
            gameObject.SetActive(visible);
        }

        private void LayoutTable()
        {
            const float pad = 0.25f;
            SetRect(table, Rect.MinMaxRect(BoardLeft - pad, BoardBottom - pad, BoardRight + pad, BoardTop + pad));
            SetRect(myZone, Rect.MinMaxRect(-5.9f, PartyY - 0.78f, 9.4f, PartyY + 0.78f));

            DeckPosition = new Vector3(-5.35f, MiddleY, 0f);
            DiscardPosition = new Vector3(-3.85f, MiddleY, 0f);
            PlayZonePosition = new Vector3(4.3f, MiddleY, 0f);
            SetRect(playZone, new Rect(PlayZonePosition.x - 1.0f, PlayZonePosition.y - 1.35f, 2.0f, 2.7f));

            OrbPosition = new Vector3(-8.35f, -4.25f, 0f);
            orb.transform.localPosition = OrbPosition;
            orb.transform.localScale = Vector3.one * 0.8f;
        }

        /// <summary>Lay out the empty table (behind menus).</summary>
        public void SyncEmpty()
        {
            LayoutTable();
            labels.Clear();
            seats.Clear();
            deckView.Place(DeckPosition, 0.95f);
            deckView.highlighted = false;
            orb.enabled = false;
            inspector.gameObject.SetActive(false);
            Hovered = null;
            foreach (CardView view in views.Values)
            {
                Destroy(view.gameObject);
            }

            views.Clear();
            foreach (CardView back in backs)
            {
                back.gameObject.SetActive(false);
            }

            foreach (SpriteRenderer zone in zonePanels)
            {
                zone.gameObject.SetActive(false);
            }
        }

        public bool IsVisibleFaceUp(CardInstance card)
        {
            return card != null && faceUpThisFrame.TryGetValue(card, out bool up) && up;
        }

        public bool IsInHand(CardInstance card)
        {
            return handOrder.Contains(card);
        }

        public void Sync(BoardFrame frame)
        {
            GameEngine game = frame.game;
            LayoutTable();
            labels.Clear();
            seats.Clear();
            faceUpThisFrame.Clear();
            backsUsed = 0;
            zonesUsed = 0;
            handOrder.Clear();

            PlayerState viewer = frame.viewer ?? game.players.FirstOrDefault();

            // ----- deck, discard, monsters, play zone
            deckView.gameObject.SetActive(game.deck.Count > 0);
            deckView.Place(DeckPosition + (HoveringDeck && frame.deckSelectable ? new Vector3(0, 0.12f, 0) : Vector3.zero), 1.25f);
            deckView.highlighted = frame.deckSelectable;
            AddBadge(game.deck.Count.ToString(), DeckPosition + new Vector3(0.5f, 0.75f, 0));

            if (game.discardPile.Count > 0)
            {
                CardInstance top = game.discardPile[game.discardPile.Count - 1];
                Place(top, DiscardPosition, 1.25f, 0f, Layers.Board, Layers.BoardBase + 5, true, frame);
                AddBadge(game.discardPile.Count.ToString(), DiscardPosition + new Vector3(0.5f, 0.75f, 0));
            }

            for (int i = 0; i < game.activeMonsters.Count; i++)
            {
                Vector3 position = new Vector3(-1.75f + i * 1.65f, MiddleY, 0f);
                Place(game.activeMonsters[i], position, MonsterScale, 0f, Layers.Board, Layers.BoardBase + 10 + i * 4, true, frame);
            }

            if (game.stagedCard != null)
            {
                Place(game.stagedCard, PlayZonePosition, 1.75f, 0f, Layers.Focus, Layers.FocusBase + 10, true, frame);
            }

            if (viewer != null)
            {
                LayoutOpponents(game, viewer, frame);
                LayoutOwnParty(viewer, frame);
                LayoutHand(viewer, frame);
                UpdateOrb(game, viewer);
            }

            // ----- clean up cards that left view
            List<CardInstance> stale = views.Keys.Where(c => !faceUpThisFrame.ContainsKey(c)).ToList();
            foreach (CardInstance card in stale)
            {
                Destroy(views[card].gameObject);
                views.Remove(card);
            }

            for (int i = backsUsed; i < backs.Count; i++)
            {
                backs[i].gameObject.SetActive(false);
            }

            for (int i = zonesUsed; i < zonePanels.Count; i++)
            {
                zonePanels[i].gameObject.SetActive(false);
            }

            UpdateHover(frame);
            UpdateInspector(frame);
        }

        private void UpdateOrb(GameEngine game, PlayerState viewer)
        {
            orb.enabled = true;
            bool myTurn = game.Current == viewer;
            orb.sprite = myTurn && viewer.actionPoints > 0 ? Art.EnergyOrb : Art.EnergyOrbEmpty;
        }

        // ----------------------------------------------------------------- opponents around the table

        private static List<SeatSide> SideLayout(int count, out int topCount)
        {
            List<SeatSide> sides = new List<SeatSide>();
            switch (count)
            {
                case 1:
                    sides.Add(SeatSide.Top);
                    break;
                case 2:
                    sides.Add(SeatSide.Left);
                    sides.Add(SeatSide.Right);
                    break;
                default:
                    sides.Add(SeatSide.Left);
                    for (int i = 0; i < count - 2; i++)
                    {
                        sides.Add(SeatSide.Top);
                    }

                    sides.Add(SeatSide.Right);
                    break;
            }

            topCount = sides.Count(s => s == SeatSide.Top);
            return sides;
        }

        private void LayoutOpponents(GameEngine game, PlayerState viewer, BoardFrame frame)
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

            List<SeatSide> sides = SideLayout(others.Count, out int topCount);
            bool hasSides = sides.Contains(SeatSide.Left);
            float topLeft = hasSides ? -6.25f : -9.3f;
            float topRight = hasSides ? 6.25f : 9.3f;
            float topWidth = (topRight - topLeft) / Mathf.Max(1, topCount);
            int topIndex = 0;

            for (int k = 0; k < others.Count; k++)
            {
                PlayerState p = others[k];
                SeatSide side = sides[k];
                Rect zone;
                Vector3 plate;
                bool vertical;
                switch (side)
                {
                    case SeatSide.Left:
                        zone = Rect.MinMaxRect(-9.35f, -1.45f, -6.4f, 3.9f);
                        plate = new Vector3(-7.85f, 4.5f, 0);
                        vertical = true;
                        break;
                    case SeatSide.Right:
                        zone = Rect.MinMaxRect(6.4f, -1.45f, 9.35f, 3.9f);
                        plate = new Vector3(7.85f, 4.5f, 0);
                        vertical = true;
                        break;
                    default:
                        float x0 = topLeft + topIndex * topWidth;
                        zone = Rect.MinMaxRect(x0 + 0.08f, 2.2f, x0 + topWidth - 0.08f, 3.9f);
                        plate = new Vector3(x0 + topWidth / 2f, 4.62f, 0);
                        vertical = false;
                        topIndex++;
                        break;
                }

                bool current = game.Current == p;
                seats.Add(new SeatInfo { player = p, plateWorld = plate, side = side, isCurrent = current });

                SpriteRenderer panel = NextZone();
                Color tint = p.leader != null ? Art.ClassColor(p.leader.def.heroClass) : Color.gray;
                panel.color = current ? new Color(1f, 0.9f, 0.45f, 0.32f) : new Color(tint.r * 0.35f, tint.g * 0.35f, tint.b * 0.35f, 0.45f);
                SetRect(panel, zone);

                LayoutParty(p, zone, vertical, frame, Layers.BoardBase + 200 + k * 80);
            }
        }

        private void LayoutParty(PlayerState p, Rect zone, bool vertical, BoardFrame frame, int orderBase)
        {
            List<CardInstance> cards = new List<CardInstance>();
            if (p.leader != null)
            {
                cards.Add(p.leader);
            }

            cards.AddRange(p.party);
            int count = cards.Count;
            float scale = OpponentCardScale;
            float w = scale;
            float h = scale * 1.4f;

            for (int i = 0; i < count; i++)
            {
                Vector3 position;
                if (vertical)
                {
                    int rows = Mathf.CeilToInt(count / 2f);
                    float stepY = rows <= 1 ? 0f : Mathf.Min(h + 0.12f, (zone.height - h - 0.75f) / (rows - 1));
                    int row = i / 2;
                    int col = i % 2;
                    position = new Vector3(zone.xMin + 0.82f + col * 1.3f, zone.yMax - h / 2f - 0.08f - row * stepY, 0f);
                }
                else
                {
                    float step = count <= 1 ? 0f : Mathf.Min(w + 0.12f, (zone.width - w - 0.8f) / (count - 1));
                    float total = step * (count - 1);
                    position = new Vector3(zone.center.x - 0.35f - total / 2f + i * step, zone.center.y + 0.02f, 0f);
                }

                int order = orderBase + i * 3;
                Place(cards[i], position, scale, 0f, Layers.Board, order, true, frame);
                if (cards[i].equippedItem != null)
                {
                    Place(cards[i].equippedItem, position + new Vector3(0.24f, -0.42f, 0), scale * 0.7f, 0f, Layers.Board, order - 2, true, frame);
                }
            }

            // Slain monsters: small trophies in the zone's corner.
            for (int i = 0; i < p.slainMonsters.Count; i++)
            {
                Vector3 position = vertical
                    ? new Vector3(zone.xMin + 0.4f + i * 0.5f, zone.yMin + 0.55f, 0f)
                    : new Vector3(zone.xMax - 0.4f, zone.yMax - 0.45f - i * 0.5f, 0f);
                Place(p.slainMonsters[i], position, 0.55f, 0f, Layers.Board, orderBase + 60 + i, true, frame);
            }
        }

        private SpriteRenderer NextZone()
        {
            if (zonesUsed >= zonePanels.Count)
            {
                zonePanels.Add(MakeSliced("Party Zone", Color.white, Layers.Table, Layers.TableBase + 2));
            }

            SpriteRenderer zone = zonePanels[zonesUsed++];
            zone.gameObject.SetActive(true);
            return zone;
        }

        private CardView NextBack()
        {
            if (backsUsed >= backs.Count)
            {
                CardView view = CardView.Create(transform, "Hidden Card");
                view.Show(null, false);
                view.SpawnAt(DeckPosition, 0.5f);
                view.RemoveHitBox();
                backs.Add(view);
            }

            CardView back = backs[backsUsed++];
            back.gameObject.SetActive(true);
            back.highlighted = false;
            return back;
        }

        // ----------------------------------------------------------------- your side

        private void LayoutOwnParty(PlayerState me, BoardFrame frame)
        {
            const float left = -5.15f;
            if (me.leader != null)
            {
                Place(me.leader, new Vector3(left, PartyY, 0), PartyScale, 0f, Layers.Board, Layers.BoardBase + 600, true, frame);
            }

            float slainSpace = me.slainMonsters.Count * 0.75f + (me.slainMonsters.Count > 0 ? 0.3f : 0f);
            float start = left + 1.3f;
            float end = 8.75f - slainSpace;
            int count = me.party.Count;
            float step = count <= 1 ? 1.15f : Mathf.Min(1.15f, (end - start) / (count - 1));
            for (int i = 0; i < count; i++)
            {
                CardInstance hero = me.party[i];
                Vector3 position = new Vector3(start + i * step, PartyY + 0.08f, 0);
                int order = Layers.BoardBase + 610 + i * 3;
                Place(hero, position, PartyScale, 0f, Layers.Board, order, true, frame);
                if (hero.equippedItem != null)
                {
                    Place(hero.equippedItem, position + new Vector3(0.26f, -0.42f, 0), PartyScale * 0.7f, 0f, Layers.Board, order - 2, true, frame);
                }
            }

            for (int i = 0; i < me.slainMonsters.Count; i++)
            {
                Place(me.slainMonsters[i], new Vector3(8.95f - i * 0.75f, PartyY, 0), 0.75f, 0f, Layers.Board, Layers.BoardBase + 680 + i, true, frame);
            }
        }

        private static float HandSpacing(int n)
        {
            return n <= 1 ? 0f : Mathf.Min(1.6f, 11f / (n - 1));
        }

        private void LayoutHand(PlayerState me, BoardFrame frame)
        {
            List<CardInstance> hand = me.hand;
            int n = hand.Count;
            handOrder.AddRange(hand);
            float spacing = HandSpacing(n);
            float angleStep = n <= 1 ? 0f : Mathf.Min(4f, 24f / (n - 1));
            int hoverIndex = HoveredInHand && Hovered != null ? hand.IndexOf(Hovered.card) : -1;

            for (int i = 0; i < n; i++)
            {
                CardInstance card = hand[i];
                bool faceUp = frame.handFaceUp;
                bool showCost = faceUp && (card.def.type == CardType.Hero || card.def.IsItem || card.def.type == CardType.Magic);
                float t = i - (n - 1) / 2f;
                Vector3 position = new Vector3(t * spacing, HandY - t * t * 0.03f, 0f);
                float rotation = -t * angleStep;

                if (frame.dragging == card)
                {
                    Place(card, frame.dragWorld, 2.3f, 0f, Layers.Focus, Layers.FocusBase + 300, faceUp, frame, showCost);
                    continue;
                }

                if (hoverIndex >= 0 && faceUp && frame.dragging == null)
                {
                    if (i == hoverIndex)
                    {
                        float height = HandHoverScale * 1.4f;
                        Vector3 lifted = new Vector3(position.x, -cam.orthographicSize + height / 2f + 0.05f, 0f);
                        Place(card, lifted, HandHoverScale, 0f, Layers.Focus, Layers.FocusBase + 200, true, frame, showCost);
                        continue;
                    }

                    position.x += i < hoverIndex ? -0.7f : 0.7f;
                }

                Place(card, position, HandScale, rotation, Layers.Hand, Layers.HandBase + i * 5, faceUp, frame, showCost);
            }
        }

        private void Place(CardInstance card, Vector3 position, float scale, float rotation, string layer, int order, bool faceUp, BoardFrame frame, bool showCost = false)
        {
            if (card == null || faceUpThisFrame.ContainsKey(card))
            {
                return;
            }

            faceUpThisFrame[card] = faceUp;
            if (!views.TryGetValue(card, out CardView view))
            {
                view = CardView.Create(transform, card.Name);
                view.SpawnAt(DeckPosition, 0.9f);
                views[card] = view;
            }

            view.Show(card, faceUp, showCost);
            view.Place(position, scale, rotation);
            view.SetSorting(layer, order);
            view.highlighted = faceUp && frame.selectable.Contains(card);
        }

        private void AddBadge(string text, Vector3 position)
        {
            labels.Add(new WorldLabel { text = text, position = position, fontSize = 18, color = Color.white, badge = true });
        }

        // ----------------------------------------------------------------- pointer

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

        private void UpdateHover(BoardFrame frame)
        {
            Hovered = null;
            HoveredInHand = false;
            HoveringDeck = false;
            HoveringDiscard = false;
            ZoomCard = null;

            Vector3 world = PointerWorld();
            if (float.IsNaN(world.x) || HereToSlayGame.PointerOverUI() || frame.dragging != null)
            {
                return;
            }

            // Hand: pick the fanned card nearest the pointer (stable while cards lift and spread apart).
            float handTop = HandY + HandScale * 0.7f + 0.15f;
            int n = handOrder.Count;
            if (world.y < handTop && n > 0 && frame.handFaceUp)
            {
                float spacing = HandSpacing(n);
                float halfWidth = spacing * (n - 1) / 2f + HandScale / 2f;
                if (Mathf.Abs(world.x) <= halfWidth + 0.3f)
                {
                    int index = n <= 1 ? 0 : Mathf.Clamp(Mathf.RoundToInt(world.x / spacing + (n - 1) / 2f), 0, n - 1);
                    if (views.TryGetValue(handOrder[index], out CardView handView))
                    {
                        Hovered = handView;
                        HoveredInHand = true;
                        float height = HandHoverScale * 1.4f;
                        float x = (index - (n - 1) / 2f) * spacing;
                        ZoomRect = new Rect(x - HandHoverScale / 2f, -cam.orthographicSize + 0.05f, HandHoverScale, height);
                        ZoomCard = handView.card;
                        return;
                    }
                }
            }

            Collider2D[] hits = Physics2D.OverlapPointAll(world);
            int bestOrder = int.MinValue;
            foreach (Collider2D hit in hits)
            {
                CardView view = hit.GetComponent<CardView>();
                if (view == null || view == inspector)
                {
                    continue;
                }

                if (view == deckView)
                {
                    HoveringDeck = true;
                    continue;
                }

                if (view.card == null || !view.faceUp || handOrder.Contains(view.card))
                {
                    continue;
                }

                int order = SortingLayer.GetLayerValueFromID(view.Face.sortingLayerID) * 100000 + view.Face.sortingOrder;
                if (order > bestOrder)
                {
                    bestOrder = order;
                    Hovered = view;
                }
            }

            if (Hovered != null && frame.game.discardPile.Count > 0 && Hovered.card == frame.game.discardPile[frame.game.discardPile.Count - 1])
            {
                HoveringDiscard = true;
            }
        }

        private void UpdateInspector(BoardFrame frame)
        {
            if (!frame.hoverZoom || Hovered == null || HoveredInHand || Hovered.card == null)
            {
                inspector.gameObject.SetActive(false);
                return;
            }

            CardInstance card = Hovered.card;
            Vector2 size = Art.CardFace(card.def).bounds.size;
            float scale = 4.4f / size.y;
            float width = size.x * scale;
            float height = size.y * scale;
            Vector3 anchor = Hovered.transform.localPosition;
            float halfW = cam.orthographicSize * cam.aspect;
            float reach = Hovered.transform.localScale.x * 0.6f + width / 2f + 0.1f;
            float x = anchor.x < 0 ? anchor.x + reach : anchor.x - reach;
            x = Mathf.Clamp(x, -halfW + width / 2f + 0.1f, halfW - width / 2f - 0.1f);
            float y = Mathf.Clamp(anchor.y, -1.0f + height / 2f, cam.orthographicSize - height / 2f - 0.1f);

            bool wasHidden = !inspector.gameObject.activeSelf || inspector.card != card;
            inspector.gameObject.SetActive(true);
            inspector.Show(card, true);
            inspector.Place(new Vector3(x, y, 0f), scale);
            if (wasHidden)
            {
                inspector.Snap();
            }

            ZoomRect = new Rect(x - width / 2f, y - height / 2f, width, height);
            ZoomCard = card;
        }
    }
}
