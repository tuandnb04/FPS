using UnityEngine;

namespace TopDownShooter.Core
{
    /// <summary>
    /// Single source of truth for all colors and palette-derived materials in TASK FORCE // ZERO.
    /// Grounded modern military tactical palette (no sci-fi neon emissions).
    /// </summary>
    public static class Palette
    {
        // Core Modern Military Art Palette
        public static readonly Color Floor = FromHex("#52554D");              // Weathered Combat Asphalt
        public static readonly Color Walls = FromHex("#6E6B61");              // Reinforced Concrete Perimeter Wall
        public static readonly Color CharacterPrimary = FromHex("#4E603D");   // Ranger Olive Drab Combat Suit
        public static readonly Color CharacterSecondary = FromHex("#EBEAE2"); // Bone-White HUD Text & Chalk Stencil
        public static readonly Color EnemyPrimary = FromHex("#6E332D");       // Hostile Mercenary Russet-Brown
        public static readonly Color EnemySecondary = FromHex("#E05A47");     // Hostile Tracer / Breacher Red
        public static readonly Color Props = FromHex("#7A7569");              // Concrete Jersey Barrier / Crate
        public static readonly Color Shadow = FromHex("#161A1D");             // Matte Gunmetal Charcoal
        public static readonly Color Accent = FromHex("#E5A024");             // Tactical Field Amber
        public static readonly Color HealthGreen = FromHex("#49C96E");        // Medkit Trauma Green

        // Extended Grounded Military Surfaces & Camos
        public static readonly Color CoyoteTan = FromHex("#A6926F");          // Coyote Tan Vest & BDUs
        public static readonly Color Gunmetal = FromHex("#2C3135");           // Parkerized Steel Weapon / Frame
        public static readonly Color GuardTowerDark = FromHex("#3A3D38");     // Painted Structural Steel
        public static readonly Color HazardYellow = FromHex("#CFA236");       // Matte Warning Stencil Yellow
        public static readonly Color NvgGreen = FromHex("#2B5C38");           // Grounded Optical NVG Lens Green

        // Daylight Tactical Compound Lighting & Atmosphere
        public static readonly Color KeyLightWarm = FromHex("#FFE8C6");       // Warm Afternoon Sun
        public static readonly Color AmbientSky = FromHex("#6F8396");         // Dusty Overcast Daylight Sky
        public static readonly Color AmbientEquator = FromHex("#555852");     // Concrete Courtyard Bounce
        public static readonly Color AmbientGround = FromHex("#3A3B35");      // Tarmac Bounce
        public static readonly Color SkyboxTop = FromHex("#516880");          // Clear Tactical Blue-Slate
        public static readonly Color SkyboxHorizon = FromHex("#B4A792");      // Warm Dusty Horizon Haze

        // Derived UI & Ballistic VFX tones
        public static readonly Color CyanGlow = FromHex("#C49438");           // Brass-Amber Trim (replaces legacy cyan)
        public static readonly Color MutedText = WithAlpha(CharacterSecondary, 0.72f);
        public static readonly Color ScrimDark = WithAlpha(Shadow, 0.80f);
        public static readonly Color PanelSurface = WithAlpha(FromHex("#15191C"), 0.93f);
        public static readonly Color PanelBorder = WithAlpha(Props, 0.88f);
        public static readonly Color ButtonTintIdle = FromHex("#FFFFFF");
        public static readonly Color ButtonTintHover = FromHex("#FFF0CF");
        public static readonly Color SecondaryButtonBg = FromHex("#272D2B");
        public static readonly Color CreditGold = FromHex("#E3B448");
        public static readonly Color BossPurple = FromHex("#D6453A");         // Artillery Danger Red (replaces sci-fi purple)
        public static readonly Color RailgunCyan = FromHex("#FFE18A");        // Supersonic AP Tracer Brass
        public static readonly Color RocketOrange = FromHex("#E86A28");       // RPG Exhaust Flame Orange
        public static readonly Color SkinGoldPrimary = FromHex("#B59867");    // Desert Sandstorm Camo
        public static readonly Color SkinCrimsonPrimary = FromHex("#2B3034"); // Night Ops Spec-Ops Black
        public static readonly Color CardRareBorder = FromHex("#E5A024");
        public static readonly Color Transparent = new Color(0f, 0f, 0f, 0f);

        public static Color FromHex(string hex)
        {
            if (ColorUtility.TryParseHtmlString(hex, out Color parsed))
            {
                return parsed;
            }
            return new Color(0.14f, 0.16f, 0.20f, 1f);
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
            float metallic = 0.1f,
            float smoothness = 0.35f,
            bool emissive = false,
            float emissionIntensity = 2.0f,
            Texture2D mainTex = null)
        {
            return CreateLitMaterial(matName, baseColor, metallic, smoothness, emissive, baseColor, emissionIntensity, mainTex);
        }

        public static Material CreateLitMaterial(
            string matName,
            Color baseColor,
            float metallic,
            float smoothness,
            bool emissive,
            Color emissionColor,
            float emissionIntensity,
            Texture2D mainTex = null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                Debug.LogError($"[Palette] Could not find 'Universal Render Pipeline/Lit' shader for {matName}");
                return null;
            }

            Material mat = new Material(shader) { name = matName };
            mat.SetColor("_BaseColor", baseColor);
            mat.SetFloat("_Smoothness", smoothness);
            mat.SetFloat("_Metallic", metallic);

            if (mainTex != null)
            {
                mat.mainTexture = mainTex;
                if (mat.HasProperty("_BaseMap"))
                {
                    mat.SetTexture("_BaseMap", mainTex);
                }
            }

            if (emissive)
            {
                mat.EnableKeyword("_EMISSION");
                Color hdrColor = new Color(
                    emissionColor.r * emissionIntensity,
                    emissionColor.g * emissionIntensity,
                    emissionColor.b * emissionIntensity,
                    1f);
                mat.SetColor("_EmissionColor", hdrColor);
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }

            return mat;
        }

        public static Material CreateParticleMaterial(Texture2D mainTex)
        {
            return CreateParticleMaterial("Mat_Particle", mainTex);
        }

        public static Material CreateParticleMaterial(string matName, Texture2D mainTex = null)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                Debug.LogError($"[Palette] Could not find 'Sprites/Default' shader for {matName}");
                return null;
            }

            Material mat = new Material(shader) { name = matName };
            mat.color = CharacterSecondary;
            if (mainTex != null)
            {
                mat.mainTexture = mainTex;
            }
            return mat;
        }

        public static Material CreateSkyboxMaterial(string matName = "Mat_ArenaSkybox")
        {
            Shader shader = Shader.Find("Skybox/Procedural");
            if (shader == null)
            {
                Debug.LogError($"[Palette] Could not find 'Skybox/Procedural' shader for {matName}");
                return null;
            }

            Material mat = new Material(shader) { name = matName };
            if (mat.HasProperty("_SunSize")) mat.SetFloat("_SunSize", 0.035f);
            if (mat.HasProperty("_AtmosphereThickness")) mat.SetFloat("_AtmosphereThickness", 0.72f);
            if (mat.HasProperty("_SkyTint")) mat.SetColor("_SkyTint", SkyboxTop);
            if (mat.HasProperty("_GroundColor")) mat.SetColor("_GroundColor", AmbientGround);
            if (mat.HasProperty("_Exposure")) mat.SetFloat("_Exposure", 0.92f);
            return mat;
        }
    }
}
