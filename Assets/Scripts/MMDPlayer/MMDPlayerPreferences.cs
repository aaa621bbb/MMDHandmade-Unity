using UnityEngine;

namespace MMDPlayer
{
    /// <summary>
    /// Lightweight PlayerPrefs-backed settings for the MMD player, so a returning user (or a game
    /// relaunch) restores the last model, playback speed, loop setting and camera viewpoint. All values
    /// are optional and default to sensible values when never written.
    /// </summary>
    public static class MMDPlayerPreferences
    {
        private const string KeyModel = "MMDPlayer.LastModel";
        private const string KeySpeed = "MMDPlayer.Speed";
        private const string KeyLoop = "MMDPlayer.Loop";
        private const string KeyCamera = "MMDPlayer.CameraView";

        // --- last model (source .pmx project path) ---

        public static void SaveModelPath(string sourcePath)
        {
            PlayerPrefs.SetString(KeyModel, sourcePath ?? string.Empty);
            PlayerPrefs.Save();
        }

        public static string LoadModelPath()
        {
            return PlayerPrefs.GetString(KeyModel, string.Empty);
        }

        // --- playback speed ---

        public static void SaveSpeed(float speed)
        {
            PlayerPrefs.SetFloat(KeySpeed, speed);
            PlayerPrefs.Save();
        }

        public static float LoadSpeed(float fallback = 1f)
        {
            return PlayerPrefs.GetFloat(KeySpeed, fallback);
        }

        // --- loop ---

        public static void SaveLoop(bool loop)
        {
            PlayerPrefs.SetInt(KeyLoop, loop ? 1 : 0);
            PlayerPrefs.Save();
        }

        public static bool LoadLoop(bool fallback = false)
        {
            return PlayerPrefs.GetInt(KeyLoop, fallback ? 1 : 0) == 1;
        }

        // --- camera view preset ---

        public static void SaveCameraView(int view)
        {
            PlayerPrefs.SetInt(KeyCamera, view);
            PlayerPrefs.Save();
        }

        public static int LoadCameraView(int fallback = 0)
        {
            return PlayerPrefs.GetInt(KeyCamera, fallback);
        }
    }
}
