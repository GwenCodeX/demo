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
