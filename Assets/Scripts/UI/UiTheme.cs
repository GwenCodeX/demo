using UnityEngine;

namespace RhythmPlayer.UI
{
    /// <summary>
    /// 界面主题：所有纹理都在运行时用代码绘制（渐变背景、细网格、圆角面板、按钮三态、强调线），
    /// 不依赖任何图片文件——"简洁科技风"且零体积。
    /// 配色：深蓝黑底 + 青色强调 + 白/浅青文字。
    /// </summary>
    public static class UiTheme
    {
        // ===== 配色 =====
        public static readonly Color BackgroundTop = new Color(0.030f, 0.045f, 0.075f);
        public static readonly Color BackgroundBottom = new Color(0.075f, 0.105f, 0.155f);
        public static readonly Color Accent = new Color(0.35f, 0.85f, 1f);
        public static readonly Color TextMain = new Color(0.92f, 0.97f, 1f);
        public static readonly Color TextDim = new Color(0.52f, 0.66f, 0.80f, 0.9f);

        static Texture2D background;
        static Texture2D grid;
        static Texture2D panel;
        static Texture2D rowNormal;
        static Texture2D rowHover;
        static Texture2D rowActive;
        static Texture2D accentLine;

        // ===== 纹理（懒生成，只做一次） =====

        /// <summary>整屏背景：竖向渐变</summary>
        public static Texture2D Background() => background ??= VerticalGradient(BackgroundTop, BackgroundBottom, 256);

        /// <summary>细网格（平铺用，wrapMode 为 Repeat）</summary>
        public static Texture2D Grid() => grid ??= GridTexture(64, 1, new Color(0.4f, 0.7f, 1f, 0.05f));

        /// <summary>圆角面板底（配合 GUIStyle.border 做九宫格拉伸）</summary>
        public static Texture2D Panel() => panel ??= RoundedRect(64, 14,
            new Color(0.075f, 0.11f, 0.16f, 0.92f), new Color(0.35f, 0.75f, 1f, 0.28f), 2);

        public static Texture2D RowNormal() => rowNormal ??= RoundedRect(64, 10,
            new Color(0.09f, 0.13f, 0.19f, 0.88f), new Color(0.35f, 0.75f, 1f, 0.18f), 2);

        public static Texture2D RowHover() => rowHover ??= RoundedRect(64, 10,
            new Color(0.14f, 0.22f, 0.31f, 0.95f), new Color(0.4f, 0.9f, 1f, 0.85f), 2);

        public static Texture2D RowActive() => rowActive ??= RoundedRect(64, 10,
            new Color(0.10f, 0.26f, 0.34f, 0.95f), Accent, 2);

        /// <summary>强调线（上亮下透明的小渐变）</summary>
        public static Texture2D AccentLine() => accentLine ??= VerticalGradient(Accent, new Color(Accent.r, Accent.g, Accent.b, 0f), 16);

        /// <summary>画整屏背景：渐变 + 平铺网格</summary>
        public static void DrawBackdrop()
        {
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Background(), ScaleMode.StretchToFill);
            GUI.DrawTextureWithTexCoords(new Rect(0f, 0f, Screen.width, Screen.height), Grid(),
                new Rect(0f, 0f, Screen.width / 64f, Screen.height / 64f));
        }

        // ===== 样式工厂（需在 OnGUI 里调用） =====

        /// <summary>大标题</summary>
        public static GUIStyle TitleStyle() => new GUIStyle(GUI.skin.label)
        {
            fontSize = 46,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            normal = { textColor = TextMain },
        };

