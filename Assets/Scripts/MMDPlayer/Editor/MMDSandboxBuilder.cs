using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MMDPlayer.Editor
{
    /// <summary>
    /// Builds the <c>MMDSandbox</c> playback scene via code (the recommended, lowest-risk approach for
    /// authoring a .unity file without running Unity). Creates the controller, model anchor, camera,
    /// light and a Canvas+EventSystem with the playback UI, then saves the scene asset.
    /// </summary>
    public static class MMDSandboxBuilder
    {
        public const string ScenePath = "Assets/Scenes/MMDSandbox.unity";

        [MenuItem("Tools/MMD Player/Build MMDSandbox Scene")]
        public static void BuildScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // --- Directional Light ---
            GameObject lightGO = new GameObject("Directional Light", typeof(Light));
            lightGO.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            Light light = lightGO.GetComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1f;
            light.color = new Color(1f, 0.95686275f, 0.8392f, 1f);
            light.shadows = LightShadows.Soft;

            // --- MMD Player Controller + Model Anchor ---
            GameObject controllerGO = new GameObject("MMDPlayerController", typeof(MMDPlayerController));
            MMDPlayerController controller = controllerGO.GetComponent<MMDPlayerController>();

            GameObject anchorGO = new GameObject("ModelAnchor");
            anchorGO.transform.SetParent(controllerGO.transform, false);
            controller.modelAnchor = anchorGO.transform;

            // Load the library asset if it exists.
            controller.library = AssetDatabase.LoadAssetAtPath<MMDAssetLibrary>(MMDResourcesScanner.DefaultLibraryPath);

            // --- Main Camera with orbit controls ---
            GameObject cameraGO = new GameObject("Main Camera");
            cameraGO.tag = "MainCamera";
            Camera camera = cameraGO.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.192f, 0.302f, 0.475f, 0f);
            camera.fieldOfView = 60f;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 1000f;
            cameraGO.transform.position = new Vector3(0f, 1f, -5f);
            cameraGO.AddComponent<AudioListener>();
            MMDOrbitCamera orbit = cameraGO.AddComponent<MMDOrbitCamera>();
            orbit.distance = 4f;

            // --- Canvas (with playback UI) ---
            GameObject canvasGO = new GameObject("PlaybackCanvas", typeof(Canvas));
            Canvas canvas = canvasGO.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

            // MMDPlaybackUI builds its own children at runtime; we only need the component present.
            canvasGO.AddComponent<MMDPlaybackUI>();

            // --- Ground (optional; makes the physics ground visually obvious if enabled) ---
            GameObject groundGO = GameObject.CreatePrimitive(PrimitiveType.Plane);
            groundGO.name = "Ground";
            groundGO.transform.localScale = new Vector3(10f, 1f, 10f);
            groundGO.transform.position = Vector3.zero;

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[MMD Player] Built MMDSandbox scene at " + ScenePath);
            Selection.activeObject = assetObject(ScenePath);
        }

        private static Object assetObject(string scenePath)
        {
            return AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath);
        }
    }
}
