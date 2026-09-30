using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HereToSlay.View
{
    /// <summary>
    /// The layered 2D environment behind the table: sky, three parallax ridges and drifting clouds.
    /// Each layer moves a different amount with the mouse to give depth.
    /// </summary>
    public sealed class SceneryLayers : MonoBehaviour
    {
        private sealed class Layer
        {
            public Transform transform;
            public Vector3 basePosition;
            public float parallax;
            public float drift;
            public float width;
        }

        private readonly List<Layer> layers = new List<Layer>();
        private Camera cam;
        private Vector2 smoothedMouse;

        public void Build(Camera targetCamera)
        {
            cam = targetCamera;
            float halfH = cam.orthographicSize;
            float halfW = halfH * cam.aspect;
            float width = halfW * 2f + 1.2f;

            AddLayer("Sky", "sky", Layers.Background, Layers.BackgroundBase, new Vector3(0, 0, 0), width, 0.05f, 0f, true, halfH * 2f + 1.2f);
            AddLayer("Clouds A", "clouds", Layers.Scenery, Layers.SceneryBase + 0, new Vector3(-3f, halfH - 0.9f, 0), width * 0.55f, 0.12f, 0.12f);
            AddLayer("Mountains Far", "mountainsFar", Layers.Scenery, Layers.SceneryBase + 10, new Vector3(0, halfH - 0.8f, 0), width * 1.1f, 0.18f, 0f);
            AddLayer("Clouds B", "clouds", Layers.Scenery, Layers.SceneryBase + 15, new Vector3(4f, halfH - 1.3f, 0), width * 0.45f, 0.25f, -0.2f);
            AddLayer("Mountains Mid", "mountainsMid", Layers.Scenery, Layers.SceneryBase + 20, new Vector3(0, halfH - 1.45f, 0), width * 1.1f, 0.3f, 0f);
            AddLayer("Forest Near", "forestNear", Layers.Scenery, Layers.SceneryBase + 30, new Vector3(0, halfH - 1.8f, 0), width * 1.1f, 0.45f, 0f);
        }

        private void AddLayer(string name, string sprite, string sortingLayer, int order, Vector3 position, float worldWidth, float parallax, float drift, bool stretchHeight = false, float worldHeight = 0f)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(transform, false);
            SpriteRenderer renderer = Art.AddRenderer(go);
            renderer.sprite = Art.Load(Art.BackgroundFolder + sprite);
            renderer.sortingLayerName = sortingLayer;
            renderer.sortingOrder = order;

            Vector2 size = renderer.sprite.bounds.size;
            float scaleX = worldWidth / Mathf.Max(0.01f, size.x);
            float scaleY = stretchHeight ? worldHeight / Mathf.Max(0.01f, size.y) : scaleX;
            go.transform.localScale = new Vector3(scaleX, scaleY, 1f);
            go.transform.localPosition = position;

            layers.Add(new Layer { transform = go.transform, basePosition = position, parallax = parallax, drift = drift, width = worldWidth });
        }

        private void Update()
        {
            if (cam == null)
            {
                return;
            }

            Vector2 mouse = Vector2.zero;
            if (Mouse.current != null)
            {
                Vector2 pixel = Mouse.current.position.ReadValue();
                mouse = new Vector2(pixel.x / Mathf.Max(1, Screen.width) - 0.5f, pixel.y / Mathf.Max(1, Screen.height) - 0.5f);
            }

            smoothedMouse = Vector2.Lerp(smoothedMouse, mouse, 1f - Mathf.Exp(-3f * Time.deltaTime));
            float halfW = cam.orthographicSize * cam.aspect;

            foreach (Layer layer in layers)
            {
                Vector3 position = layer.basePosition;
                if (layer.drift != 0f)
                {
                    float span = halfW * 2f + layer.width;
                    float x = layer.basePosition.x + Time.time * layer.drift;
                    x = Mathf.Repeat(x + span / 2f, span) - span / 2f;
                    position.x = x;
                }

                position.x -= smoothedMouse.x * layer.parallax;
                position.y -= smoothedMouse.y * layer.parallax * 0.5f;
                layer.transform.localPosition = position;
            }
        }
    }
}
