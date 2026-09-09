using UnityEngine;

namespace MMDPlayer
{
    /// <summary>
    /// Determines whether UMT's native Bullet physics is available on the current platform.
    ///
    /// UMT ships its native plugin for Windows (x64), Android (arm64-v8a) and WebGL only.
    /// macOS and Linux editors have no physics solver — that is expected from the plugin
    /// layout, not a bug. This guard lets the rest of the player degrade gracefully instead
    /// of attempting a native call that would throw.
    /// </summary>
    public static class MMDPhysicsGuard
    {
        /// <summary>Message shown in the UI when physics is unavailable.</summary>
        public const string UnsupportedMessage =
            "当前平台无 Bullet 原生库（仅 Windows/Android/Web 可用），物理已关闭。";

        /// <summary>
        /// Returns true only when live Bullet physics can actually run on this platform.
        /// A null-safe, exception-free check that never touches the native plugin.
        /// </summary>
        public static bool IsPhysicsAvailable()
        {
#if UNITY_EDITOR
            // Inside the editor the native Bullet library only loads on Windows (x64).
            // An Android/WebGL build target still runs inside a host editor process whose
            // plugin ABI is the editor's own, so on macOS/Linux editors physics is off.
            return Application.platform == RuntimePlatform.WindowsEditor;
#else
            switch (Application.platform)
            {
                case RuntimePlatform.WindowsPlayer:
                case RuntimePlatform.Android:
                case RuntimePlatform.WebGLPlayer:
                    return true;
                default:
                    return false;
            }
#endif
        }
    }
}
