using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GiantCity
{
    /// <summary>
    /// Drives the tiny protagonist: a left-side virtual joystick moves the player, a right-side drag orbits
    /// a low third-person camera. Movement is a simple transform move with obstacle avoidance against
    /// <see cref="CityBuilding"/> markers so the player can duck between buildings. Built entirely in code
    /// (no prefabs), so the scene only needs a Camera plus this component.
    ///
    /// The camera lives outside the scaled world root (so near/far stay sane) but its framing distance is
    /// derived from <see cref="WorldScaler"/> via <see cref="ApplyWorldScale"/>. Call <see cref="ApplyWorldScale"/>
    /// after the world scale is set.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TinyPlayerController : MonoBehaviour
    {
        [Header("Player")]
        public TinyPlayerCharacter character;
        public float moveSpeed = 6f;
        public float runSpeed = 10f;

        [Header("Camera")]
        public Transform cameraTarget;
        /// <summary>Distance (in small-world units) the camera stays behind the player, scaled by the world scale.</summary>
        public float cameraBaseDistance = 12f;
        /// <summary>Height (in small-world units) of the camera above the player, scaled by the world scale.</summary>
        public float cameraBaseHeight = 6f;
        public float lookHeight = 2.5f;
        public float cameraFollowLerp = 8f;
        public float orbitSpeed = 140f;
        public float pitchMin = 8f;
        public float pitchMax = 60f;

        private float _worldScale = 1f;
        private Vector2 _look;
        private bool _leftHeld;
        private bool _rightHeld;
        private Vector2 _leftTouchStart;
        private Vector2 _rightDrag;

        // Joystick visuals.
        private Image _stickBase;
        private Image _stickKnob;
        private RectTransform _stickBaseRT;
        private RectTransform _stickKnobRT;
        private const float StickRadius = 70f;

        private float _cameraYaw;
        private float _cameraPitch = 30f;

        private void Awake()
        {
            if (character == null)
            {
                character = GetComponentInChildren<TinyPlayerCharacter>();
            }
            if (character == null)
            {
                character = gameObject.AddComponent<TinyPlayerCharacter>();
                character.Build();
            }
            else if (character.gameObject == gameObject)
            {
                character.Build();
            }

            if (cameraTarget == null)
            {
                cameraTarget = character.transform;
            }

            EnsureCamera();
            EnsureCanvas();
        }

        private void Update()
        {
            HandleInput();
            Move();
            UpdateCamera();
        }

        // ---------------------------------------------------------------------
        //  Input (touch first, mouse fallback)
        // ---------------------------------------------------------------------

        private void HandleInput()
        {
            int touchCount = Input.touchCount;
            if (touchCount > 0)
            {
                HandleTouchInput();
                return;
            }

            HandleMouseInput();
        }

        private void HandleTouchInput()
        {
            // Find the first touch on the left half (joystick) and the first on the right half (camera).
            bool leftFound = false;
            bool rightFound = false;
            Vector2 leftPos = default;
            Vector2 rightPos = default;

            for (int i = 0; i < Input.touchCount; ++i)
            {
                Touch t = Input.GetTouch(i);
                if (t.position.x < Screen.width * 0.45f && !leftFound)
                {
                    leftFound = true;
                    leftPos = t.position;
                }
                else if (!rightFound)
                {
                    rightFound = true;
                    rightPos = t.position;
                }
            }

            if (leftFound)
            {
                _leftHeld = true;
                _look = ClampStick(StickDelta(leftPos));
                ShowJoystick();
                SetKnob(_look);
            }
            else
            {
                _leftHeld = false;
                _look = Vector2.zero;
                HideJoystick();
            }

            if (rightFound)
            {
                UpdateOrbit(rightPos);
            }
        }

        private void HandleMouseInput()
        {
            if (Input.GetMouseButton(0))
            {
                if (Input.mousePosition.x < Screen.width * 0.45f)
                {
                    _leftHeld = true;
                    _look = ClampStick(StickDelta(Input.mousePosition));
                    ShowJoystick();
                    SetKnob(_look);
                    return;
                }

                // Right side drag orbits the camera.
                _rightHeld = true;
                if (_rightDrag == Vector2.zero)
                {
                    _rightDrag = Input.mousePosition;
                }
                Vector2 delta = (Vector2)Input.mousePosition - _rightDrag;
                _rightDrag = Input.mousePosition;
                _cameraYaw += delta.x * orbitSpeed * 0.02f;
                _cameraPitch = Mathf.Clamp(_cameraPitch + (-delta.y) * orbitSpeed * 0.02f, pitchMin, pitchMax);
                _leftHeld = false;
                _look = Vector2.zero;
                HideJoystick();
            }
            else
            {
                _leftHeld = false;
                _rightHeld = false;
                _rightDrag = Vector2.zero;
                _look = Vector2.zero;
                HideJoystick();
            }
        }

        private Vector2 StickDelta(Vector2 screenPos)
        {
            Vector2 center = _stickBaseRT != null ? (Vector2)_stickBaseRT.position : new Vector2(Screen.width * 0.14f, Screen.height * 0.14f);
            return screenPos - center;
        }

        private static Vector2 ClampStick(Vector2 delta)
        {
            if (delta.magnitude > StickRadius)
            {
                delta = delta.normalized * StickRadius;
            }
            return delta / StickRadius;
        }

        private void UpdateOrbit(Vector2 newPos)
        {
            if (_leftHeld == false && !_rightHeld)
            {
                _rightHeld = true;
            }
            // Compare against the previous frame's position if we tracked it.
            if (_lastRightPos != Vector2.zero)
            {
                Vector2 delta = newPos - _lastRightPos;
                _cameraYaw += delta.x * orbitSpeed * 0.02f;
                _cameraPitch = Mathf.Clamp(_cameraPitch + (-delta.y) * orbitSpeed * 0.02f, pitchMin, pitchMax);
            }
            _lastRightPos = newPos;
        }

        private Vector2 _lastRightPos;

        // ---------------------------------------------------------------------
        //  Movement
        // ---------------------------------------------------------------------

        private void Move()
        {
            Transform root = transform;
            Vector3 input = new Vector3(_look.x, 0f, _look.y);
            if (input.sqrMagnitude < 0.0001f)
            {
                return;
            }

            // Camera-relative movement (forward follows camera yaw).
            Quaternion rot = Quaternion.Euler(0f, _cameraYaw, 0f);
            Vector3 worldDir = rot * input;
            worldDir.y = 0f;
            if (worldDir.sqrMagnitude < 0.0001f)
            {
                return;
            }
            worldDir.Normalize();

            float speed = _look.magnitude > 0.85f ? runSpeed : moveSpeed;
            Vector3 target = root.localPosition + worldDir * speed * Time.deltaTime;

            if (CanMoveTo(TargetWorld(root, target)))
            {
                root.localPosition = target;
            }

            if (character != null)
            {
                // Face the movement direction.
                float yaw = Mathf.Atan2(worldDir.x, worldDir.z) * Mathf.Rad2Deg;
                character.transform.rotation = Quaternion.Slerp(character.transform.rotation, Quaternion.Euler(0f, yaw, 0f), 12f * Time.deltaTime);
            }
        }

        private Vector3 TargetWorld(Transform root, Vector3 localTarget)
        {
            return transform.TransformPoint(localTarget);
        }

        private bool CanMoveTo(Vector3 worldPos)
        {
            Collider[] hits = Physics.OverlapSphere(worldPos, GiantCityConfig.TinyColliderRadius);
            foreach (Collider hit in hits)
            {
                if (hit == null)
                {
                    continue;
                }
                if (hit.GetComponentInParent<CityBuilding>() != null)
                {
                    return false;
                }
            }
            return true;
        }

        // ---------------------------------------------------------------------
        //  Camera
        // ---------------------------------------------------------------------

        private void EnsureCamera()
        {
            if (Camera.main != null)
            {
                return;
            }
            GameObject cameraGO = new GameObject("TinyCamera", typeof(Camera), typeof(AudioListener));
            cameraGO.tag = "MainCamera";
            Camera cam = cameraGO.GetComponent<Camera>();
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 5000f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.55f, 0.65f, 0.85f, 1f);
        }

        private void UpdateCamera()
        {
            Transform camT = Camera.main != null ? Camera.main.transform : null;
            if (camT == null || cameraTarget == null)
            {
                return;
            }

            float ax = _cameraYaw * Mathf.Deg2Rad;
            float ap = _cameraPitch * Mathf.Deg2Rad;

            float dist = cameraBaseDistance * _worldScale;
            float height = cameraBaseHeight * _worldScale;

            Vector3 forward = new Vector3(Mathf.Sin(ax), 0f, Mathf.Cos(ax));
            Vector3 up = new Vector3(0f, Mathf.Sin(ap), 0f);
            Vector3 offset = -forward * dist * Mathf.Cos(ap) + up * height;

            Vector3 desired = cameraTarget.position + offset;
            camT.position = Vector3.Lerp(camT.position, desired, cameraFollowLerp * Time.deltaTime);

            Vector3 look = cameraTarget.position + Vector3.up * (lookHeight * _worldScale);
            Vector3 dir = look - camT.position;
            if (dir.sqrMagnitude > 0.0001f)
            {
                camT.rotation = Quaternion.Slerp(camT.rotation, Quaternion.LookRotation(dir, Vector3.up), cameraFollowLerp * Time.deltaTime);
            }
        }

        /// <summary>Called by the world scaler/controller to update the camera framing scale.</summary>
        public void ApplyWorldScale(float worldScale)
        {
            _worldScale = worldScale;
        }

        /// <summary>Snaps the camera behind the player instantly (call after the player is repositioned).</summary>
        public void SnapCamera()
        {
            Transform camT = Camera.main != null ? Camera.main.transform : null;
            if (camT == null || cameraTarget == null)
            {
                return;
            }
            float ax = _cameraYaw * Mathf.Deg2Rad;
            float ap = _cameraPitch * Mathf.Deg2Rad;
            float dist = cameraBaseDistance * _worldScale;
            float height = cameraBaseHeight * _worldScale;
            Vector3 forward = new Vector3(Mathf.Sin(ax), 0f, Mathf.Cos(ax));
            Vector3 offset = -forward * dist * Mathf.Cos(ap) + Vector3.up * height;
            camT.position = cameraTarget.position + offset;
            Vector3 look = cameraTarget.position + Vector3.up * (lookHeight * _worldScale);
            camT.rotation = Quaternion.LookRotation(look - camT.position, Vector3.up);
        }

        // ---------------------------------------------------------------------
        //  Joystick UI
        // ---------------------------------------------------------------------

        private void EnsureCanvas()
        {
            if (FindObjectOfType<EventSystem>() == null)
            {
                new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            }

            Canvas canvas = FindObjectOfType<Canvas>();
            if (canvas == null)
            {
                GameObject canvasGO = new GameObject("GameCanvas", typeof(Canvas));
                canvas = canvasGO.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvasGO.AddComponent<GraphicRaycaster>();
                CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1280f, 720f);
                scaler.matchWidthOrHeight = 0f;
            }

            if (_stickBase == null)
            {
                BuildJoystick(canvas.transform);
            }
        }

        private void BuildJoystick(Transform canvasRoot)
        {
            RectTransform root = MakeRect("Joystick", canvasRoot);
            root.anchorMin = new Vector2(0f, 0f);
            root.anchorMax = new Vector2(0f, 0f);
            root.pivot = new Vector2(0.5f, 0.5f);
            root.anchoredPosition = new Vector2(120f, 120f);
            root.sizeDelta = new Vector2(StickRadius * 2f, StickRadius * 2f);

            _stickBase = root.gameObject.AddComponent<Image>();
            _stickBase.color = new Color(1f, 1f, 1f, 0.22f);
            _stickBaseRT = root;

            RectTransform knob = MakeRect("Knob", root);
            knob.anchorMin = new Vector2(0.5f, 0.5f);
            knob.anchorMax = new Vector2(0.5f, 0.5f);
            knob.pivot = new Vector2(0.5f, 0.5f);
            knob.anchoredPosition = Vector2.zero;
            knob.sizeDelta = new Vector2(56f, 56f);
            _stickKnob = knob.gameObject.AddComponent<Image>();
            _stickKnob.color = new Color(1f, 1f, 1f, 0.5f);
            _stickKnobRT = knob;

            root.gameObject.SetActive(false);
        }

        private void SetKnob(Vector2 look)
        {
            if (_stickKnobRT != null)
            {
                _stickKnobRT.anchoredPosition = look * StickRadius;
            }
        }

        private void ShowJoystick()
        {
            if (_stickBaseRT != null)
            {
                _stickBaseRT.gameObject.SetActive(true);
            }
        }

        private void HideJoystick()
        {
            if (_stickBaseRT != null)
            {
                _stickBaseRT.gameObject.SetActive(false);
            }
        }

        private static RectTransform MakeRect(string name, Transform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.GetComponent<RectTransform>();
        }
    }
}
