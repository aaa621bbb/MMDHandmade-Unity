using UnityEngine;

namespace MMDPlayer
{
    /// <summary>
    /// Mobile-friendly runtime tuning for the MMD player. Kept deliberately conservative and idempotent:
    ///
    ///  - Caps <c>Application.targetFrameRate</c> so the device does not try to render at its maximum rate
    ///    (which drains the battery on phones) while keeping playback smooth.
    ///  - Prevents the screen from sleeping while the model is being viewed.
    ///  - On Android/iOS lowers shadow quality and MSAA so the toon-shaded model stays at a good frame rate.
    ///
    /// Turn this off (via <c>MMDPlayerController.optimizeForMobile</c>) in a game that manages its own
    /// quality settings.
    /// </summary>
    public static class MMDMobilePerformance
    {
        /// <summary>
        /// Applies the mobile runtime optimisations. Safe to call repeatedly (e.g. on every scene load).
        /// </summary>
        public static void Apply(int targetFrameRate = 60)
        {
            if (targetFrameRate > 0)
            {
                Application.targetFrameRate = targetFrameRate;
            }

            // Keep the screen awake so the viewer / gameplay never dims mid-scene.
            Screen.sleepTimeout = SleepTimeout.NeverSleep;

#if UNITY_ANDROID || UNITY_IOS
            // Lower the cost of a toon model on a phone. These are deliberately conservative and only
            // applied once per session on mobile platforms.
            QualitySettings.shadows = ShadowQuality.Disable;
            QualitySettings.shadowResolution = ShadowResolution.Low;
            QualitySettings.antiAliasing = Mathf.Min(QualitySettings.antiAliasing, 2);
            QualitySettings.vSyncCount = 0;
#endif
        }
    }
}
