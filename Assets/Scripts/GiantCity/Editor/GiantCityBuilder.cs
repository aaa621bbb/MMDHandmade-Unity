using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace GiantCity.Editor
{
    /// <summary>
    /// Builds the Phase 2 <c>GiantCity</c> scene via code (the same low-risk approach used in Phase 1).
    /// Creates the controller, the miniature world (city + tiny player), the giantess anchor, a follow
    /// camera, a light/ground, and a Canvas+HUD. The giantess is loaded at runtime from a phone folder; a
    /// placeholder is used until one is supplied.
    /// </summary>
    public static class GiantCityBuilder
    {
        public const string ScenePath = "Assets/Scenes/GiantCity.unity";

        [MenuItem("Tools/Giant City/Build GiantCity Scene")]
        public static void BuildScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // --- Directional Light ---
            GameObject lightGO = new GameObject("Directional Light", typeof(Light));
            lightGO.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            Light light = lightGO.GetComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1f;
            light.shadows = LightShadows.Soft;

            // --- Giant ground (native scale, so the giantess and the miniature world both rest on it) ---
            GameObject groundGO = GameObject.CreatePrimitive(PrimitiveType.Plane);
            groundGO.name = "Ground";
            groundGO.transform.localScale = new Vector3(2000f, 1f, 2000f);
            groundGO.transform.position = Vector3.zero;

            // --- Controller + world root ---
            GameObject controllerGO = new GameObject("GiantCityController");
            GiantCityController controller = controllerGO.AddComponent<GiantCityController>();

            GameObject worldRoot = new GameObject("MiniWorld");
            worldRoot.transform.SetParent(controllerGO.transform, false);
            controller.worldRoot = worldRoot.transform;

            GameObject giantAnchor = new GameObject("GiantAnchor");
            giantAnchor.transform.SetParent(controllerGO.transform, false);
            controller.giantAnchor = giantAnchor.transform;

            // City generator (under the scaled world root).
            CityGenerator city = worldRoot.AddComponent<CityGenerator>();
            controller.city = city;

            // Tiny player + character (under the scaled world root).
            GameObject tinyPlayerGO = new GameObject("TinyPlayer");
            tinyPlayerGO.transform.SetParent(worldRoot.transform, false);
            TinyPlayerController tinyPlayer = tinyPlayerGO.AddComponent<TinyPlayerController>();
            tinyPlayer.character = tinyPlayerGO.AddComponent<TinyPlayerCharacter>();
            controller.tinyPlayer = tinyPlayer;
            controller.tinyCharacter = tinyPlayer.character;

            // World scaler (on the controller; scales the world root).
            WorldScaler worldScaler = controllerGO.AddComponent<WorldScaler>();
            worldScaler.worldRoot = worldRoot.transform;
            controller.worldScaler = worldScaler;

            // Model picker.
            MobileModelPicker picker = controllerGO.AddComponent<MobileModelPicker>();
            controller.modelPicker = picker;

            // --- Main Camera (low, driven by TinyPlayerController) ---
            GameObject cameraGO = new GameObject("Main Camera");
            cameraGO.tag = "MainCamera";
            Camera cam = cameraGO.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.55f, 0.65f, 0.85f, 1f);
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 5000f;
            cam.fieldOfView = 60f;
            cameraGO.AddComponent<AudioListener>();

            // --- HUD Canvas ---
            GameObject canvasGO = new GameObject("GiantCanvas", typeof(Canvas));
            Canvas canvas = canvasGO.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGO.AddComponent<GiantCityHUD>();

            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[GiantCity] Built scene at " + ScenePath);
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
        }
    }
}
