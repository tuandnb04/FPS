using UnityEditor;
using UnityEngine;

namespace Editor
{
    /// <summary>
    /// Automatically optimizes Assets/Resources/VoiceRadio/*.wav clips for low memory footprint
    /// (Mono Vorbis CompressedInMemory, LoadInBackground, 24kHz preserved) without re-decoding 87 WAVs into RAM at boot.
    /// </summary>
    public sealed class VoiceRadioImporter : AssetPostprocessor
    {
        private const string VoiceFolderPrefix = "Assets/Resources/VoiceRadio/";

        private void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith(VoiceFolderPrefix)) return;
            if (assetImporter is not AudioImporter importer) return;
            ConfigureRadioVoiceImporter(importer);
        }

        [MenuItem("Vanguard/Optimize Radio Audio Imports")]
        public static void OptimizeAllRadioVoiceAssets()
        {
            var guids = AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/Resources/VoiceRadio" });
            var updated = 0;
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var t in guids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(t);
                    if (AssetImporter.GetAtPath(path) is not AudioImporter importer) continue;
                    if (!ConfigureRadioVoiceImporter(importer)) continue;
                    importer.SaveAndReimport();
                    updated++;
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            Debug.Log($"[VoiceRadioImporter] Optimized {updated}/{guids.Length} radio voice clips (CompressedInMemory Mono Vorbis).");
        }

        private static bool ConfigureRadioVoiceImporter(AudioImporter importer)
        {
            var settings = importer.defaultSampleSettings;
            var dirty = false;

            if (!importer.forceToMono)
            {
                importer.forceToMono = true;
                dirty = true;
            }
            if (!importer.loadInBackground)
            {
                importer.loadInBackground = true;
                dirty = true;
            }
            if (settings.preloadAudioData)
            {
                settings.preloadAudioData = false;
                dirty = true;
            }
            if (settings.loadType != AudioClipLoadType.CompressedInMemory)
            {
                settings.loadType = AudioClipLoadType.CompressedInMemory;
                dirty = true;
            }
            if (settings.compressionFormat != AudioCompressionFormat.Vorbis)
            {
                settings.compressionFormat = AudioCompressionFormat.Vorbis;
                dirty = true;
            }
            if (!Mathf.Approximately(settings.quality, 0.72f))
            {
                settings.quality = 0.72f;
                dirty = true;
            }
            if (settings.sampleRateSetting != AudioSampleRateSetting.PreserveSampleRate)
            {
                settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
                dirty = true;
            }

            if (dirty)
            {
                importer.defaultSampleSettings = settings;
            }
            return dirty;
        }
    }
}
