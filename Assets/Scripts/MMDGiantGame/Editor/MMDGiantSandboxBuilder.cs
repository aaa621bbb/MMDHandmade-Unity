using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MMDGiantGame.Editor
{
    /// <summary>
    /// Builds the Phase 2 "MMDGiantSandbox" scene via code (the same low-risk option chosen in Phase 1).
    /// Creates the giant game manager, the city builder, a follow camera, a light, and a Canvas+EventSystem
    /// with the giant HUD. The Phase 1 MMD player controller is optional: this scene wires a
    /// <see cref="MMDPlayer.MMDPlayerController"/> if one exists in the scene, otherwise the game manager
    /// falls back to a placeholder giant. Saving the scene never blocks editing the Phase 1 scene.
    /// </summary>
    public static class MMDGiantSandboxBuilder
    {
        public const string ScenePath = "Assets/Scenes/MMDGiantSandbox.unity";

        [MenuItem("Tools/MMD Giant Game/Build MMDGiantSandbox Scene")]
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
            light.shadows = LightShadows.None;

            // --- Ground plane ---
            GameObject groundGO = GameObject.CreatePrimitive(PrimitiveType.Plane);
            groundGO.name = "Ground";
            groundGO.transform.localScale = new Vector3(30f, 1f, 30f);
            groundGO.transform.position = Vector3.zero;

            // --- Giant city builder ---
            GameObject cityGO = new GameObject("City");
            MMDCityBuilder city = cityGO.AddComponent<MMDCityBuilder>();

            // --- Game manager ---
            GameObject managerGO = new GameObject("MMDGiantGameManager");
            MMDGiantGameManager manager = managerGO.AddComponent<MMDGiantGameManager>();
            manager.city = city;
            manager.giantScale = 18f;

            // --- Follow camera ---
            GameObject cameraGO = new GameObject("Main Camera");
            cameraGO.tag = "MainCamera";
            Camera cam = cameraGO.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.35f, 0.55f, 0.78f, 1f);
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 2000f;
            cam.fieldOfView = 60f;
            cameraGO.transform.position = new Vector3(0f, 52f, -96f);
            cameraGO.AddComponent<AudioListener>();

            MMDGiantCamera giantCam = cameraGO.AddComponent<MMDGiantCamera>();
            giantCam.distance = 96f;
            giantCam.height = 52f;
            manager.gameCamera = giantCam;

            // --- Canvas + HUD ---
            GameObject canvasGO = new GameObject("GiantCanvas", typeof(Canvas));
            Canvas canvas = canvasGO.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGO.AddComponent<MMDGiantHUD>();

            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

            // --- Optional Phase 1 controller (let the giant use a real MMD model if the user has one) ---
            MMDPlayer.MMDPlayerController controller = managerGO.GetComponent<MMDPlayer.MMDPlayerController>();
            if (controller == null)
            {
                controller = managerGO.AddComponent<MMDPlayer.MMDPlayerController>();
            }
            // Hook up the Phase 1 asset library if it has already been scanned.
            controller.library = AssetDatabase.LoadAssetAtPath<MMDPlayer.MMDAssetLibrary>(
                "Assets/MMDResources/Library.asset");
            controller.autoLoadLastModel = false; // the giant game manager drives model loading
            controller.optimizeForMobile = true;
            controller.livePhysics = false; // giant game prioritises frame rate over Bullet cloth
            manager.player = controller;
            manager.autoLoadGiantFromLibrary = true;

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[MMDGiantGame] Built MMDSandbox scene at " + ScenePath);
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
        }
    }
}
