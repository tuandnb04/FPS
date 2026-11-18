using UnityEngine;

namespace Core
{
    /// <summary>
    /// Single source of truth for all colors and palette-derived materials in PROTOCOL ZERO // VANGUARD-09.
    /// Grounded modern military tactical palette across all three campaign operations.
    /// </summary>
    public static class Palette
    {
        // Core Modern Military Art Palette (Mission 01: Blackwater Pier // Coastal Dusk)
        public static readonly Color Floor = FromHex("#3A3F3C");              // Damp Combat Asphalt
        public static readonly Color Walls = FromHex("#555852");              // Reinforced Concrete Perimeter Wall
        public static readonly Color CharacterPrimary = FromHex("#4E603D");   // Ranger Olive Drab Combat Suit
        public static readonly Color CharacterSecondary = FromHex("#EBEAE2"); // Bone-White HUD Text & Chalk Stencil
        public static readonly Color EnemyPrimary = FromHex("#6E332D");       // Hostile Ironward Russet-Brown
        public static readonly Color EnemySecondary = FromHex("#E05A47");     // Hostile Tracer / Breacher Red
        public static readonly Color Props = FromHex("#6B685F");              // Weathered Concrete Barrier / Crate
        public static readonly Color Shadow = FromHex("#12161A");             // Matte Night-Ops Charcoal
        public static readonly Color Accent = FromHex("#E5A024");             // Tactical Field Amber
        public static readonly Color HealthGreen = FromHex("#49C96E");        // Medkit Trauma Green

        // Extended Grounded Military Surfaces & Camos
        public static readonly Color CoyoteTan = FromHex("#A6926F");          // Coyote Tan Vest & BDUs
        public static readonly Color Gunmetal = FromHex("#2C3135");           // Parkerized Steel Weapon / Frame
        public static readonly Color GuardTowerDark = FromHex("#3A3D38");     // Painted Structural Steel
        public static readonly Color HazardYellow = FromHex("#CFA236");       // Matte Warning Stencil Yellow
        public static readonly Color NvgGreen = FromHex("#2B5C38");           // Grounded Optical NVG Lens Green
        public static readonly Color SkullMaskBone = FromHex("#DEDAD0");      // Wraith Ballistic Half-Mask Ivory
        public static readonly Color NvgPhosphor = FromHex("#68FF9B");        // GPNVG-18 Phosphor Night Vision Tint
        public static readonly Color SodiumAmber = FromHex("#FFB048");        // Warm Sodium Floodlight Lamp
        public static readonly Color BarrelHazardRed = FromHex("#B83126");    // Explosive Fuel Barrel Drum Red
        public static readonly Color HitmarkerWhite = FromHex("#F6F5F0");     // Ballistic Hitmarker White
        public static readonly Color HitmarkerCrimson = FromHex("#FF3B30");   // Fatal Kill Hitmarker Red
        public static readonly Color DmzRidgeEarth = FromHex("#3A4738");      // Blackcrest Alpine Ridge Earth
        public static readonly Color RoadAsphalt = FromHex("#2E3337");        // Textureless Matte Tarmac Convoy Road

        // Multi-Mission Environment & Weather Palettes
        public static readonly Color M1PierWater = FromHex("#1B3140");        // Mission 01 Coastal Harbor Water
        public static readonly Color M1ConcreteApron = FromHex("#434A50");    // Mission 01 Wet Dockyard Concrete
        public static readonly Color M2CanyonEarth = FromHex("#664835");      // Mission 02 Iron Corridor Canyon Earth
        public static readonly Color M2RailSteel = FromHex("#383C42");        // Mission 02 Switchyard Rail & Tie Steel
        public static readonly Color M2DepotSkyTop = FromHex("#2B2330");      // Mission 02 Pre-Dawn Amber-Slate Sky
        public static readonly Color M2DepotSkyHorizon = FromHex("#8F5E3C");  // Mission 02 Pre-Dawn Canyon Haze
        public static readonly Color M2KeyLightAmber = FromHex("#FFD19A");    // Mission 02 Low-Angle Dawn Sunlight
        public static readonly Color M3PeakSnow = FromHex("#CBD6E0");         // Mission 03 Blackcrest Frost / Snow Cap
        public static readonly Color M3CitadelStone = FromHex("#3B4248");     // Mission 03 Citadel Basalt Concrete
        public static readonly Color M3HeliosCoreCyan = FromHex("#4CC9E8");   // Mission 03 Project HELIOS Capacitor Arc

        // Night-Ops & Moonlight Lighting & Atmosphere
        public static readonly Color KeyLightWarm = FromHex("#C4D7F5");       // Crisp Moonlight Blue-Silver Key
        public static readonly Color AmbientSky = FromHex("#4A5D74");         // Night-Ops Mountain Sky Fill
        public static readonly Color AmbientEquator = FromHex("#333C44");     // Wet Concrete Courtyard Bounce
        public static readonly Color AmbientGround = FromHex("#1F2422");      // Dark Tarmac Bounce
        public static readonly Color SkyboxTop = FromHex("#142131");          // Deep Coastal Night Sky
        public static readonly Color SkyboxHorizon = FromHex("#394B5A");      // Low Valley Mist Horizon

