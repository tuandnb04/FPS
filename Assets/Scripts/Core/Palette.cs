using UnityEngine;

namespace TopDownShooter.Core
{
    /// <summary>
    /// Single source of truth for all colors and palette-derived materials in VANGUARD // NEON ARENA.
    /// Every material, light, fog, particle, and UI color reads exclusively from here.
    /// </summary>
    public static class Palette
    {
        // Core Art Bible Palette
        public static readonly Color Floor = FromHex("#2A313E");              // Deep Tactical Slate
        public static readonly Color Walls = FromHex("#384152");              // Reinforced Steel
        public static readonly Color CharacterPrimary = FromHex("#2AD4E8");   // Electric Tactical Cyan
        public static readonly Color CharacterSecondary = FromHex("#E8EEF5"); // Ceramic White
        public static readonly Color EnemyPrimary = FromHex("#E84855");       // Hostile Crimson-Coral
        public static readonly Color EnemySecondary = FromHex("#FF8A93");     // Highlight Coral
        public static readonly Color Props = FromHex("#526075");              // Industrial Cover Crate / Trim
        public static readonly Color Shadow = FromHex("#12161F");             // Abyss Navy-Slate
        public static readonly Color Accent = FromHex("#FFB830");             // Signal Amber
        public static readonly Color HealthGreen = FromHex("#3CE882");        // Vitality Mint

        // Lighting & Atmosphere
        public static readonly Color KeyLightWarm = FromHex("#FFEAD0");
        public static readonly Color AmbientSky = FromHex("#3A4F6B");
        public static readonly Color AmbientEquator = FromHex("#232832");
        public static readonly Color AmbientGround = FromHex("#181E2B");
        public static readonly Color SkyboxTop = FromHex("#18202E");
        public static readonly Color SkyboxHorizon = FromHex("#2B3548");

        // Derived UI & VFX tones
        public static readonly Color CyanGlow = FromHex("#4DF0FF");
        public static readonly Color MutedText = WithAlpha(CharacterSecondary, 0.72f);
        public static readonly Color ScrimDark = WithAlpha(Shadow, 0.80f);
        public static readonly Color PanelSurface = WithAlpha(FromHex("#1A202B"), 0.90f);
        public static readonly Color PanelBorder = WithAlpha(Props, 0.85f);
        public static readonly Color ButtonTintIdle = FromHex("#FFFFFF");
        public static readonly Color ButtonTintHover = FromHex("#FFF3D6");
        public static readonly Color SecondaryButtonBg = FromHex("#263042");
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
            if (mat.HasProperty("_SunSize")) mat.SetFloat("_SunSize", 0.02f);
            if (mat.HasProperty("_AtmosphereThickness")) mat.SetFloat("_AtmosphereThickness", 0.45f);
            if (mat.HasProperty("_SkyTint")) mat.SetColor("_SkyTint", SkyboxTop);
            if (mat.HasProperty("_GroundColor")) mat.SetColor("_GroundColor", Shadow);
            if (mat.HasProperty("_Exposure")) mat.SetFloat("_Exposure", 0.65f);
            return mat;
        }
    }
}
