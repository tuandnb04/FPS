using System;
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
        private static Sprite _chamferPanel;
        private static Sprite _tacticalBorder;
        private static Sprite _radarGridRing;
        private static Sprite _radarCone;
        private static Sprite _hitArc;
        private static Sprite _skullKiaIcon;
        private static Sprite _circleSprite;
        private static Sprite _shieldIcon;
        private static Sprite _crosshairIcon;
        private static Sprite _speakerIcon;
        private static Sprite _chevronIcon;
        private static Sprite _damageVignette;
        private static Sprite _ordnanceIcon;
        private static Sprite _speedIcon;
        private static Sprite _creditsIcon;
        private static Sprite _fragIcon;
        private static Sprite _artilleryIcon;
        private static Sprite _medalIcon;
        private static Sprite _intelIcon;
        private static Sprite _broadcastIcon;
        private static Sprite _lockIcon;
        private static Sprite _playIcon;
        private static Sprite _restartIcon;
        private static Sprite _homeIcon;
        private static readonly Sprite[] OperatorClassIcons = new Sprite[4];
        private static readonly Sprite[] SectorTypeIcons = new Sprite[5];
        private static Texture2D _softParticleTex;
        private static Texture2D _telegraphRingTex;
        private static Texture2D _tacticalFloorTex;

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

        public static Sprite GetCrosshairIcon()
        {
            if (_crosshairIcon != null) return _crosshairIcon;
            const int s = 64;
            var tex = CreateBlankTexture("UI_CrosshairTex", s);
            const float mid = (s - 1) * 0.5f;
            for (var y = 0; y < s; y++)
            {
                for (var x = 0; x < s; x++)
                {
                    var dx = x - mid;
                    var dy = y - mid;
                    var r = Mathf.Sqrt(dx * dx + dy * dy);
                    var horiz = Mathf.Abs(dy) <= 1.15f && Mathf.Abs(dx) is >= 7f and <= 24f;
                    var vert = Mathf.Abs(dx) <= 1.15f && Mathf.Abs(dy) is >= 7f and <= 24f;
                    var center = r <= 2.0f;
                    if (horiz || vert || center)
                    {
                        tex.SetPixel(x, y, new Color(1f, 1f, 1f, center ? 1f : 0.92f));
                    }
                }
            }
            tex.Apply();
            _crosshairIcon = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 100f);
            _crosshairIcon.name = "UI_CrosshairIcon";
            return _crosshairIcon;
        }

        public static Sprite GetChamferPanelSprite()
        {
            if (_chamferPanel != null) return _chamferPanel;
            const int s = 32;
            const int cut = 4;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false)
            {
                name = "UI_ChamferPanelTex",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            for (var y = 0; y < s; y++)
            {
                for (var x = 0; x < s; x++)
                {
                    var dTL = x + (s - 1 - y);
                    var dTR = (s - 1 - x) + (s - 1 - y);
                    var dBL = x + y;
                    var dBR = (s - 1 - x) + y;
                    var inside = dTL >= cut && dTR >= cut && dBL >= cut && dBR >= cut;
                    tex.SetPixel(x, y, inside ? Color.white : Palette.Transparent);
                }
            }
            tex.Apply();
            var border = new Vector4(cut + 3, cut + 3, cut + 3, cut + 3);
            _chamferPanel = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
            _chamferPanel.name = "UI_ChamferPanel";
            return _chamferPanel;
        }

        public static Sprite GetTacticalBorderSprite()
        {
            if (_tacticalBorder != null) return _tacticalBorder;
            const int s = 32;
            const int cut = 4;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false)
            {
                name = "UI_TacticalBorderTex",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            for (var y = 0; y < s; y++)
            {
                for (var x = 0; x < s; x++)
                {
                    var dTL = x + (s - 1 - y);
                    var dTR = (s - 1 - x) + (s - 1 - y);
                    var dBL = x + y;
                    var dBR = (s - 1 - x) + y;
                    var inside = dTL >= cut && dTR >= cut && dBL >= cut && dBR >= cut;
                    var isEdge = inside && (x <= 1 || x >= s - 2 || y <= 1 || y >= s - 2 ||
                                            dTL <= cut + 1 || dTR <= cut + 1 || dBL <= cut + 1 || dBR <= cut + 1);
                    tex.SetPixel(x, y, isEdge ? Color.white : Palette.Transparent);
                }
            }
            tex.Apply();
            var border = new Vector4(cut + 3, cut + 3, cut + 3, cut + 3);
            _tacticalBorder = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
            _tacticalBorder.name = "UI_TacticalBorder";
            return _tacticalBorder;
        }

        public static Sprite GetRadarGridRingSprite()
        {
            if (_radarGridRing != null) return _radarGridRing;
            const int s = 128;
            var tex = CreateBlankTexture("UI_RadarGridRingTex", s);
            const float center = (s - 1) * 0.5f;
            for (var y = 0; y < s; y++)
            {
                for (var x = 0; x < s; x++)
                {
                    var dx = x - center;
                    var dy = y - center;
                    var r = Mathf.Sqrt(dx * dx + dy * dy);
                    var outer = Mathf.Abs(r - 59f) <= 1.1f;
                    var inner = Mathf.Abs(r - 30f) <= 0.9f && ((x + y) % 5 != 0);
                    var cross = (Mathf.Abs(dx) <= 0.7f || Mathf.Abs(dy) <= 0.7f) && r <= 59f && ((int)r % 6 < 3);
                    if (outer || inner || cross)
                    {
                        var a = outer ? 0.52f : (inner ? 0.34f : 0.16f);
                        tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                    }
                }
            }
            tex.Apply();
            _radarGridRing = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 100f);
            _radarGridRing.name = "UI_RadarGridRing";
            return _radarGridRing;
        }

        public static Sprite GetRadarConeSprite()
        {
            if (_radarCone != null) return _radarCone;
            const int s = 128;
            var tex = CreateBlankTexture("UI_RadarConeTex", s);
            const float center = (s - 1) * 0.5f;
            for (var y = 0; y < s; y++)
            {
                for (var x = 0; x < s; x++)
                {
                    var dx = x - center;
                    var dy = y - center;
                    if (dy <= 0f) continue;
                    var r = Mathf.Sqrt(dx * dx + dy * dy);
                    if (r is < 4f or > 58f) continue;
                    var angDeg = Mathf.Abs(Mathf.Atan2(dx, dy) * Mathf.Rad2Deg);
                    if (angDeg > 32f) continue;
                    var radialFade = Mathf.Clamp01(1f - r / 58f) * 0.42f;
                    var edgeFade = Mathf.Clamp01((32f - angDeg) / 5f);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, radialFade * edgeFade));
                }
            }
            tex.Apply();
            _radarCone = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 100f);
            _radarCone.name = "UI_RadarCone";
            return _radarCone;
        }

        public static Sprite GetHitArcSprite()
        {
            if (_hitArc != null) return _hitArc;
            const int s = 128;
            var tex = CreateBlankTexture("UI_HitArcTex", s);
            const float center = (s - 1) * 0.5f;
            for (var y = 0; y < s; y++)
            {
                for (var x = 0; x < s; x++)
                {
                    var dx = x - center;
                    var dy = y - center;
                    if (dy <= 0f) continue;
                    var r = Mathf.Sqrt(dx * dx + dy * dy);
                    if (r is < 44f or > 58f) continue;
                    var angDeg = Mathf.Abs(Mathf.Atan2(dx, dy) * Mathf.Rad2Deg);
                    if (angDeg > 36f) continue;
                    var band = Mathf.Clamp01(1f - Mathf.Abs(r - 51f) / 7f);
                    var sweep = Mathf.Clamp01((36f - angDeg) / 14f);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, band * sweep));
                }
            }
            tex.Apply();
            _hitArc = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 100f);
            _hitArc.name = "UI_HitArc";
            return _hitArc;
        }

        public static Sprite GetSkullKiaIcon()
        {
            if (_skullKiaIcon != null) return _skullKiaIcon;
            const int s = 32;
            var tex = CreateBlankTexture("UI_SkullKiaTex", s);
            for (var y = 0; y < s; y++)
            {
                for (var x = 0; x < s; x++)
                {
                    var nx = (x - 15.5f) / 12f;
                    var ny = (y - 15.5f) / 12f;
                    var cranium = (nx * nx + (ny - 0.15f) * (ny - 0.15f)) <= 0.68f;
                    var jaw = Mathf.Abs(nx) <= 0.44f && ny is >= -0.78f and <= 0.05f;
                    if (!cranium && !jaw) continue;
                    var leftEye = ((nx + 0.30f) * (nx + 0.30f) + (ny - 0.06f) * (ny - 0.06f)) <= 0.045f;
                    var rightEye = ((nx - 0.30f) * (nx - 0.30f) + (ny - 0.06f) * (ny - 0.06f)) <= 0.045f;
                    var teethGap = ny < -0.45f && (x % 3 == 0);
                    if (!leftEye && !rightEye && !teethGap)
                    {
                        tex.SetPixel(x, y, Color.white);
                    }
                }
            }
            tex.Apply();
            _skullKiaIcon = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 100f);
            _skullKiaIcon.name = "UI_SkullKiaIcon";
            return _skullKiaIcon;
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

        public static Sprite GetSpeakerIcon()
        {
            return RenderSdfSprite(ref _speakerIcon, "UI_SpeakerIconTex", 64, p =>
            {
                var body = SdBox(p - new Vector2(-0.46f, 0f), new Vector2(0.16f, 0.21f));
                var coneA = SdTriangle(p, new Vector2(-0.34f, 0.21f), new Vector2(0.04f, 0.44f), new Vector2(0.04f, -0.44f));
                var coneB = SdTriangle(p, new Vector2(-0.34f, 0.21f), new Vector2(0.04f, -0.44f), new Vector2(-0.34f, -0.21f));
                var speaker = Mathf.Min(body, Mathf.Min(coneA, coneB));
                var w1 = SdArc(p - new Vector2(-0.04f, 0f), 0.34f, 0.052f, 0.72f);
                var w2 = SdArc(p - new Vector2(-0.04f, 0f), 0.56f, 0.052f, 0.68f);
                return Mathf.Min(speaker, Mathf.Min(w1, w2));
            });
        }

        public static Sprite GetOrdnanceIcon()
        {
            return RenderSdfSprite(ref _ordnanceIcon, "UI_OrdnanceIconTex", 64, p =>
            {
                var r = Rotate(p, -0.58f);
                var c1 = SdCartridge(r - new Vector2(-0.22f, 0f));
                var c2 = SdCartridge(r - new Vector2(0.22f, 0f));
                return Mathf.Min(c1, c2);
            });
        }

        public static Sprite GetSpeedIcon()
        {
            return RenderSdfSprite(ref _speedIcon, "UI_SpeedIconTex", 64, p =>
            {
                var d = 10f;
                for (var i = -1; i <= 1; i++)
                {
                    var ox = i * 0.34f;
                    var a = SdSegment(p, new Vector2(-0.22f + ox, 0.42f), new Vector2(0.14f + ox, 0f)) - 0.068f;
                    var b = SdSegment(p, new Vector2(-0.22f + ox, -0.42f), new Vector2(0.14f + ox, 0f)) - 0.068f;
                    d = Mathf.Min(d, Mathf.Min(a, b));
                }
                return d;
            });
        }

        public static Sprite GetCreditsIcon()
        {
            return RenderSdfSprite(ref _creditsIcon, "UI_CreditsIconTex", 64, p =>
            {
                var outer = SdHexagon(p, 0.64f);
                var innerCut = SdHexagon(p, 0.46f);
                var ring = Mathf.Max(outer, -innerCut);
                var coreBar = SdBox(p, new Vector2(0.11f, 0.30f));
                var crossBar = SdBox(p, new Vector2(0.26f, 0.09f));
                return Mathf.Min(ring, Mathf.Min(coreBar, crossBar));
            });
        }

        public static Sprite GetFragIcon()
        {
            return RenderSdfSprite(ref _fragIcon, "UI_FragIconTex", 64, p =>
            {
                var body = SdBox(p - new Vector2(0f, -0.14f), new Vector2(0.32f, 0.40f)) - 0.12f;
                var neck = SdBox(p - new Vector2(0f, 0.38f), new Vector2(0.12f, 0.14f));
                var spoon = SdSegment(p, new Vector2(0f, 0.50f), new Vector2(0.30f, 0.40f)) - 0.048f;
                var spoonTail = SdSegment(p, new Vector2(0.30f, 0.40f), new Vector2(0.44f, -0.18f)) - 0.042f;
                var pinRing = Mathf.Abs((p - new Vector2(-0.28f, 0.46f)).magnitude - 0.13f) - 0.042f;
                var pinCut = (p - new Vector2(-0.28f, 0.46f)).magnitude - 0.08f;
                var shell = Mathf.Min(Mathf.Min(body, neck), Mathf.Min(spoon, Mathf.Min(spoonTail, pinRing)));
                var hGroove = Mathf.Abs(Mathf.Abs(p.y + 0.14f) - 0.15f) - 0.028f;
                var vGroove = Mathf.Abs(p.x) - 0.028f;
                var grooves = Mathf.Max(body + 0.06f, Mathf.Min(hGroove, vGroove));
                return Mathf.Max(shell, -Mathf.Min(grooves, pinCut));
            });
        }

        public static Sprite GetArtilleryIcon()
        {
            return RenderSdfSprite(ref _artilleryIcon, "UI_ArtilleryIconTex", 64, p =>
            {
                var ring = Mathf.Abs(p.magnitude - 0.50f) - 0.055f;
                var dot = p.magnitude - 0.14f;
                var n = SdSegment(p, new Vector2(0f, 0.34f), new Vector2(0f, 0.72f)) - 0.048f;
                var s = SdSegment(p, new Vector2(0f, -0.34f), new Vector2(0f, -0.72f)) - 0.048f;
                var e = SdSegment(p, new Vector2(0.34f, 0f), new Vector2(0.72f, 0f)) - 0.048f;
                var w = SdSegment(p, new Vector2(-0.34f, 0f), new Vector2(-0.72f, 0f)) - 0.048f;
                return Mathf.Min(Mathf.Min(ring, dot), Mathf.Min(Mathf.Min(n, s), Mathf.Min(e, w)));
            });
        }

        public static Sprite GetMedalIcon()
        {
            return RenderSdfSprite(ref _medalIcon, "UI_MedalIconTex", 64, p =>
            {
                var star = SdStar5(p - new Vector2(0f, 0.08f), 0.56f, 0.24f);
                var leftWing = SdSegment(p, new Vector2(-0.56f, -0.36f), new Vector2(0f, -0.66f)) - 0.055f;
                var rightWing = SdSegment(p, new Vector2(0.56f, -0.36f), new Vector2(0f, -0.66f)) - 0.055f;
                return Mathf.Min(star, Mathf.Min(leftWing, rightWing));
            });
        }

        public static Sprite GetIntelIcon()
        {
            return RenderSdfSprite(ref _intelIcon, "UI_IntelIconTex", 64, p =>
            {
                var doc = SdBox(p, new Vector2(0.50f, 0.60f)) - 0.04f;
                var cornerClip = ((p.x + p.y) - 0.78f) * 0.707f;
                var frame = Mathf.Max(doc, cornerClip);
                var innerCut = SdBox(p, new Vector2(0.37f, 0.47f));
                var border = Mathf.Max(frame, -Mathf.Max(innerCut, cornerClip - 0.14f));
                var l1 = SdSegment(p, new Vector2(-0.24f, 0.24f), new Vector2(0.14f, 0.24f)) - 0.045f;
                var l2 = SdSegment(p, new Vector2(-0.24f, 0.0f), new Vector2(0.24f, 0.0f)) - 0.045f;
                var l3 = SdSegment(p, new Vector2(-0.24f, -0.24f), new Vector2(0.18f, -0.24f)) - 0.045f;
                return Mathf.Min(border, Mathf.Min(l1, Mathf.Min(l2, l3)));
            });
        }

        public static Sprite GetBroadcastIcon()
        {
            return RenderSdfSprite(ref _broadcastIcon, "UI_BroadcastIconTex", 64, p =>
            {
                var mast = SdSegment(p, new Vector2(0f, -0.62f), new Vector2(0f, 0.28f)) - 0.055f;
                var legL = SdSegment(p, new Vector2(-0.34f, -0.62f), new Vector2(0f, -0.02f)) - 0.048f;
                var legR = SdSegment(p, new Vector2(0.34f, -0.62f), new Vector2(0f, -0.02f)) - 0.048f;
                var tip = (p - new Vector2(0f, 0.34f)).magnitude - 0.11f;
                var center = new Vector2(0f, 0.34f);
                var q = p - center;
                var absX = new Vector2(Mathf.Abs(q.x), q.y);
                var w1 = SdArc(absX, 0.32f, 0.048f, 0.85f);
                var w2 = SdArc(absX, 0.54f, 0.048f, 0.85f);
                return Mathf.Min(Mathf.Min(Mathf.Min(mast, tip), Mathf.Min(legL, legR)), Mathf.Min(w1, w2));
            });
        }

        public static Sprite GetLockIcon()
        {
            return RenderSdfSprite(ref _lockIcon, "UI_LockIconTex", 64, p =>
            {
                var body = SdBox(p - new Vector2(0f, -0.22f), new Vector2(0.46f, 0.38f)) - 0.05f;
                var shackleOuter = SdCapsuleU(p - new Vector2(0f, 0.24f), 0.28f, 0.26f);
                var shackleInner = SdCapsuleU(p - new Vector2(0f, 0.22f), 0.16f, 0.22f);
                var shackle = Mathf.Max(shackleOuter, -shackleInner);
                var keyHole = (p - new Vector2(0f, -0.15f)).magnitude - 0.11f;
                var keySlot = SdBox(p - new Vector2(0f, -0.30f), new Vector2(0.055f, 0.15f));
                return Mathf.Max(Mathf.Min(body, shackle), -Mathf.Min(keyHole, keySlot));
            });
        }

        public static Sprite GetPlayIcon()
        {
            return RenderSdfSprite(ref _playIcon, "UI_PlayIconTex", 64, p =>
                SdTriangle(p, new Vector2(-0.36f, -0.50f), new Vector2(-0.36f, 0.50f), new Vector2(0.52f, 0f)) - 0.03f);
        }

        public static Sprite GetRestartIcon()
        {
            return RenderSdfSprite(ref _restartIcon, "UI_RestartIconTex", 64, p =>
            {
                var ang = Mathf.Atan2(p.y, p.x);
                var ring = Mathf.Abs(p.magnitude - 0.46f) - 0.085f;
                var openGap = (ang > 0.25f && ang < 1.15f) ? 0.5f : -1f;
                var arc = Mathf.Max(ring, openGap);
                var arrow = SdTriangle(p, new Vector2(0.08f, 0.64f), new Vector2(0.24f, 0.20f), new Vector2(0.56f, 0.48f)) - 0.02f;
                return Mathf.Min(arc, arrow);
            });
        }

        public static Sprite GetHomeIcon()
        {
            return RenderSdfSprite(ref _homeIcon, "UI_HomeIconTex", 64, p =>
            {
                var roofL = SdSegment(p, new Vector2(-0.60f, -0.02f), new Vector2(0f, 0.54f)) - 0.068f;
                var roofR = SdSegment(p, new Vector2(0.60f, -0.02f), new Vector2(0f, 0.54f)) - 0.068f;
                var walls = SdBox(p - new Vector2(0f, -0.26f), new Vector2(0.40f, 0.28f)) - 0.03f;
                var door = SdBox(p - new Vector2(0f, -0.38f), new Vector2(0.14f, 0.20f));
                return Mathf.Min(Mathf.Min(roofL, roofR), Mathf.Max(walls, -door));
            });
        }

        public static Sprite GetOperatorClassIcon(int roleIndex)
        {
            var idx = Mathf.Clamp(roleIndex, 0, OperatorClassIcons.Length - 1);
            return RenderSdfSprite(ref OperatorClassIcons[idx], "UI_OpRoleIcon_" + idx, 64, p =>
            {
                return idx switch
                {
                    0 => Mathf.Min(
                        SdChevronUp(p - new Vector2(0f, -0.20f), 0.52f),
                        Mathf.Min(SdChevronUp(p - new Vector2(0f, 0.04f), 0.44f), SdStar5(p - new Vector2(0f, 0.44f), 0.24f, 0.10f))),
                    1 => Mathf.Min(
                        SdBox(p - new Vector2(-0.24f, 0f), new Vector2(0.13f, 0.52f)) - 0.02f,
                        Mathf.Min(SdBox(p - new Vector2(0.24f, 0f), new Vector2(0.13f, 0.52f)) - 0.02f,
                            SdBox(p, new Vector2(0.42f, 0.11f)))),
                    2 => Mathf.Min(
                        Mathf.Abs(p.magnitude - 0.48f) - 0.06f,
                        Mathf.Min((p.magnitude - 0.14f),
                            Mathf.Min(SdSegment(p, new Vector2(-0.68f, 0f), new Vector2(0.68f, 0f)) - 0.04f,
                                SdSegment(p, new Vector2(0f, -0.68f), new Vector2(0f, 0.68f)) - 0.04f))),
                    _ => Mathf.Min(
                        SdSegment(p, new Vector2(-0.46f, -0.46f), new Vector2(0.46f, 0.46f)) - 0.075f,
                        Mathf.Min(SdSegment(p, new Vector2(-0.46f, 0.46f), new Vector2(0.46f, -0.46f)) - 0.075f,
                            SdHexagon(p, 0.24f)))
                };
            });
        }

        public static Sprite GetSectorTypeIcon(int actIndex)
        {
            var idx = Mathf.Clamp(actIndex, 0, SectorTypeIcons.Length - 1);
            return RenderSdfSprite(ref SectorTypeIcons[idx], "UI_SectorIcon_" + idx, 64, p =>
            {
                return idx switch
                {
                    0 => Mathf.Min(
                        SdSegment(p, new Vector2(0f, -0.44f), new Vector2(0f, 0.44f)) - 0.055f,
                        Mathf.Min(SdSegment(p, new Vector2(-0.28f, 0.22f), new Vector2(0.28f, 0.22f)) - 0.05f,
                            Mathf.Min(SdSegment(p, new Vector2(-0.48f, -0.20f), new Vector2(0f, -0.52f)) - 0.055f,
                                Mathf.Min(SdSegment(p, new Vector2(0.48f, -0.20f), new Vector2(0f, -0.52f)) - 0.055f,
                                    Mathf.Abs((p - new Vector2(0f, 0.48f)).magnitude - 0.14f) - 0.04f)))),
                    1 => Mathf.Min(
                        SdSegment(p, new Vector2(-0.34f, -0.56f), new Vector2(-0.18f, 0.56f)) - 0.055f,
                        Mathf.Min(SdSegment(p, new Vector2(0.34f, -0.56f), new Vector2(0.18f, 0.56f)) - 0.055f,
                            Mathf.Min(SdBox(p - new Vector2(0f, 0.28f), new Vector2(0.36f, 0.055f)),
                                Mathf.Min(SdBox(p, new Vector2(0.42f, 0.055f)), SdBox(p - new Vector2(0f, -0.32f), new Vector2(0.48f, 0.055f)))))),
                    2 => Mathf.Min(
                        SdSegment(p, new Vector2(-0.42f, -0.54f), new Vector2(0f, 0.04f)) - 0.052f,
                        Mathf.Min(SdSegment(p, new Vector2(0.42f, -0.54f), new Vector2(0f, 0.04f)) - 0.052f,
                            Mathf.Min(SdArc(new Vector2(Mathf.Abs(p.x), p.y - 0.22f), 0.34f, 0.05f, 0.85f),
                                Mathf.Min(SdArc(new Vector2(Mathf.Abs(p.x), p.y - 0.22f), 0.56f, 0.05f, 0.85f), (p - new Vector2(0f, 0.22f)).magnitude - 0.11f)))),
                    3 => Mathf.Min(
                        SdTriangle(p, new Vector2(-0.62f, -0.46f), new Vector2(-0.12f, 0.46f), new Vector2(0.32f, -0.46f)) - 0.02f,
                        SdTriangle(p, new Vector2(-0.08f, -0.46f), new Vector2(0.32f, 0.30f), new Vector2(0.64f, -0.46f)) - 0.02f),
                    _ => Mathf.Min(
                        Mathf.Max(SdHexagon(p, 0.62f), -SdHexagon(p, 0.45f)),
                        SdHexagon(p, 0.24f))
                };
            });
        }

        private static Sprite RenderSdfSprite(ref Sprite cache, string texName, int size, Func<Vector2, float> signedDist)
        {
            if (cache != null) return cache;
            var tex = CreateBlankTexture(texName, size);
            var pixels = new Color[size * size];
            var scale = size * 0.48f;
            for (var y = 0; y < size; y++)
            {
                var ny = ((y + 0.5f) / size) * 2f - 1f;
                for (var x = 0; x < size; x++)
                {
                    var nx = ((x + 0.5f) / size) * 2f - 1f;
                    var d = signedDist(new Vector2(nx, ny));
                    var a = Mathf.Clamp01(0.5f - d * scale);
                    pixels[y * size + x] = a > 0.01f ? new Color(1f, 1f, 1f, a) : Palette.Transparent;
                }
            }
            tex.SetPixels(pixels);
            tex.Apply(false, false);
            cache = Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
            return cache;
        }

        private static Vector2 Rotate(Vector2 p, float rad)
        {
            var c = Mathf.Cos(rad);
            var s = Mathf.Sin(rad);
            return new Vector2(p.x * c - p.y * s, p.x * s + p.y * c);
        }

        private static float SdBox(Vector2 p, Vector2 b)
        {
            var q = new Vector2(Mathf.Abs(p.x), Mathf.Abs(p.y)) - b;
            return new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f);
        }

        private static float SdSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var pa = p - a;
            var ba = b - a;
            var h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Mathf.Max(0.0001f, Vector2.Dot(ba, ba)));
            return (pa - ba * h).magnitude;
        }

        private static float SdTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            var e0 = b - a; var e1 = c - b; var e2 = a - c;
            var v0 = p - a; var v1 = p - b; var v2 = p - c;
            var pq0 = v0 - e0 * Mathf.Clamp01(Vector2.Dot(v0, e0) / Vector2.Dot(e0, e0));
            var pq1 = v1 - e1 * Mathf.Clamp01(Vector2.Dot(v1, e1) / Vector2.Dot(e1, e1));
            var pq2 = v2 - e2 * Mathf.Clamp01(Vector2.Dot(v2, e2) / Vector2.Dot(e2, e2));
            var s = Mathf.Sign(e0.x * e2.y - e0.y * e2.x);
            var d = Mathf.Min(
                Mathf.Min(Vector2.Dot(pq0, pq0), Vector2.Dot(pq1, pq1)),
                Vector2.Dot(pq2, pq2));
            var w = Mathf.Min(
                Mathf.Min(s * (v0.x * e0.y - v0.y * e0.x), s * (v1.x * e1.y - v1.y * e1.x)),
                s * (v2.x * e2.y - v2.y * e2.x));
            return -Mathf.Sqrt(d) * Mathf.Sign(w);
        }

        private static float SdHexagon(Vector2 p, float r)
        {
            var k = new Vector3(-0.8660254f, 0.5f, 0.5773503f);
            p = new Vector2(Mathf.Abs(p.x), Mathf.Abs(p.y));
            p -= 2f * Mathf.Min(Vector2.Dot(new Vector2(k.x, k.y), p), 0f) * new Vector2(k.x, k.y);
            p -= new Vector2(Mathf.Clamp(p.x, -k.z * r, k.z * r), r);
            return p.magnitude * Mathf.Sign(p.y);
        }

        private static float SdArc(Vector2 p, float r, float thickness, float sinAperture)
        {
            var ring = Mathf.Abs(p.magnitude - r) - thickness;
            if (p.x < 0f) return Mathf.Max(ring, -p.x);
            var sinAng = Mathf.Abs(p.y) / Mathf.Max(0.001f, p.magnitude);
            return Mathf.Max(ring, sinAng - sinAperture);
        }

        private static float SdCapsuleU(Vector2 p, float width, float height)
        {
            if (p.y < 0f)
            {
                var l = SdSegment(p, new Vector2(-width, 0f), new Vector2(-width, -height));
                var r = SdSegment(p, new Vector2(width, 0f), new Vector2(width, -height));
                return Mathf.Min(l, r);
            }
            return p.magnitude - width;
        }

        private static float SdCartridge(Vector2 p)
        {
            var casing = SdBox(p - new Vector2(0f, -0.14f), new Vector2(0.11f, 0.34f)) - 0.02f;
            var tip = SdTriangle(p, new Vector2(-0.11f, 0.18f), new Vector2(0.11f, 0.18f), new Vector2(0f, 0.56f)) - 0.01f;
            return Mathf.Min(casing, tip);
        }

        private static float SdChevronUp(Vector2 p, float span)
        {
            var l = SdSegment(p, new Vector2(-span, -0.14f), new Vector2(0f, 0.22f)) - 0.055f;
            var r = SdSegment(p, new Vector2(span, -0.14f), new Vector2(0f, 0.22f)) - 0.055f;
            return Mathf.Min(l, r);
        }

        private static float SdStar5(Vector2 p, float rOuter, float rInner)
        {
            var k1 = new Vector2(0.80901699f, -0.58778525f);
            var k2 = new Vector2(-0.80901699f, -0.58778525f);
            p.x = Mathf.Abs(p.x);
            p -= 2f * Mathf.Max(Vector2.Dot(k1, p), 0f) * k1;
            p -= 2f * Mathf.Max(Vector2.Dot(k2, p), 0f) * k2;
            p.x = Mathf.Abs(p.x);
            p.y -= rOuter;
            var ba = new Vector2(rInner * 0.58778525f, rInner * 0.80901699f - rOuter);
            var h = Mathf.Clamp(Vector2.Dot(p, ba) / Vector2.Dot(ba, ba), 0f, 1f);
            return (p - ba * h).magnitude * Mathf.Sign(p.y * ba.x - p.x * ba.y);
        }

        public static Texture2D GetTacticalFloorTexture()
        {
            if (_tacticalFloorTex != null) return _tacticalFloorTex;
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                name = "Tex_TacticalFloorGrid",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Repeat
            };

            var pixels = new Color[size * size];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var modX = x % 64;
                    var modY = y % 64;
                    var isJoint = modX <= 1 || modY <= 1;
                    var isBevel = !isJoint && (modX == 2 || modY == 2);
                    var isCrossHair = (modX == 32 && (modY >= 29 && modY <= 35)) || (modY == 32 && (modX >= 29 && modX <= 35));
                    var hash = ((x * 73856093) ^ (y * 19349663) ^ ((x + y) * 83492791)) & 255;
                    var speckle = (hash / 255f - 0.5f) * 0.055f;

                    var shade = 0.94f + speckle;
                    if (isJoint) shade = 0.72f;
                    else if (isBevel) shade = 1.00f;
                    else if (isCrossHair) shade = 0.86f;

                    shade = Mathf.Clamp01(shade);
                    pixels[y * size + x] = new Color(shade, shade, shade, 1f);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply(true, false);
            _tacticalFloorTex = tex;
            return _tacticalFloorTex;
        }
    }
}