        /// <summary>居中提示 / 副标题</summary>
        public static GUIStyle HintStyle() => new GUIStyle(GUI.skin.label)
        {
            fontSize = 17,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = TextDim },
        };

        /// <summary>歌曲行按钮·选中态</summary>
        public static GUIStyle RowSelectedStyle() => new GUIStyle(GUI.skin.button)
        {
            fontSize = 20,
            alignment = TextAnchor.MiddleLeft,
            normal = { background = RowActive(), textColor = TextMain },
            hover = { background = RowActive(), textColor = TextMain },
            active = { background = RowActive(), textColor = TextMain },
            border = new RectOffset(14, 14, 14, 14),
            padding = new RectOffset(18, 12, 6, 6),
        };

        /// <summary>歌曲行按钮·普通态（小菜单用）</summary>
        public static GUIStyle MenuRowStyle() => new GUIStyle(GUI.skin.button)
        {
            fontSize = 20,
            alignment = TextAnchor.MiddleLeft,
            normal = { background = RowNormal(), textColor = TextMain },
            hover = { background = RowHover(), textColor = TextMain },
            active = { background = RowActive(), textColor = TextMain },
            border = new RectOffset(14, 14, 14, 14),
            padding = new RectOffset(18, 12, 6, 6),
        };

        /// <summary>小按钮（暂停键等）</summary>
        public static GUIStyle SmallButtonStyle() => new GUIStyle(GUI.skin.button)
        {
            fontSize = 22,
            alignment = TextAnchor.MiddleCenter,
            normal = { background = RowNormal(), textColor = TextMain },
            hover = { background = RowHover(), textColor = TextMain },
            active = { background = RowActive(), textColor = TextMain },
            border = new RectOffset(14, 14, 14, 14),
            padding = new RectOffset(10, 10, 6, 6),
        };

        /// <summary>菜单大按钮（暂停菜单四项等，触摸友好）</summary>
        public static GUIStyle MenuButtonStyle() => new GUIStyle(GUI.skin.button)
        {
            fontSize = 26,
            alignment = TextAnchor.MiddleCenter,
            normal = { background = RowNormal(), textColor = TextMain },
            hover = { background = RowHover(), textColor = TextMain },
            active = { background = RowActive(), textColor = TextMain },
            border = new RectOffset(14, 14, 14, 14),
            padding = new RectOffset(14, 14, 10, 10),
        };

        /// <summary>判定点的编号/按键标签</summary>
        public static GUIStyle PadLabelStyle() => new GUIStyle(GUI.skin.label)
        {
            fontSize = 16,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = new Color(0.6f, 0.85f, 1f, 0.75f) },
        };

        /// <summary>圆角面板（GUI.Box 用）</summary>
        public static GUIStyle PanelStyle() => new GUIStyle(GUI.skin.box)
        {
            normal = { background = Panel() },
            border = new RectOffset(18, 18, 18, 18),
            padding = new RectOffset(18, 18, 12, 12),
        };

        /// <summary>歌曲行按钮（三种状态：普通 / 悬停 / 按下）</summary>
        public static GUIStyle RowStyle() => new GUIStyle(GUI.skin.button)
        {
            fontSize = 24,
            alignment = TextAnchor.MiddleLeft,
            normal = { background = RowNormal(), textColor = TextMain },
            hover = { background = RowHover(), textColor = TextMain },
            active = { background = RowActive(), textColor = TextMain },
            border = new RectOffset(14, 14, 14, 14),
            padding = new RectOffset(24, 16, 8, 8),
        };

        /// <summary>空槽位提示（暗色行）</summary>
        public static GUIStyle EmptyRowStyle() => new GUIStyle(GUI.skin.label)
        {
            fontSize = 20,
            alignment = TextAnchor.MiddleLeft,
            normal = { textColor = new Color(0.45f, 0.55f, 0.65f, 0.65f) },
            padding = new RectOffset(28, 16, 8, 8),
        };

        /// <summary>加载中提示（面板 + 居中文字）</summary>
        public static GUIStyle LoadingStyle() => new GUIStyle(GUI.skin.box)
        {
            fontSize = 20,
            alignment = TextAnchor.MiddleCenter,
            normal = { background = Panel(), textColor = TextMain },
            border = new RectOffset(18, 18, 18, 18),
        };

        // ===== 纹理绘制 =====

        static Texture2D VerticalGradient(Color top, Color bottom, int height)
        {
            var texture = NewTexture(4, height);
            for (var y = 0; y < height; y++)
            {
                var color = Color.Lerp(bottom, top, y / (float)(height - 1));
                for (var x = 0; x < 4; x++) texture.SetPixel(x, y, color);
            }
            texture.Apply();
            return texture;
        }

        static Texture2D GridTexture(int size, int lineWidth, Color lineColor)
        {
            var texture = NewTexture(size, size);
            texture.wrapMode = TextureWrapMode.Repeat; // 平铺
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var onLine = x < lineWidth || y < lineWidth;
                    texture.SetPixel(x, y, onLine ? lineColor : Color.clear);
                }
            }
            texture.Apply();
            return texture;
        }

        static Texture2D RoundedRect(int size, int radius, Color fill, Color border, int borderWidth)
        {
            var texture = NewTexture(size, size);
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var distance = RoundedDistance(x + 0.5f, y + 0.5f, size, radius); // >0 表示在圆角矩形内部
                    var color = Color.clear;
                    if (distance > 0f) color = fill;
                    else if (distance > -borderWidth) color = border;

                    // 边缘 1px 简单抗锯齿
                    if (distance > -1f && distance < 1f) color.a *= Mathf.Clamp01(distance + 1f);
                    texture.SetPixel(x, y, color);
                }
            }
            texture.Apply();
            return texture;
        }

        static float RoundedDistance(float x, float y, int size, float radius)
        {
            var cx = Mathf.Clamp(x, radius, size - radius);
            var cy = Mathf.Clamp(y, radius, size - radius);
            var dx = x - cx;
            var dy = y - cy;
            return radius - Mathf.Sqrt(dx * dx + dy * dy);
        }

        static Texture2D NewTexture(int width, int height)
        {
            return new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };
        }
    }
}
