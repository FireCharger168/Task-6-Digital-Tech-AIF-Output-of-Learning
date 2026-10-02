using UnityEngine;

namespace HereToSlay.View
{
    /// <summary>
    /// A card on the table: face sprite, drop shadow, highlight glow and an optional Slay-the-Spire style cost orb.
    /// It glides (position, scale and rotation) towards the slot the BoardView gives it.
    /// </summary>
    public sealed class CardView : MonoBehaviour
    {
        public CardInstance card;
        public bool faceUp;
        public Vector3 targetPosition;
        public float targetScale = 1f;
        public float targetRotation;
        public bool highlighted;
        public Color highlightColor = new Color(0.45f, 1f, 0.55f);
        public float followSpeed = 14f;

        private SpriteRenderer face;
        private SpriteRenderer shadow;
        private SpriteRenderer glow;
        private SpriteRenderer costBadge;
        private BoxCollider2D hitBox;
        private Sprite currentSprite;

        public SpriteRenderer Face => face;

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
            shadowGo.transform.localPosition = new Vector3(0.06f, -0.07f, 0f);
            shadow = Art.AddRenderer(shadowGo);
            shadow.sprite = Art.Panel;
            shadow.drawMode = SpriteDrawMode.Sliced;
            shadow.color = new Color(0f, 0f, 0f, 0.5f);

            face = Art.AddRenderer(gameObject);

            GameObject costGo = new GameObject("Cost");
            costGo.transform.SetParent(transform, false);
            costBadge = Art.AddRenderer(costGo);
            costBadge.sprite = Art.CostOrb;
            costBadge.enabled = false;

            hitBox = gameObject.AddComponent<BoxCollider2D>();
        }

        public void Show(CardInstance instance, bool showFace, bool showCost = false)
        {
            card = instance;
            faceUp = showFace;
            Sprite sprite = showFace && instance != null ? Art.CardFace(instance.def) : Art.CardBack;
            if (sprite != currentSprite)
            {
                currentSprite = sprite;
                face.sprite = sprite;
                Vector2 size = sprite.bounds.size;
                if (hitBox != null)
                {
                    hitBox.size = size;
                }

                shadow.size = size;
                glow.size = size + new Vector2(0.62f, 0.62f);
                costBadge.transform.localPosition = new Vector3(-size.x / 2f + 0.08f, size.y / 2f - 0.08f, 0f);
                costBadge.transform.localScale = Vector3.one * 0.38f;
            }

            costBadge.enabled = showCost && showFace;
        }

        /// <summary>Decorative cards (face-down hands, the zoomed inspector) must not catch the pointer.</summary>
        public void RemoveHitBox()
        {
            if (hitBox != null)
            {
                Destroy(hitBox);
                hitBox = null;
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
            costBadge.sortingLayerName = layer;
            costBadge.sortingOrder = order + 1;
        }

        public void Place(Vector3 position, float scale, float rotation = 0f)
        {
            targetPosition = position;
            targetScale = scale;
            targetRotation = rotation;
        }

        public void Snap()
        {
            transform.localPosition = targetPosition;
            transform.localScale = new Vector3(targetScale, targetScale, 1f);
            transform.localRotation = Quaternion.Euler(0, 0, targetRotation);
        }

        public void SpawnAt(Vector3 position, float scale)
        {
            transform.localPosition = position;
            transform.localScale = Vector3.one * scale;
        }

        private void Update()
        {
            float t = 1f - Mathf.Exp(-followSpeed * Time.deltaTime);
            transform.localPosition = Vector3.Lerp(transform.localPosition, targetPosition, t);
            float s = Mathf.Lerp(transform.localScale.x, targetScale, t);
            transform.localScale = new Vector3(s, s, 1f);
            float angle = Mathf.LerpAngle(transform.localEulerAngles.z, targetRotation, t);
            transform.localRotation = Quaternion.Euler(0, 0, angle);

            glow.enabled = highlighted;
            if (highlighted)
            {
                float pulse = 0.55f + 0.35f * Mathf.Sin(Time.time * 5f);
                glow.color = new Color(highlightColor.r, highlightColor.g, highlightColor.b, Mathf.Clamp01(pulse + 0.3f));
            }
        }
    }
}
