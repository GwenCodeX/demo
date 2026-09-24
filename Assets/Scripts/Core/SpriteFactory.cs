using UnityEngine;

namespace RhythmPlayer.Core
{
    public static class SpriteFactory
    {
        static Sprite square;

        /// 1x1 白色精灵（pixelsPerUnit = 1），配合缩放和颜色当任意矩形用
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
