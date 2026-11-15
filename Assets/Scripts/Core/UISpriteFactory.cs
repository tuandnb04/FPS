using UnityEngine;

namespace TopDownShooter.Core
{
    /// <summary>
    /// Procedurally generates crisp palette-compliant UI sprites, icons, particle shapes,
    /// and subtle floor grid textures so no default Unity skin or white placeholder appears.
    /// </summary>
    public static class UISpriteFactory
    {
        private static Sprite _solidRect;
        private static Sprite _roundedRect;
        private static Sprite _circleSprite;
        private static Sprite _ringSprite;
        private static Sprite _shieldIcon;
        private static Sprite _starIcon;
        private static Sprite _crosshairIcon;
        private static Sprite _speakerIcon;
        private static Texture2D _softParticleTex;
        private static Texture2D _floorGridTex;
        private static Texture2D _telegraphRingTex;

        public static Sprite GetSolidRectSprite()
        {
            if (_solidRect != null) return _solidRect;
            Texture2D tex = new Texture2D(16, 16, TextureFormat.RGBA32, false)
            {
                name = "UI_SolidRectTex",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            Color fill = new Color(1f, 1f, 1f, 1f);
            Color[] pixels = new Color[16 * 16];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = fill;
            tex.SetPixels(pixels);
            tex.Apply();
            _solidRect = Sprite.Create(tex, new Rect(0, 0, 16, 16), new Vector2(0.5f, 0.5f), 100f);
            _solidRect.name = "UI_SolidRectSprite";
            return _solidRect;
        }

        public static Sprite GetRoundedRectSprite()
        {
            if (_roundedRect != null) return _roundedRect;
            const int size = 64;
            const int radius = 8;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "UI_RoundedRectTex",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float alpha = RoundedBoxAlpha(x, y, size, size, radius);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();

            Vector4 border = new Vector4(radius + 2, radius + 2, radius + 2, radius + 2);
            _roundedRect = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
            _roundedRect.name = "UI_RoundedRectSprite";
            return _roundedRect;
        }

        public static Sprite GetShieldIcon()
        {
            if (_shieldIcon != null) return _shieldIcon;
            const int s = 64;
            Texture2D tex = CreateBlankTexture("UI_ShieldTex", s);
            for (int y = 0; y < s; y++)
            {
                for (int x = 0; x < s; x++)
                {
                    float nx = (x - s * 0.5f) / (s * 0.4f);
                    float ny = (y - s * 0.5f) / (s * 0.42f);
                    bool insideTop = ny >= 0f && Mathf.Abs(nx) <= 0.9f && ny <= 0.9f;
                    bool insideBottom = ny < 0f && ny >= -0.95f && Mathf.Abs(nx) <= 0.9f * (1f - (-ny / 1.05f) * 0.85f);
                    if (insideTop || insideBottom)
                    {
                        bool inner = (ny >= 0.05f && Mathf.Abs(nx) <= 0.62f && ny <= 0.65f) ||
                                     (ny < 0.05f && ny >= -0.68f && Mathf.Abs(nx) <= 0.62f * (1f - (-ny / 0.85f) * 0.85f));
                        float a = inner ? 0.45f : 1f;
                        tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                    }
                }
            }
            tex.Apply();
            _shieldIcon = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 100f);
            _shieldIcon.name = "UI_ShieldIcon";
            return _shieldIcon;
        }

        public static Sprite GetStarIcon()
        {
            if (_starIcon != null) return _starIcon;
            const int s = 64;
            Texture2D tex = CreateBlankTexture("UI_StarTex", s);
            for (int y = 0; y < s; y++)
            {
                for (int x = 0; x < s; x++)
                {
                    float dx = (x - s * 0.5f) / (s * 0.45f);
                    float dy = (y - s * 0.5f) / (s * 0.45f);
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float angle = Mathf.Atan2(dy, dx);
                    float spikes = 0.55f + 0.42f * Mathf.Cos(4f * angle);
                    if (r <= spikes)
                    {
                        float alpha = Mathf.Clamp01((spikes - r) * 8f);
                        tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                    }
                }
            }
            tex.Apply();
            _starIcon = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 100f);
            _starIcon.name = "UI_StarIcon";
            return _starIcon;
        }

