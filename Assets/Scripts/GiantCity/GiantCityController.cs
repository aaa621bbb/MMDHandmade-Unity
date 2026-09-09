using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UMT;

namespace GiantCity
{
    /// <summary>
    /// Central orchestrator for the Phase 2 giantess-city sandbox. It:
    ///
    ///  - Builds the miniature world (city + tiny player) and scales the *world*, never the giantess, so MMD
    ///    Bullet physics stays stable (see <see cref="WorldScaler"/>).
    ///  - Loads the giantess from a user-supplied .pmx at runtime (bytes + sibling textures), and lets the
    ///    player swap her at will. Falls back to a placeholder when no model has been supplied, so the game
    ///    is playable immediately.
    ///  - Wires the <see cref="GiantessBrain"/> (AI) and <see cref="StompDetector"/> (hit), applies the
    ///    invincible toggle, and resolves stomp hits to safe respawns / visual-only feedback.
    ///
    /// The tiny player is a procedural capsule that runs between buildings while the giantess chases.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GiantCityController : MonoBehaviour
    {
        [Header("World")]
        [Tooltip("Root that holds the miniature city + tiny player. Its scale is set by WorldScaler.")]
        public Transform worldRoot;
        [Tooltip("Root the giantess model is instantiated under (kept at native scale).")]
        public Transform giantAnchor;
        [Tooltip("Scales the miniature world to match the giantess (do NOT scale the giantess).")]
        public WorldScaler worldScaler;

        [Header("Components")]
        public CityGenerator city;
        public TinyPlayerController tinyPlayer;
        public TinyPlayerCharacter tinyCharacter;
        public GiantessBrain giantessBrain;
        public StompDetector stompDetector;
        public GiantessAnatomy giantessAnatomy;
        public MobileModelPicker modelPicker;
        public GiantCityHUD hud;

        [Header("Rules")]
        [Tooltip("When true, a stomp is visual-only (no hit / no respawn). Sandbox default is off.")]
        public bool invincible = false;
        [Tooltip("Automatically load the last-used giantess on start, if one was saved.")]
        public bool autoLoadLastModel = true;
        [Tooltip("Enable live Bullet physics on the giantess (Android arm64 supports it).")]
        public bool livePhysics = true;

        private PMXImportResult _giantResult;
        private MMDTransformManager _tm;
        private MMDPhysicsManager _physics;
        private GameObject _placeholderGiant;
        private Transform _leftFootAnchor;
        private Transform _rightFootAnchor;
        private bool _loading;

        /// <summary>True while a giantess model is being imported.</summary>
        public bool IsLoading => _loading;
        /// <summary>True when a real (non-placeholder) giantess is loaded.</summary>
        public bool HasRealGiantess => _giantResult != null && _giantResult.root != null;

        /// <summary>Current invincible state; setting it also updates the HUD.</summary>
        public bool Invincible
        {
            get => invincible;
            set { invincible = value; if (hud != null) hud.SetInvincible(value); }
        }

        private async void Start()
        {
            EnsureHierarchy();

            if (city != null)
            {
                city.Build();
            }

            if (modelPicker != null)
            {
                modelPicker.RefreshFromDefaultFolder();
            }

            if (autoLoadLastModel && modelPicker != null)
            {
                GiantessModelOption last = modelPicker.LoadLastModel();
                if (last != null)
                {
                    await LoadGiantessAsync(last);
                    return;
                }
            }

            // No saved model: make a placeholder so the game is immediately playable.
            SetStatus("未找到可用女巨人。点「换模型」从手机文件夹导入 .pmx。");
            InstallPlaceholderGiant();
        }

        // ---------------------------------------------------------------------
        //  Hierarchy bootstrapping
        // ---------------------------------------------------------------------

        private void EnsureHierarchy()
        {
            if (giantAnchor == null)
            {
                GameObject anchor = new GameObject("GiantAnchor");
                anchor.transform.SetParent(transform, false);
                giantAnchor = anchor.transform;
            }

            if (worldRoot == null)
            {
                GameObject root = new GameObject("MiniWorld");
                root.transform.SetParent(transform, false);
                worldRoot = root.transform;
            }

            if (city == null)
            {
                city = FindObjectOfType<CityGenerator>();
            }
            if (city == null)
            {
                city = worldRoot.gameObject.AddComponent<CityGenerator>();
            }

            if (tinyPlayer == null)
            {
                tinyPlayer = FindObjectOfType<TinyPlayerController>();
            }
            if (tinyPlayer == null)
            {
                tinyPlayer = worldRoot.gameObject.AddComponent<TinyPlayerController>();
                tinyPlayer.transform.SetParent(worldRoot, false);
            }

            if (tinyCharacter == null)
            {
                tinyCharacter = FindObjectOfType<TinyPlayerCharacter>();
            }
            if (tinyCharacter == null && tinyPlayer != null)
            {
                tinyCharacter = tinyPlayer.GetComponentInChildren<TinyPlayerCharacter>();
            }

            if (worldScaler == null)
            {
                worldScaler = FindObjectOfType<WorldScaler>();
            }
            if (worldScaler == null)
            {
                worldScaler = gameObject.AddComponent<WorldScaler>();
                worldScaler.worldRoot = worldRoot;
            }

            if (modelPicker == null)
            {
                modelPicker = FindObjectOfType<MobileModelPicker>();
            }
            if (modelPicker == null)
            {
                modelPicker = gameObject.AddComponent<MobileModelPicker>();
            }

            if (hud == null)
            {
                hud = FindObjectOfType<GiantCityHUD>();
            }
        }

        private void SetStatus(string message)
        {
            if (hud != null)
            {
                hud.SetStatus(message);
            }
        }

        // ---------------------------------------------------------------------
        //  Giantess loading / swapping
        // ---------------------------------------------------------------------

        /// <summary>Prompts the user to pick a model from the device and loads it, replacing the current one.</summary>
        public async void PickGiantessFromDevice()
        {
            if (_loading)
            {
                return;
            }

            if (modelPicker == null)
            {
                SetStatus("模型选择器不可用。");
                return;
            }

            GiantessModelOption option = modelPicker.PickModel();
            if (option == null)
            {
                SetStatus("已取消选择模型。");
                return;
            }

            SetStatus("正在导入 " + option.displayName + " …");
            await LoadGiantessAsync(option);
        }

        /// <summary>Imports the given model and swaps it in as the giantess. Fails back to the placeholder.</summary>
        public async Task<bool> LoadGiantessAsync(GiantessModelOption option)
        {
            if (_loading)
            {
                return false;
            }

            if (modelPicker == null)
            {
                return false;
            }

            _loading = true;
            try
            {
                PMXImportResult result = await modelPicker.ImportAsync(option, giantAnchor);
                if (result == null || result.root == null)
                {
                    SetStatus("导入失败，已保留原模型。");
                    return false;
                }

                SwapToRealGiantess(result);
                SetStatus("女巨人已载入：" + option.displayName);
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                SetStatus("导入异常：" + e.Message);
                return false;
            }
            finally
            {
                _loading = false;
            }
        }

        private void SwapToRealGiantess(PMXImportResult result)
        {
            UnloadCurrentGiant();

            _giantResult = result;
            _tm = result.mmdTransformResult != null ? result.mmdTransformResult.transformManager : null;
            _physics = result.mmdTransformResult != null ? result.mmdTransformResult.physicsManager : null;

            ApplyGiantPhysics();

            // Locate the feet/head so the stomp detector and AI can use them.
            FindFeetAndHead();

            // AI.
            if (giantessBrain == null)
            {
                giantessBrain = result.root.GetComponent<GiantessBrain>() ?? result.root.AddComponent<GiantessBrain>();
            }
            giantessBrain.Configure(result.root.transform, tinyCharacter != null ? tinyCharacter.transform : null);

            // Stomp trigger.
            if (stompDetector == null)
            {
                stompDetector = result.root.GetComponent<StompDetector>() ?? result.root.AddComponent<StompDetector>();
            }
            Transform player = tinyCharacter != null ? tinyCharacter.transform : null;
            stompDetector.Configure(_leftFootAnchor, _rightFootAnchor, player, OnStompHit);

            // Match the world to the giantess (scale the world, never the giantess).
            ApplyWorldScale(ComputeGiantBounds(result.root));
        }

        private void ApplyGiantPhysics()
        {
            if (_tm == null)
            {
                return;
            }

            _tm.transformEnabled = true;
            _tm.doSDEFSkinning = true;

            // Only enable Bullet physics where the native plugin exists (Android arm64 is supported).
            bool available = MMDPlayer.MMDPhysicsGuard.IsPhysicsAvailable();
            _tm.livePhysics = available && livePhysics;

            if (_physics != null)
            {
                _physics.enableGroundCollision = true;
            }

            if (available && livePhysics)
            {
                try
                {
                    _tm.ResetPhysics();
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning("[GiantCity] Physics reset failed; disabling live physics. " + e.Message);
                    _tm.livePhysics = false;
                }
            }
        }

        private void FindFeetAndHead()
        {
            _leftFootAnchor = null;
            _rightFootAnchor = null;

            if (giantessAnatomy != null)
            {
                giantessAnatomy.Rescan(_tm);
                _leftFootAnchor = giantessAnatomy.LeftFoot;
                _rightFootAnchor = giantessAnatomy.RightFoot;
            }

            if (_leftFootAnchor == null || _rightFootAnchor == null)
            {
                // Fallback: pick the two lowest bones (in world Y) as the feet.
                if (_tm != null && _tm.bones != null)
                {
                    PickFeetFromBones(_tm.bones);
                }
            }
        }

        private void PickFeetFromBones(MMDBoneTransform[] bones)
        {
            float minY = float.MaxValue;
            float secondMinY = float.MaxValue;
            MMDBoneTransform lowest = null;
            MMDBoneTransform secondLowest = null;

            foreach (MMDBoneTransform bone in bones)
            {
                if (bone == null)
                {
                    continue;
                }
                float y = bone.transform.position.y;
                if (y < minY)
                {
                    secondMinY = minY;
                    secondLowest = lowest;
                    minY = y;
                    lowest = bone;
                }
                else if (y < secondMinY)
                {
                    secondMinY = y;
                    secondLowest = bone;
                }
            }

            if (_leftFootAnchor == null && lowest != null)
            {
                _leftFootAnchor = lowest.transform;
            }
            if (_rightFootAnchor == null && secondLowest != null)
            {
                _rightFootAnchor = secondLowest.transform;
            }
        }

        private void ApplyWorldScale(Bounds giantBounds)
        {
            if (worldScaler == null)
            {
                return;
            }
            float scale = worldScaler.Apply(giantBounds);
            if (tinyPlayer != null)
            {
                tinyPlayer.ApplyWorldScale(scale);
                tinyPlayer.SnapCamera();
            }
        }

        private static Bounds ComputeGiantBounds(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0)
            {
                return new Bounds(root.transform.position, Vector3.one * 2f);
            }

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; ++i)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }
            return bounds;
        }

        private void UnloadCurrentGiant()
        {
            if (_tm != null)
            {
                try
                {
                    _tm.DisposeRuntimeData();
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning("[GiantCity] DisposeRuntimeData failed. " + e.Message);
                }
            }

            if (_giantResult != null && _giantResult.root != null)
            {
                Destroy(_giantResult.root);
                _giantResult = null;
            }

            RemovePlaceholderGiant();

            _tm = null;
            _physics = null;
            _leftFootAnchor = null;
            _rightFootAnchor = null;
        }

        // ---------------------------------------------------------------------
        //  Placeholder giant (no model supplied)
        // ---------------------------------------------------------------------

        private void InstallPlaceholderGiant()
        {
            if (giantAnchor == null)
            {
                EnsureHierarchy();
            }

            RemovePlaceholderGiant();

            GameObject placeholder = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            placeholder.name = "PlaceholderGiant";
            placeholder.transform.SetParent(giantAnchor, false);
            placeholder.transform.localPosition = new Vector3(0f, 400f, 0f);
            placeholder.transform.localScale = new Vector3(900f, 1200f, 900f);

            Renderer r = placeholder.GetComponent<Renderer>();
            if (r != null)
            {
                Shader shader = Shader.Find("Standard");
                if (shader != null)
                {
                    Material mat = new Material(shader);
                    mat.color = new Color(0.7f, 0.35f, 0.45f, 1f);
                    r.material = mat;
                }
            }
            // Ground the capsule: shift so its base is at y=0.
            placeholder.transform.localPosition = new Vector3(0f, 600f, 0f);

            // Foot/head anchors.
            GameObject footL = new GameObject("FootL");
            footL.transform.SetParent(placeholder.transform, false);
            footL.transform.localPosition = new Vector3(200f, -580f, 200f);
            _leftFootAnchor = footL.transform;

            GameObject footR = new GameObject("FootR");
            footR.transform.SetParent(placeholder.transform, false);
            footR.transform.localPosition = new Vector3(-200f, -580f, 200f);
            _rightFootAnchor = footR.transform;

            _placeholderGiant = placeholder;

            // AI + stomp for the placeholder so the mechanic is fully playable.
            if (giantessBrain == null)
            {
                giantessBrain = placeholder.GetComponent<GiantessBrain>() ?? placeholder.AddComponent<GiantessBrain>();
            }
            giantessBrain.Configure(placeholder.transform, tinyCharacter != null ? tinyCharacter.transform : null);

            if (stompDetector == null)
            {
                stompDetector = placeholder.GetComponent<StompDetector>() ?? placeholder.AddComponent<StompDetector>();
            }
            Transform player = tinyCharacter != null ? tinyCharacter.transform : null;
            stompDetector.Configure(_leftFootAnchor, _rightFootAnchor, player, OnStompHit);

            Bounds bounds = ComputeGiantBounds(placeholder);
            ApplyWorldScale(bounds);
        }

        private void RemovePlaceholderGiant()
        {
            if (_placeholderGiant != null)
            {
                Destroy(_placeholderGiant);
                _placeholderGiant = null;
            }
        }

        // ---------------------------------------------------------------------
        //  Stomp hit resolution
        // ---------------------------------------------------------------------

        private void OnStompHit()
        {
            // Visual/dust feedback always happens; the consequence depends on invincible mode.
            if (invincible)
            {
                SetStatus("女巨人踩到了你，但你在无敌模式，毫发无损！");
                return;
            }

            RespawnPlayer();
            SetStatus("你被踩倒了！已重生。");
        }

        /// <summary>Respawns the tiny player near the world centre, away from the giantess's feet.</summary>
        public void RespawnPlayer()
        {
            if (tinyPlayer == null)
            {
                return;
            }

            tinyPlayer.transform.localPosition = new Vector3(0f, GiantCityConfig.TinySpawnY, 0f);
            tinyPlayer.SnapCamera();

            if (tinyCharacter != null)
            {
                tinyCharacter.HitFeedback(RespawnKnockDirection());
            }
        }

        private Vector3 RespawnKnockDirection()
        {
            // Push the player away from the giantess if it is standing somewhere.
            if (giantAnchor == null)
            {
                return Vector3.zero;
            }
            Vector3 away = tinyPlayer.transform.position - giantAnchor.position;
            away.y = 0f;
            return away.sqrMagnitude > 0.0001f ? away : Vector3.zero;
        }

        /// <summary>Regenerates the city and resets the player to the centre.</summary>
        public void RegenerateCity()
        {
            if (city != null)
            {
                city.Build();
            }
            RespawnPlayer();
        }
    }
}