        // Derived UI & Ballistic VFX tones
        public static readonly Color CyanGlow = FromHex("#C49438");           // Brass-Amber Trim
        public static readonly Color MutedText = WithAlpha(CharacterSecondary, 0.72f);
        public static readonly Color ScrimDark = WithAlpha(Shadow, 0.80f);
        public static readonly Color PanelSurface = WithAlpha(FromHex("#15191C"), 0.93f);
        public static readonly Color ButtonTintIdle = FromHex("#FFFFFF");
        public static readonly Color ButtonTintHover = FromHex("#FFF0CF");
        public static readonly Color SecondaryButtonBg = FromHex("#272D2B");
        public static readonly Color CreditGold = FromHex("#E3B448");
        public static readonly Color BossPurple = FromHex("#D6453A");         // Artillery Danger Red
        public static readonly Color RailgunCyan = FromHex("#FFE18A");        // Supersonic AP Tracer Brass
        public static readonly Color RocketOrange = FromHex("#E86A28");       // RPG Exhaust Flame Orange
        public static readonly Color SkinGoldPrimary = FromHex("#B59867");    // Desert Sandstorm Camo
        public static readonly Color SkinCrimsonPrimary = FromHex("#2B3034"); // Night Ops Spec-Ops Black
        public static readonly Color CardRareBorder = FromHex("#E5A024");
        public static readonly Color SquadBlue = FromHex("#42A5F5");         // Vanguard-09 Friendly Squad Chevron Blue
        public static readonly Color DamageBloodRed = FromHex("#A31419");    // Critical Damage Vignette
        public static readonly Color C4Amber = FromHex("#F2A72E");           // C4 Sabotage Objective Amber
        public static readonly Color RadarGreen = FromHex("#45CC73");        // Extraction LZ & Sweep Green
        public static readonly Color Transparent = new(0f, 0f, 0f, 0f);
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int Smoothness = Shader.PropertyToID("_Smoothness");
        private static readonly int Metallic = Shader.PropertyToID("_Metallic");
        private static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        private static readonly int SunSize = Shader.PropertyToID("_SunSize");
        private static readonly int AtmosphereThickness = Shader.PropertyToID("_AtmosphereThickness");
        private static readonly int SkyTint = Shader.PropertyToID("_SkyTint");
        private static readonly int GroundColor = Shader.PropertyToID("_GroundColor");
        private static readonly int Exposure = Shader.PropertyToID("_Exposure");

        public static Color FromHex(string hex)
        {
            return ColorUtility.TryParseHtmlString(hex, out var parsed) ? parsed : new Color(0.14f, 0.16f, 0.20f, 1f);
        }

        public static Color WithAlpha(Color c, float alpha)
        {
            return new Color(c.r, c.g, c.b, Mathf.Clamp01(alpha));
        }

        public static Color Lerp(Color a, Color b, float t)
        {
            return Color.Lerp(a, b, t);
        }

        public static Material CreateLitMaterial(
            string matName,
            Color baseColor,
            float metallic = 0.05f,
            float smoothness = 0.28f,
            bool emissive = false,
            float emissionIntensity = 2.0f,
            Texture2D mainTex = null)
        {
            return CreateLitMaterial(matName, baseColor, metallic, smoothness, emissive, baseColor, emissionIntensity, mainTex);
        }

        private static Material CreateLitMaterial(
            string matName,
            Color baseColor,
            float metallic,
            float smoothness,
            bool emissive,
            Color emissionColor,
            float emissionIntensity,
            Texture2D mainTex = null)
        {
            var shader = Shader.Find("Vanguard/StylizedLit") ?? Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                Debug.LogError($"[Palette] Could not find 'Vanguard/StylizedLit' or URP/Lit shader for {matName}");
                return null;
            }

            var mat = new Material(shader)
            {
                name = matName,
                enableInstancing = true
            };
            mat.SetColor(BaseColor, baseColor);
            mat.SetFloat(Smoothness, smoothness);
            mat.SetFloat(Metallic, metallic);

            if (mainTex != null && mat.HasProperty(BaseMap))
            {
                mat.mainTexture = mainTex;
                mat.SetTexture(BaseMap, mainTex);
            }

            if (!emissive) return mat;
            mat.EnableKeyword("_EMISSION");
            var hdrColor = new Color(
                emissionColor.r * emissionIntensity,
                emissionColor.g * emissionIntensity,
                emissionColor.b * emissionIntensity,
                1f);
            mat.SetColor(EmissionColor, hdrColor);
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;

            return mat;
        }

        public static Material CreateParticleMaterial(Texture2D mainTex)
        {
            return CreateParticleMaterial("Mat_Particle", mainTex);
        }

        public static Material CreateParticleMaterial(string matName, Texture2D mainTex = null)
        {
            var shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                Debug.LogError($"[Palette] Could not find 'Sprites/Default' shader for {matName}");
                return null;
            }

            var mat = new Material(shader)
            {
                name = matName,
                color = CharacterSecondary
            };
            if (mainTex != null)
            {
                mat.mainTexture = mainTex;
            }
            return mat;
        }

        public static Material CreateSkyboxMaterial(string matName = "Mat_ArenaSkybox")
        {
            var shader = Shader.Find("Skybox/Procedural");
            if (shader == null)
            {
                Debug.LogError($"[Palette] Could not find 'Skybox/Procedural' shader for {matName}");
                return null;
            }

            var mat = new Material(shader) { name = matName };
            if (mat.HasProperty(SunSize)) mat.SetFloat(SunSize, 0.035f);
            if (mat.HasProperty(AtmosphereThickness)) mat.SetFloat(AtmosphereThickness, 0.72f);
            if (mat.HasProperty(SkyTint)) mat.SetColor(SkyTint, SkyboxTop);
            if (mat.HasProperty(GroundColor)) mat.SetColor(GroundColor, AmbientGround);
            if (mat.HasProperty(Exposure)) mat.SetFloat(Exposure, 0.56f);
            return mat;
        }
    }
}
