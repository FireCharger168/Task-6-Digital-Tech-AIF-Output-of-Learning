using System.Collections.Generic;
using UnityEngine;

namespace HereToSlay.View
{
    /// <summary>Sprite loading helpers. All art lives under Assets/Resources so it can be loaded at runtime.</summary>
    public static class Art
    {
        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();
        private static Sprite white;
        private static Material unlitMaterial;

        /// <summary>
        /// Unlit sprite material. URP 2D lights only affect the sorting layers they target, so lit sprites on our
        /// custom layers would render black; unlit sprites always show at full brightness.
        /// </summary>
        public static Material SpriteMaterial
        {
            get
            {
                if (unlitMaterial == null)
                {
                    Shader shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
                    if (shader == null)
                    {
                        shader = Shader.Find("Sprites/Default");
                    }

                    unlitMaterial = new Material(shader) { name = "HereToSlay Sprite Unlit" };
                }

                return unlitMaterial;
            }
        }

        public static SpriteRenderer AddRenderer(GameObject go)
        {
            SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
            renderer.sharedMaterial = SpriteMaterial;
            return renderer;
        }

        public const string BackgroundFolder = "Backgrounds/";

        public static Sprite Load(string path, float pixelsPerUnit = 100f, Vector4 border = default)
        {
            string key = path + "|" + pixelsPerUnit + "|" + border;
            if (Cache.TryGetValue(key, out Sprite cached) && cached != null)
            {
                return cached;
            }

            Texture2D texture = Resources.Load<Texture2D>(path);
            if (texture == null)
            {
                Debug.LogWarning($"[HereToSlay] Missing texture at Resources/{path}");
                return White;
            }

            texture.wrapMode = TextureWrapMode.Clamp;
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f),
                pixelsPerUnit, 0, SpriteMeshType.FullRect, border);
            sprite.name = path;
            Cache[key] = sprite;
            return sprite;
        }

        public static Sprite White
        {
            get
            {
                if (white == null)
                {
                    Texture2D texture = new Texture2D(4, 4);
                    Color[] pixels = new Color[16];
                    for (int i = 0; i < pixels.Length; i++)
                    {
                        pixels[i] = Color.white;
                    }

                    texture.SetPixels(pixels);
                    texture.Apply();
                    white = Sprite.Create(texture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
                }

                return white;
            }
        }

        /// <summary>A 9-sliced rounded rectangle, 64px texture with 20px corners, 100 px per unit.</summary>
        public static Sprite Panel => Load(BackgroundFolder + "panel", 100f, new Vector4(20, 20, 20, 20));
        public static Sprite Frame => Load(BackgroundFolder + "frame", 100f, new Vector4(20, 20, 20, 20));
        public static Sprite Glow => Load(BackgroundFolder + "glow", 100f, new Vector4(40, 40, 40, 40));

        public static Sprite CardBack => CardFaceById("cardBack000");

        public static Sprite CardFace(CardDefinition definition)
        {
            if (definition == null)
            {
                return CardBack;
            }

            Sprite sprite = definition.LoadSprite();
            return sprite != null ? sprite : White;
        }

        public static Sprite CardFaceById(string id)
        {
            CardDefinition definition = HereToSlayCardDatabase.GetCard(id);
            return definition != null && definition.LoadSprite() != null ? definition.LoadSprite() : White;
        }

        public static Color ClassColor(HeroClass heroClass)
        {
            switch (heroClass)
            {
                case HeroClass.Fighter: return new Color(0.86f, 0.25f, 0.22f);
                case HeroClass.Bard: return new Color(0.95f, 0.55f, 0.18f);
                case HeroClass.Guardian: return new Color(0.96f, 0.80f, 0.20f);
                case HeroClass.Ranger: return new Color(0.35f, 0.72f, 0.30f);
                case HeroClass.Thief: return new Color(0.25f, 0.50f, 0.90f);
                case HeroClass.Wizard: return new Color(0.62f, 0.35f, 0.85f);
                default: return new Color(0.8f, 0.8f, 0.8f);
            }
        }
    }
}