        public static Sprite GetCrosshairIcon()
        {
            if (_crosshairIcon != null) return _crosshairIcon;
            const int s = 64;
            Texture2D tex = CreateBlankTexture("UI_CrosshairTex", s);
            for (int y = 0; y < s; y++)
            {
                for (int x = 0; x < s; x++)
                {
                    float dx = x - s * 0.5f;
                    float dy = y - s * 0.5f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    bool ring = r >= 16f && r <= 20f;
                    bool horiz = Mathf.Abs(dy) <= 2f && Mathf.Abs(dx) >= 8f && Mathf.Abs(dx) <= 27f;
                    bool vert = Mathf.Abs(dx) <= 2f && Mathf.Abs(dy) >= 8f && Mathf.Abs(dy) <= 27f;
                    bool center = r <= 3f;
                    if (ring || horiz || vert || center)
                    {
                        tex.SetPixel(x, y, new Color(1f, 1f, 1f, 1f));
                    }
                }
            }
            tex.Apply();
            _crosshairIcon = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 100f);
            _crosshairIcon.name = "UI_CrosshairIcon";
            return _crosshairIcon;
        }

        public static Sprite GetRingSprite()
        {
            if (_ringSprite != null) return _ringSprite;
            const int s = 64;
            Texture2D tex = CreateBlankTexture("UI_RingTex", s);
            for (int y = 0; y < s; y++)
            {
                for (int x = 0; x < s; x++)
                {
                    float dx = x - s * 0.5f;
                    float dy = y - s * 0.5f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    if (r >= 22f && r <= 28f)
                    {
                        float edge = Mathf.Min(r - 22f, 28f - r);
                        float a = Mathf.Clamp01(edge / 1.5f);
                        tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                    }
                }
            }
            tex.Apply();
            _ringSprite = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 100f);
            _ringSprite.name = "UI_RingSprite";
            return _ringSprite;
        }

        public static Sprite GetCircleSprite()
        {
            if (_circleSprite != null) return _circleSprite;
            const int s = 64;
            Texture2D tex = CreateBlankTexture("UI_CircleTex", s);
            float center = (s - 1) * 0.5f;
            float maxR = s * 0.48f;
            for (int y = 0; y < s; y++)
            {
                for (int x = 0; x < s; x++)
                {
                    float dx = x - center;
                    float dy = y - center;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    if (r <= maxR)
                    {
                        float a = Mathf.Clamp01((maxR - r) / 1.5f);
                        tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                    }
                }
            }
            tex.Apply();
            _circleSprite = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 100f);
            _circleSprite.name = "UI_CircleSprite";
            return _circleSprite;
        }

        public static Sprite GetSpeakerIcon()
        {
            if (_speakerIcon != null) return _speakerIcon;
            const int s = 64;
            Texture2D tex = CreateBlankTexture("UI_SpeakerTex", s);
            for (int y = 0; y < s; y++)
            {
                for (int x = 0; x < s; x++)
                {
                    bool body = x >= 12 && x <= 24 && y >= 22 && y <= 42;
                    bool cone = x > 24 && x <= 38 && Mathf.Abs(y - 32) <= 10 + (x - 24) * 0.85f;
                    float dx = x - 36f;
                    float dy = y - 32f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    bool wave1 = x >= 40 && r >= 11f && r <= 14.5f;
                    bool wave2 = x >= 44 && r >= 19f && r <= 22.5f;
                    if (body || cone || wave1 || wave2)
                    {
                        tex.SetPixel(x, y, new Color(1f, 1f, 1f, 1f));
                    }
                }
            }
            tex.Apply();
            _speakerIcon = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 100f);
            _speakerIcon.name = "UI_SpeakerIcon";
            return _speakerIcon;
        }

        public static Texture2D GetSoftParticleTexture()
        {
            if (_softParticleTex != null) return _softParticleTex;
            const int s = 64;
            _softParticleTex = CreateBlankTexture("VFX_SoftParticleTex", s);
            for (int y = 0; y < s; y++)
            {
                for (int x = 0; x < s; x++)
                {
                    float dx = (x - s * 0.5f) / (s * 0.48f);
                    float dy = (y - s * 0.5f) / (s * 0.48f);
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(1f - d);
                    a = a * a;
                    _softParticleTex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }
            _softParticleTex.Apply();
            return _softParticleTex;
        }

        public static Texture2D GetTelegraphRingTexture()
        {
            if (_telegraphRingTex != null) return _telegraphRingTex;
            const int s = 128;
            _telegraphRingTex = CreateBlankTexture("VFX_TelegraphRingTex", s);
            float center = (s - 1) * 0.5f;
            float maxR = s * 0.485f;

            for (int y = 0; y < s; y++)
            {
                for (int x = 0; x < s; x++)
                {
                    float dx = x - center;
                    float dy = y - center;
                    float nr = Mathf.Sqrt(dx * dx + dy * dy) / maxR;
                    if (nr > 1.0f) continue;

                    float aaOuter = Mathf.Clamp01((1.0f - nr) * 32f);
                    float outerRing = Mathf.Clamp01(1f - Mathf.Abs(nr - 0.92f) / 0.065f);
                    float innerRing = Mathf.Clamp01(1f - Mathf.Abs(nr - 0.45f) / 0.03f);
                    float fill = 0.14f + 0.08f * nr;
                    float a = Mathf.Clamp01(Mathf.Max(fill, Mathf.Max(outerRing * 0.88f, innerRing * 0.36f))) * aaOuter;
                    _telegraphRingTex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }

            _telegraphRingTex.Apply();
            return _telegraphRingTex;
        }

        public static Texture2D GetArenaFloorGridTexture()
        {
            if (_floorGridTex != null) return _floorGridTex;
            const int s = 64;
            _floorGridTex = new Texture2D(s, s, TextureFormat.RGBA32, true)
            {
                name = "Arena_FloorGridTex",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Repeat
            };

            Color baseTone = new Color(0.88f, 0.90f, 0.94f, 1f);
            Color gridLine = new Color(1.00f, 1.00f, 1.00f, 1f);
            Color subTick = new Color(0.95f, 0.97f, 1.00f, 1f);

            for (int y = 0; y < s; y++)
            {
                for (int x = 0; x < s; x++)
                {
                    bool mainBorder = x < 2 || y < 2;
                    bool centerCross = (x == 32 && y % 4 < 2) || (y == 32 && x % 4 < 2);
                    Color c = mainBorder ? gridLine : (centerCross ? subTick : baseTone);
                    _floorGridTex.SetPixel(x, y, c);
                }
            }
            _floorGridTex.Apply();
            return _floorGridTex;
        }

        private static Texture2D CreateBlankTexture(string name, int size)
        {
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            Color[] clear = new Color[size * size];
            for (int i = 0; i < clear.Length; i++) clear[i] = Palette.Transparent;
            tex.SetPixels(clear);
            return tex;
        }

        private static float RoundedBoxAlpha(int x, int y, int width, int height, int radius)
        {
            float px = x + 0.5f;
            float py = y + 0.5f;
            float dx = Mathf.Max(0f, Mathf.Max(radius - px, px - (width - radius)));
            float dy = Mathf.Max(0f, Mathf.Max(radius - py, py - (height - radius)));
            float dist = Mathf.Sqrt(dx * dx + dy * dy);
            return Mathf.Clamp01((radius + 0.5f) - dist);
        }
    }
}
