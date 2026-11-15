using System;
using System.Collections;
using UnityEngine;

namespace TopDownShooter.Core
{
    /// <summary>
    /// Easing functions and unscaled-safe coroutine tween helper.
    /// </summary>
    public static class Ease
    {
        public static float Linear(float t) => Mathf.Clamp01(t);

        public static float OutCubic(float t)
        {
            t = Mathf.Clamp01(t);
            float inv = 1f - t;
            return 1f - inv * inv * inv;
        }

        public static float OutBack(float t)
        {
            t = Mathf.Clamp01(t);
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            float tm1 = t - 1f;
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
            Func<float, float> curve = ease ?? OutCubic;

            if (duration <= 0.0001f)
            {
                apply(curve(1f));
                yield break;
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                float dt = unscaled ? Time.unscaledDeltaTime : Time.deltaTime;
                elapsed += dt;
                float norm = Mathf.Clamp01(elapsed / duration);
                apply(curve(norm));
                yield return null;
            }

            apply(curve(1f));
        }
    }
}
