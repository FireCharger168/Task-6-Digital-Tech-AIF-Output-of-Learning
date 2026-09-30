using UnityEngine;

namespace HereToSlay.View
{
    /// <summary>A card on the table: face sprite, drop shadow and a highlight glow, gliding towards its target slot.</summary>
    public sealed class CardView : MonoBehaviour
    {
        public CardInstance card;
        public bool faceUp;
        public Vector3 targetPosition;
        public float targetScale = 1f;
        public bool highlighted;
        public Color highlightColor = new Color(1f, 0.85f, 0.25f);

        private SpriteRenderer face;
        private SpriteRenderer shadow;
        private SpriteRenderer glow;
        private BoxCollider2D hitBox;
        private Sprite currentSprite;
        private bool placedOnce;

        public static CardView Create(Transform parent, string name)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            CardView view = go.AddComponent<CardView>();
            view.Build();
            return view;
        }

        private void Build()
        {
            GameObject glowGo = new GameObject("Glow");
            glowGo.transform.SetParent(transform, false);
            glow = Art.AddRenderer(glowGo);
            glow.sprite = Art.Glow;
            glow.drawMode = SpriteDrawMode.Sliced;
            glow.enabled = false;

            GameObject shadowGo = new GameObject("Shadow");
            shadowGo.transform.SetParent(transform, false);
            shadowGo.transform.localPosition = new Vector3(0.07f, -0.08f, 0f);
            shadow = Art.AddRenderer(shadowGo);
            shadow.sprite = Art.Panel;
            shadow.drawMode = SpriteDrawMode.Sliced;
            shadow.color = new Color(0f, 0f, 0f, 0.45f);

            face = Art.AddRenderer(gameObject);
            hitBox = gameObject.AddComponent<BoxCollider2D>();
        }

        public void Show(CardInstance instance, bool showFace)
        {
            card = instance;
            faceUp = showFace;
            Sprite sprite = showFace && instance != null ? Art.CardFace(instance.def) : Art.CardBack;
            if (sprite != currentSprite)
            {
                currentSprite = sprite;
                face.sprite = sprite;
                Vector2 size = sprite.bounds.size;
                hitBox.size = size;
                shadow.size = size;
                glow.size = size + new Vector2(0.55f, 0.55f);
            }
        }

        public void SetSorting(string layer, int order)
        {
            face.sortingLayerName = layer;
            face.sortingOrder = order;
            shadow.sortingLayerName = layer;
            shadow.sortingOrder = order - 1;
            glow.sortingLayerName = layer;
            glow.sortingOrder = order - 2;
        }

        public void Place(Vector3 position, float scale, bool snap = false)
        {
            targetPosition = position;
            targetScale = scale;
            if (snap || !placedOnce)
            {
                placedOnce = true;
                if (snap)
                {
                    transform.localPosition = position;
                    transform.localScale = Vector3.one * scale;
                }
            }
        }

        public void SpawnAt(Vector3 position, float scale)
        {
            transform.localPosition = position;
            transform.localScale = Vector3.one * scale;
            placedOnce = true;
        }

        private void Update()
        {
            float t = 1f - Mathf.Exp(-12f * Time.deltaTime);
            transform.localPosition = Vector3.Lerp(transform.localPosition, targetPosition, t);
            float s = Mathf.Lerp(transform.localScale.x, targetScale, t);
            transform.localScale = new Vector3(s, s, 1f);

            glow.enabled = highlighted;
            if (highlighted)
            {
                float pulse = 0.55f + 0.35f * Mathf.Sin(Time.time * 5f);
                glow.color = new Color(highlightColor.r, highlightColor.g, highlightColor.b, pulse);
            }
        }
    }
}
