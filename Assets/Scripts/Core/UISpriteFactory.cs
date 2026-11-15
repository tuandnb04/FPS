using UnityEngine;

namespace Core
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
        private static Sprite _chevronIcon;
        private static Sprite _damageVignette;
        private static Texture2D _softParticleTex;
        private static Texture2D _floorGridTex;
        private static Texture2D _telegraphRingTex;

        public static Sprite GetSolidRectSprite()
        {
            if (_solidRect != null) return _solidRect;
            var tex = new Texture2D(16, 16, TextureFormat.RGBA32, false)
            {
                name = "UI_SolidRectTex",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            var fill = new Color(1f, 1f, 1f, 1f);
            var pixels = new Color[16 * 16];
            for (var i = 0; i < pixels.Length; i++) pixels[i] = fill;
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
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "UI_RoundedRectTex",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            var pixels = new Color[size * size];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var alpha = RoundedBoxAlpha(x, y, size, size, radius);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();

            var border = new Vector4(radius + 2, radius + 2, radius + 2, radius + 2);
            _roundedRect = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
            _roundedRect.name = "UI_RoundedRectSprite";
            return _roundedRect;
        }

        public static Sprite GetShieldIcon()
        {
            if (_shieldIcon != null) return _shieldIcon;
            const int s = 64;
            var tex = CreateBlankTexture("UI_ShieldTex", s);
            for (var y = 0; y < s; y++)
            {
                for (var x = 0; x < s; x++)
                {
                    var nx = (x - s * 0.5f) / (s * 0.4f);
                    var ny = (y - s * 0.5f) / (s * 0.42f);
                    var insideTop = ny >= 0f && Mathf.Abs(nx) <= 0.9f && ny <= 0.9f;
                    var insideBottom = ny is < 0f and >= -0.95f && Mathf.Abs(nx) <= 0.9f * (1f - (-ny / 1.05f) * 0.85f);
                    if (!insideTop && !insideBottom) continue;
                    var inner = (ny >= 0.05f && Mathf.Abs(nx) <= 0.62f && ny <= 0.65f) ||
                                (ny is < 0.05f and >= -0.68f && Mathf.Abs(nx) <= 0.62f * (1f - (-ny / 0.85f) * 0.85f));
                    var a = inner ? 0.45f : 1f;
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
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
            var tex = CreateBlankTexture("UI_StarTex", s);
            for (var y = 0; y < s; y++)
            {
                for (var x = 0; x < s; x++)
                {
                    var dx = (x - s * 0.5f) / (s * 0.45f);
                    var dy = (y - s * 0.5f) / (s * 0.45f);
                    var r = Mathf.Sqrt(dx * dx + dy * dy);
                    var angle = Mathf.Atan2(dy, dx);
                    var spikes = 0.55f + 0.42f * Mathf.Cos(4f * angle);
                    if (!(r <= spikes)) continue;
                    var alpha = Mathf.Clamp01((spikes - r) * 8f);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
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
            var tex = CreateBlankTexture("UI_CrosshairTex", s);
            for (var y = 0; y < s; y++)
            {
                for (var x = 0; x < s; x++)
                {
                    var dx = x - s * 0.5f;
                    var dy = y - s * 0.5f;
                    var r = Mathf.Sqrt(dx * dx + dy * dy);
                    var ring = r is >= 16f and <= 20f;
                    var horiz = Mathf.Abs(dy) <= 2f && Mathf.Abs(dx) >= 8f && Mathf.Abs(dx) <= 27f;
                    var vert = Mathf.Abs(dx) <= 2f && Mathf.Abs(dy) >= 8f && Mathf.Abs(dy) <= 27f;
                    var center = r <= 3f;
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
            var tex = CreateBlankTexture("UI_RingTex", s);
            for (var y = 0; y < s; y++)
            {
                for (var x = 0; x < s; x++)
                {
                    var dx = x - s * 0.5f;
                    var dy = y - s * 0.5f;
                    var r = Mathf.Sqrt(dx * dx + dy * dy);
                    if (!(r >= 22f) || !(r <= 28f)) continue;
                    var edge = Mathf.Min(r - 22f, 28f - r);
                    var a = Mathf.Clamp01(edge / 1.5f);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
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
            var tex = CreateBlankTexture("UI_CircleTex", s);
            const float center = (s - 1) * 0.5f;
            const float maxR = s * 0.48f;
            for (var y = 0; y < s; y++)
            {
                for (var x = 0; x < s; x++)
                {
                    var dx = x - center;
                    var dy = y - center;
                    var r = Mathf.Sqrt(dx * dx + dy * dy);
                    if (!(r <= maxR)) continue;
                    var a = Mathf.Clamp01((maxR - r) / 1.5f);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }
            tex.Apply();
            _circleSprite = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 100f);
            _circleSprite.name = "UI_CircleSprite";
            return _circleSprite;
        }

        public static Sprite GetChevronIcon()
        {
            if (_chevronIcon != null) return _chevronIcon;
            const int s = 32;
            var tex = CreateBlankTexture("UI_ChevronTex", s);
            for (var y = 0; y < s; y++)
            {
                for (var x = 0; x < s; x++)
                {
                    var nx = (x - (s - 1) * 0.5f) / (s * 0.38f);
                    var ny = (y - (s - 1) * 0.5f) / (s * 0.42f);
                    if (!(ny >= -0.75f) || !(ny <= 0.85f)) continue;
                    var halfW = (0.85f - ny) / 1.6f * 0.92f;
                    var notch = -0.35f - Mathf.Abs(nx) * 0.45f;
                    if (Mathf.Abs(nx) <= halfW && ny >= notch)
                    {
                        tex.SetPixel(x, y, new Color(1f, 1f, 1f, 1f));
                    }
                }
            }
            tex.Apply();
            _chevronIcon = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 100f);
            _chevronIcon.name = "UI_ChevronIcon";
            return _chevronIcon;
        }

        public static Sprite GetDamageVignetteSprite()
        {
            if (_damageVignette != null) return _damageVignette;
            const int s = 128;
            var tex = CreateBlankTexture("UI_DamageVignetteTex", s);
            const float center = (s - 1) * 0.5f;
            for (var y = 0; y < s; y++)
            {
                for (var x = 0; x < s; x++)
                {
                    var nx = Mathf.Abs(x - center) / center;
                    var ny = Mathf.Abs(y - center) / center;
                    var edge = Mathf.Sqrt(nx * nx * 0.65f + ny * ny * 0.65f);
                    var alpha = Mathf.Clamp01((edge - 0.52f) / 0.62f);
                    alpha = alpha * alpha * 0.92f;
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }
            tex.Apply();
            _damageVignette = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 100f);
            _damageVignette.name = "UI_DamageVignette";
            return _damageVignette;
        }

        public static Texture2D GetSoftParticleTexture()
        {
            if (_softParticleTex != null) return _softParticleTex;
            const int s = 64;
            _softParticleTex = CreateBlankTexture("VFX_SoftParticleTex", s);
            for (var y = 0; y < s; y++)
            {
                for (var x = 0; x < s; x++)
                {
                    var dx = (x - s * 0.5f) / (s * 0.48f);
                    var dy = (y - s * 0.5f) / (s * 0.48f);
                    var d = Mathf.Sqrt(dx * dx + dy * dy);
                    var a = Mathf.Clamp01(1f - d);
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
            const float center = (s - 1) * 0.5f;
            const float maxR = s * 0.485f;

            for (var y = 0; y < s; y++)
            {
                for (var x = 0; x < s; x++)
                {
                    var dx = x - center;
                    var dy = y - center;
                    var nr = Mathf.Sqrt(dx * dx + dy * dy) / maxR;
                    if (nr > 1.0f) continue;

                    var aaOuter = Mathf.Clamp01((1.0f - nr) * 32f);
                    var outerRing = Mathf.Clamp01(1f - Mathf.Abs(nr - 0.92f) / 0.065f);
                    var innerRing = Mathf.Clamp01(1f - Mathf.Abs(nr - 0.45f) / 0.03f);
                    var fill = 0.14f + 0.08f * nr;
                    var a = Mathf.Clamp01(Mathf.Max(fill, Mathf.Max(outerRing * 0.88f, innerRing * 0.36f))) * aaOuter;
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

            var baseTone = new Color(0.86f, 0.86f, 0.82f, 1f);
            var jointLine = new Color(0.62f, 0.63f, 0.59f, 1f);
            var subtleWear = new Color(0.79f, 0.79f, 0.75f, 1f);

            for (var y = 0; y < s; y++)
            {
                for (var x = 0; x < s; x++)
                {
                    var mainBorder = x < 2 || y < 2;
                    var quarterSeam = (x == 32 && y % 6 < 3) || (y == 32 && x % 6 < 3);
                    var hash = (uint)(x * 73856093 ^ y * 19349663);
                    var grain = ((hash & 31u) / 31f - 0.5f) * 0.05f;
                    var c = mainBorder ? jointLine : (quarterSeam ? subtleWear : baseTone);
                    c = new Color(
                        Mathf.Clamp01(c.r + grain),
                        Mathf.Clamp01(c.g + grain),
                        Mathf.Clamp01(c.b + grain),
                        1f);
                    _floorGridTex.SetPixel(x, y, c);
                }
            }
            _floorGridTex.Apply();
            return _floorGridTex;
        }

        private static Texture2D CreateBlankTexture(string name, int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            var clear = new Color[size * size];
            for (var i = 0; i < clear.Length; i++) clear[i] = Palette.Transparent;
            tex.SetPixels(clear);
            return tex;
        }

        private static float RoundedBoxAlpha(int x, int y, int width, int height, int radius)
        {
            var px = x + 0.5f;
            var py = y + 0.5f;
            var dx = Mathf.Max(0f, Mathf.Max(radius - px, px - (width - radius)));
            var dy = Mathf.Max(0f, Mathf.Max(radius - py, py - (height - radius)));
            var dist = Mathf.Sqrt(dx * dx + dy * dy);
            return Mathf.Clamp01((radius + 0.5f) - dist);
        }
    }
}
