using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Core
{
    /// <summary>
    /// Easing functions, unscaled-safe coroutine tween helper, and zero-alloc quantized Wait cache.
    /// </summary>
    public static class Ease
    {
        private static readonly Dictionary<int, WaitForSeconds> ScaledWaits = new(24);
        private static readonly Dictionary<int, WaitForSecondsRealtime> RealtimeWaits = new(16);

        public static WaitForSeconds GetWait(float seconds)
        {
            var key = Mathf.Max(1, Mathf.RoundToInt(seconds * 100f));
            if (!ScaledWaits.TryGetValue(key, out var wait))
            {
                wait = new WaitForSeconds(key * 0.01f);
                ScaledWaits[key] = wait;
            }
            return wait;
        }

        public static WaitForSecondsRealtime GetWaitRealtime(float seconds)
        {
            var key = Mathf.Max(1, Mathf.RoundToInt(seconds * 100f));
            if (!RealtimeWaits.TryGetValue(key, out var wait))
            {
                wait = new WaitForSecondsRealtime(key * 0.01f);
                RealtimeWaits[key] = wait;
            }
            return wait;
        }

        public static float Linear(float t) => Mathf.Clamp01(t);

        public static float OutCubic(float t)
        {
            t = Mathf.Clamp01(t);
            var inv = 1f - t;
            return 1f - inv * inv * inv;
        }

        public static float InOutCubic(float t)
        {
            t = Mathf.Clamp01(t);
            return t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) * 0.5f;
        }

        public static float OutBack(float t)
        {
            t = Mathf.Clamp01(t);
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            var tm1 = t - 1f;
            return 1f + c3 * tm1 * tm1 * tm1 + c1 * tm1 * tm1;
        }

        public static float OutElastic(float t)
        {
            t = Mathf.Clamp01(t);
            if (t <= 0f) return 0f;
            if (t >= 1f) return 1f;
            const float c4 = (2f * Mathf.PI) / 3f;
            return Mathf.Pow(2f, -10f * t) * Mathf.Sin((t * 10f - 0.75f) * c4) + 1f;
        }

        /// <summary>
        /// Tweens 0..1 over duration seconds. Runs on unscaled time by default so UI/shell
        /// transitions keep animating when Time.timeScale == 0.
        /// </summary>
        public static IEnumerator Tween(
            float duration,
            Action<float> apply,
            Func<float, float> ease = null,
            bool unscaled = true)
        {
            if (apply == null) yield break;
            var curve = ease ?? OutCubic;

            if (duration <= 0.0001f)
            {
                apply(curve(1f));
                yield break;
            }

            var elapsed = 0f;
            while (elapsed < duration)
            {
                var dt = unscaled ? Time.unscaledDeltaTime : Time.deltaTime;
                elapsed += dt;
                var norm = Mathf.Clamp01(elapsed / duration);
                apply(curve(norm));
                yield return null;
            }

            apply(curve(1f));
        }
    }
}
