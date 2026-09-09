using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using MMDPlayer;

namespace MMDGiantGame
{
    /// <summary>
    /// Core orchestrator for the Phase 2 "giantess city / stomp" game on Android. It reuses the Phase 1
    /// <see cref="MMDPlayerController"/> to drive the giantess model (so you can drop in any MMD model and
    /// it dances/animates as the giant), and layers a touch-driven stomp game on top:
    ///
    ///  - One-finger drag steers the giant across the ground plane (raycast to y=0).
    ///  - A tap (short, no drag) performs a stomp; the HUD "踩踏" button does the same.
    ///  - Walking near / on top of a prop breaks it; a stomp breaks everything in a radius.
    ///  - Buildings topple, people pop; each awards score.
    ///
    /// When no MMD model is available (no .pmx in the library yet) it spawns a placeholder capsule sized
    /// as a giant so the mechanic is fully testable without any asset, and it swaps in an MMD model
    /// automatically once the player provides one.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MMDGiantGameManager : MonoBehaviour
    {
        [Header("Phase 1 Integration")]
        [Tooltip("The Phase 1 MMD player controller that drives the giantess model (optional).")]
        public MMDPlayerController player;
        [Tooltip("When enabled, tries to load the last-used / first MMD model from the library as the giant.")]
        public bool autoLoadGiantFromLibrary = true;

        [Header("Giant")]
        [Tooltip("Where the giant stands and moves. If null, the manager creates one and a placeholder capsule.")]
        public Transform giantRoot;
        [Tooltip("Uniform scale applied to the giantess model / placeholder (MMD scale is ~1.7m, so e.g. 18 makes her tower).")]
        public float giantScale = 18f;
        [Tooltip("Face the direction the giant is walking (rotates the model via the Phase 1 controller).")]
        public bool faceMovement = true;
        [Tooltip("Move speed in world units per second.")]
        public float moveSpeed = 14f;

        [Header("Stomp")]
        [Tooltip("World radius around the giant that a stomp destroys.")]
        public float stompRadius = 9f;
        [Tooltip("World radius that walking over a prop auto-breaks it.")]
        public float walkBreakRadius = 4f;
        [Tooltip("Seconds between stomps (cooldown).")]
        public float stompCooldown = 0.55f;

        [Header("Scene Refs")]
        public MMDCityBuilder city;
        public MMDGiantHUD hud;
        public MMDGiantCamera gameCamera;

        private Camera _mainCamera;
        private GameObject _placeholderGiant;
        private float _stompTimer;
        private bool _stomping;
        private bool _dragging;
        private Vector2 _touchStart;

        /// <summary>Current score. Raised by <see cref="DoStomp"/> and walking breaks.</summary>
        public int Score { get; private set; }

        private void Awake()
        {
            _mainCamera = Camera.main;
            EnsureGiant();
        }

        private void Start()
        {
            if (_mainCamera == null)
            {
                _mainCamera = Camera.main;
            }

            if (city != null)
            {
                city.Build();
            }

            if (gameCamera != null && giantRoot != null)
            {
                gameCamera.SnapTo(giantRoot);
            }

            if (autoLoadGiantFromLibrary)
            {
                TryLoadGiantFromLibrary();
            }
        }

        private void Update()
        {
            if (_stompTimer > 0f)
            {
                _stompTimer -= Time.deltaTime;
            }

            if (_stompTimer <= 0f)
            {
                _stomping = false;
            }

            HandleTouch();
        }

        // ---------------------------------------------------------------------
        //  Giant setup / MMD reuse
        // ---------------------------------------------------------------------

        private void EnsureGiant()
        {
            if (giantRoot != null)
            {
                return;
            }

            GameObject go = new GameObject("Giant");
            go.transform.SetParent(transform, false);
            giantRoot = go.transform;

            // Placeholder so the mechanic works without any MMD asset. A capsule standing on the ground.
            _placeholderGiant = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            _placeholderGiant.name = "PlaceholderGiant";
            _placeholderGiant.transform.SetParent(giantRoot, false);
            _placeholderGiant.transform.localPosition = new Vector3(0f, 0.5f * giantScale, 0f);
            _placeholderGiant.transform.localScale = new Vector3(giantScale * 0.55f, giantScale, giantScale * 0.55f);

            // Remove the collider so movement/clicks don't get blocked by the giant itself.
            Collider col = _placeholderGiant.GetComponent<Collider>();
            if (col != null)
            {
                Destroy(col);
            }
        }

        private void TryLoadGiantFromLibrary()
        {
            if (player == null || player.library == null)
            {
                return;
            }

            MMDAssetLibrary library = player.library;
            if (library.entries == null || library.entries.Count == 0)
            {
                return;
            }

            // Configure the Phase 1 controller BEFORE loading so the anchor and scale are applied at
            // build time (the controller's ground-align runs inside LoadModel using these values).
            player.modelAnchor = giantRoot;
            player.modelScale = giantScale;
            player.autoPlayOnLoad = true; // the giantess dances while the player steers her
            player.loop = true;

            // Prefer the last-used model (via Phase 1 preferences), else the first in the library.
            string savedPath = MMDPlayerPreferences.LoadModelPath();
            bool loaded = !string.IsNullOrEmpty(savedPath) && player.LoadModelBySourcePath(savedPath);

            if (!loaded)
            {
                MMDAssetLibrary.ModelEntry first = library.entries[0];
                if (first != null && first.model != null)
                {
                    player.LoadModel(first.model);
                    loaded = true;
                }
            }

            if (loaded)
            {
                RemovePlaceholderGiant();
            }
        }

        /// <summary>Destroys the capsule placeholder once a real MMD giantess has been loaded.</summary>
        private void RemovePlaceholderGiant()
        {
            if (_placeholderGiant != null)
            {
                Destroy(_placeholderGiant);
                _placeholderGiant = null;
            }
        }

        // ---------------------------------------------------------------------
        //  Touch input
        // ---------------------------------------------------------------------

        private void HandleTouch()
        {
            if (Input.touchCount == 0)
            {
                _dragging = false;
                return;
            }

            Touch touch = Input.GetTouch(0);
            switch (touch.phase)
            {
                case TouchPhase.Began:
                    _dragging = true;
                    _touchStart = touch.position;
                    MoveGiantToward(touch.position);
                    break;

                case TouchPhase.Moved:
                case TouchPhase.Stationary:
                    if (_dragging)
                    {
                        MoveGiantToward(touch.position);
                    }
                    break;

                case TouchPhase.Ended:
                    if (_dragging && Vector2.Distance(touch.position, _touchStart) < 24f)
                    {
                        // A tap (no meaningful drag) = stomp.
                        DoStomp();
                    }
                    _dragging = false;
                    break;
            }
        }

        private void MoveGiantToward(Vector2 screenPoint)
        {
            if (giantRoot == null || _mainCamera == null)
            {
                return;
            }

            Ray ray = _mainCamera.ScreenPointToRay(screenPoint);
            Plane ground = new Plane(Vector3.up, Vector3.zero);
            if (!ground.Raycast(ray, out float enter))
            {
                return;
            }

            Vector3 hit = ray.GetPoint(enter);
            hit.y = giantRoot.position.y;

            Vector3 delta = hit - giantRoot.position;
            delta.y = 0f;
            if (delta.sqrMagnitude < 0.0001f)
            {
                return;
            }

            float step = moveSpeed * Time.deltaTime;
            Vector3 move = delta.normalized * Mathf.Min(step, delta.magnitude);
            giantRoot.position += move;

            if (faceMovement && player != null)
            {
                // Rotate the model to face walking direction (0° faces +Z in MMD).
                float yaw = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
                player.SetModelYaw(yaw);
            }

            // Walking into props knocks them over (continuous feedback, no full stomp anim).
            BreakPropsWithin(giantRoot.position, walkBreakRadius);
        }

        // ---------------------------------------------------------------------
        //  Stomp
        // ---------------------------------------------------------------------

        /// <summary>Performs a stomp at the giant's current position (subject to cooldown).</summary>
        public void DoStomp()
        {
            if (_stomping || _stompTimer > 0f)
            {
                return;
            }

            StartCoroutine(StompRoutine());
        }

        private IEnumerator StompRoutine()
        {
            _stomping = true;
            _stompTimer = stompCooldown;

            // Brief squash-and-recover so the stomp reads visually even on the placeholder giant.
            yield return StartCoroutine(SquashGiant());

            BreakPropsWithin(giantRoot.position, stompRadius);

            _stomping = false;
            _stompTimer = stompCooldown;
        }

        private IEnumerator SquashGiant()
        {
            if (giantRoot == null)
            {
                yield break;
            }

            Vector3 baseScale = giantRoot.localScale;
            float recover = 0.18f;
            float t = 0f;

            // Squash down.
            while (t < recover * 0.5f)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / (recover * 0.5f));
                giantRoot.localScale = new Vector3(baseScale.x * (1f + 0.15f * k),
                    baseScale.y * (1f - 0.3f * k),
                    baseScale.z * (1f + 0.15f * k));
                yield return null;
            }

