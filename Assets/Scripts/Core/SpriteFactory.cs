using UnityEngine;

namespace RhythmPlayer.Core
{
    /// <summary>
    /// 精灵工厂：运行时生成纯色矩形精灵。
    /// 当皮肤素材缺失时，所有音符/特效都会退回这个 1x1 白方块以保证程序能跑。
    /// </summary>
    public static class SpriteFactory
    {
        static Sprite square;

        /// <summary>生成"下窄上宽"的梯形精灵，用于落点分区高亮（innerRatio = 下边宽 / 上边宽）</summary>
        public static Sprite Trapezoid(float innerRatio)
        {
            const int size = 64;
            var ratio = Mathf.Clamp(innerRatio, 0.05f, 1f);
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            var pixels = new Color32[size * size];

            for (var y = 0; y < size; y++)
            {
                var t = (float)y / (size - 1);
                var half = Mathf.Lerp(ratio, 1f, t) * 0.5f;
                var left = (0.5f - half) * size;
                var right = (0.5f + half) * size;
                for (var x = 0; x < size; x++)
                {
                    var inside = x >= left - 0.5f && x <= right - 0.5f;
                    pixels[y * size + x] = inside ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();

            var sprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        /// <summary>1x1 白色精灵（pixelsPerUnit = 1），配合缩放和颜色当任意矩形用</summary>
        public static Sprite Square()
        {
            if (square != null) return square;

            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            texture.SetPixel(0, 0, Color.white);
            texture.Apply();

            square = Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
            square.hideFlags = HideFlags.HideAndDontSave;
            return square;
        }
    }
}
