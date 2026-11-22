// MenuSceneObjects.cs —— 主菜单元素的场景工厂。
// 尺寸全部烘焙进 Mesh 顶点，Transform 不带缩放：1 世界单位 = 1 像素，子对象坐标即像素，
// 这样层级里任何层（Slot / Button / Label）都能直接用像素数摆位置。
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace Ra2md.Menu
{
    public static class MenuSceneObjects
    {
        public const int PlaneWidth = 800;
        public const int PlaneHeight = 600;
        public const float CameraDistance = 10f;

        private static Shader _shader;
        private static Texture2D _transparent;

        /// <summary>缺图时的透明兜底：材质没贴图会渲染成纯白，必须显式给透明纹理。</summary>
        public static Texture2D TransparentTexture
        {
            get
            {
                if (_transparent == null)
                {
                    _transparent = new Texture2D(1, 1, TextureFormat.RGBA32, false)
                    {
                        name = "MenuTransparent",
                        filterMode = FilterMode.Point,
                        wrapMode = TextureWrapMode.Clamp,
                    };
                    _transparent.SetPixel(0, 0, new Color(0f, 0f, 0f, 0f));
                    _transparent.Apply(false, false);
                }
                return _transparent;
            }
        }

        public static Shader QuadShader
        {
            get
            {
                if (_shader == null)
                {
                    _shader = Shader.Find("Ra2md/MenuQuad");
                    if (_shader == null) _shader = Shader.Find("Unlit/Transparent");
                }
                return _shader;
            }
        }

        public static Material NewMaterial(Texture texture, int sortingOrder)
        {
            var shader = QuadShader;
            if (shader == null)
            {
                Debug.LogError("[菜单] 找不到 MenuQuad / Unlit/Transparent 着色器");
                shader = Shader.Find("Sprites/Default");
            }

            var material = new Material(shader);
            material.name = "MenuQuad_" + sortingOrder;
            material.mainTexture = texture;
            if (material.HasProperty("_MainTex") && texture != null) material.SetTexture("_MainTex", texture);
            if (material.HasProperty("_Color")) material.SetColor("_Color", Color.white);
            material.renderQueue = 3000 + sortingOrder;
            return material;
        }

        public static Mesh CreateQuadMesh(int width, int height)
        {
            var mesh = new Mesh();
            mesh.name = "MenuQuad_" + width + "x" + height;

            var halfWidth = width * 0.5f;
            var halfHeight = height * 0.5f;

            mesh.vertices = new[]
            {
                new Vector3(-halfWidth, -halfHeight, 0f),
                new Vector3(halfWidth, -halfHeight, 0f),
                new Vector3(halfWidth, halfHeight, 0f),
                new Vector3(-halfWidth, halfHeight, 0f),
            };
            mesh.uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0f, 1f),
            };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateBounds();
            return mesh;
        }

        private static GameObject QuadCore(Transform parent, string name, Texture texture,
            int width, int height, int sortingOrder, bool interactive)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = CreateQuadMesh(width, height);

            // 贴图为 null 时 NRE 不报错但会渲染成一片白，等于整块界面“消失”，这里显式换成透明纹理。
            var effective = texture != null ? texture : (Texture)TransparentTexture;

            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = NewMaterial(effective, sortingOrder);
            renderer.sortingOrder = sortingOrder;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

            if (interactive)
            {
                var box = go.AddComponent<BoxCollider>();
                box.size = new Vector3(width, height, 4f);
            }

            return go;
        }

        /// <summary>按 800×600 的 UI 像素矩形（左上原点）摆在 MenuWorld 下。</summary>
        public static GameObject Quad(Transform parent, string name, Texture texture,
            int x, int y, int width, int height, int sortingOrder, bool interactive)
        {
            var go = QuadCore(parent, name, texture, width, height, sortingOrder, interactive);
            go.transform.localPosition = new Vector3(
                x + width * 0.5f,
                PlaneHeight - y - height * 0.5f,
                0f);
            return go;
        }

        /// <summary>以父对象中心为基准的 Quad（层级内部用，坐标即像素偏移）。</summary>
        public static GameObject QuadLocal(Transform parent, string name, Texture texture,
            int width, int height, int sortingOrder, bool interactive, float offsetX = 0f, float offsetY = 0f)
        {
            var go = QuadCore(parent, name, texture, width, height, sortingOrder, interactive);
            go.transform.localPosition = new Vector3(offsetX, offsetY, 0f);
            return go;
        }

        public static void SetTexture(GameObject go, Texture texture)
        {
            if (go == null) return;
            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null && renderer.sharedMaterial != null)
            {
                var effective = texture != null ? texture : (Texture)TransparentTexture;
                renderer.sharedMaterial.mainTexture = effective;
                if (renderer.sharedMaterial.HasProperty("_MainTex"))
                {
                    renderer.sharedMaterial.SetTexture("_MainTex", effective);
                }
            }
        }

        public static void SetAlpha(GameObject go, float alpha)
        {
            if (go == null) return;
            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer == null || renderer.sharedMaterial == null) return;
            if (!renderer.sharedMaterial.HasProperty("_Color")) return;
            var color = Color.white;
            color.a = alpha;
            renderer.sharedMaterial.SetColor("_Color", color);
        }

        public static void SetVisible(GameObject go, bool visible)
        {
            if (go == null) return;
            if (go.activeSelf != visible) go.SetActive(visible);
        }

        /// <summary>在任意 GameObject 下挂 WorldSpace Canvas（父级无缩放，Canvas 1 单位 = 1 像素）。</summary>
        public static RectTransform AttachLabelCanvas(GameObject host, int width, int height, int sortingOrder)
        {
            var go = new GameObject("LabelCanvas", typeof(RectTransform));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(host.transform, false);
            rect.localPosition = new Vector3(0f, 0f, -0.05f);
            rect.localRotation = Quaternion.identity;
            rect.localScale = Vector3.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(width, height);

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.overrideSorting = true;
            canvas.sortingOrder = sortingOrder;

            var raycaster = go.AddComponent<GraphicRaycaster>();
            raycaster.enabled = false;

            return rect;
        }

        /// <summary>在 800×600 平面（左上原点）里放一段文字，坐标与 Quad 同一套。</summary>
        public static Text AddCenteredTextAt(Transform canvasParent, string text, int x, int yTop,
            int width, int height, int fontSize, Color color)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(canvasParent, false);
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 0f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = new Vector2(x, PlaneHeight - yTop);

            var label = go.AddComponent<Text>();
            label.text = text;
            label.font = MenuFonts.Chinese();
            label.fontSize = fontSize;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = color;
            label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;

            var outline = go.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
            outline.effectDistance = new Vector2(1f, -1f);
            return label;
        }

        /// <summary>在 Canvas 里创建一个居中、不参与拾取的文字。</summary>
        public static Text AddCenteredText(RectTransform host, string text, int width, int height,
            int fontSize, Color color)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(host, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(width, height);

            var label = go.AddComponent<Text>();
            label.text = text;
            label.font = MenuFonts.Chinese();
            label.fontSize = fontSize;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = color;
            label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;

            var outline = go.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
            outline.effectDistance = new Vector2(1f, -1f);

            return label;
        }
    }
}