            // Recover.
            t = 0f;
            while (t < recover * 0.5f)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / (recover * 0.5f));
                float vy = Mathf.Lerp(1f - 0.3f, 1f, k);
                float vx = Mathf.Lerp(1f + 0.15f, 1f, k);
                giantRoot.localScale = new Vector3(baseScale.x * vx, baseScale.y * vy, baseScale.z * vx);
                yield return null;
            }

            giantRoot.localScale = baseScale;
        }

        private void BreakPropsWithin(Vector3 center, float radius)
        {
            if (city == null)
            {
                return;
            }

            IReadOnlyList<MMDBreakableProp> props = city.Props;
            if (props == null)
            {
                return;
            }

            List<MMDBreakableProp> consumed = new List<MMDBreakableProp>();
            for (int i = 0; i < props.Count; ++i)
            {
                MMDBreakableProp prop = props[i];
                if (prop == null)
                {
                    continue;
                }

                Vector3 diff = prop.transform.position - center;
                diff.y = 0f;
                if (diff.magnitude > radius)
                {
                    continue;
                }

                prop.Stomp(AddScore);
                consumed.Add(prop);
            }

            if (consumed.Count > 0)
            {
                // Remove only the props we consumed; others continue to be stompable.
                city.RemoveProps(consumed);
            }
        }

        private void AddScore(int value)
        {
            Score += value;
            if (hud != null)
            {
                hud.UpdateScore(Score);
            }
        }

        // ---------------------------------------------------------------------
        //  Game control
        // ---------------------------------------------------------------------

        /// <summary>Resets score and regenerates the city. The giant's position is kept.</summary>
        public void ResetGame()
        {
            Score = 0;
            if (hud != null)
            {
                hud.UpdateScore(0);
            }

            if (city != null)
            {
                city.Build();
            }

            if (giantRoot != null)
            {
                giantRoot.position = new Vector3(0f, giantRoot.position.y, 0f);
            }
        }
    }
}
