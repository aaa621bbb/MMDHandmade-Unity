using UnityEngine;

namespace MMDPlayer
{
    /// <summary>Named camera viewpoints for the orbit camera; used by the UI's view presets.</summary>
    public enum CameraView
    {
        Front = 0,
        Back = 1,
        Left = 2,
        Right = 3,
        Top = 4,
        ThreeQuarter = 5,
        Reset = 6,
    }

    /// <summary>
    /// Orbit camera for the MMD player. Left-drag orbits, scroll-wheel zooms, right/middle-drag pans,
    /// and double-click resets the view. On mobile a single finger orbits and two-finger pinch zooms.
    /// Automatically follows the loaded model (it listens to the controller's
    /// <see cref="MMDPlayerController.OnModelLoaded"/> event and frames the model bounds).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MMDOrbitCamera : MonoBehaviour
    {
        [Header("Target")]
        [Tooltip("Transform this camera orbits. If null, the player auto-wires it to the loaded model.")]
        public Transform target;

        [Header("View")]
        [Tooltip("Current orbit distance along the view axis.")]
        public float distance = 4f;
        public float minDistance = 0.5f;
        public float maxDistance = 60f;
        [Tooltip("Orbit angles in degrees: X = pitch (toward the ground), Y = yaw.")]
        public Vector2 orbitAngles = new Vector2(15f, 0f);
        [Tooltip("Offset (relative to the target) of the point the camera looks at.")]
        public Vector3 focusOffset = new Vector3(0f, 1f, 0f);

        [Header("Input")]
        public float orbitSpeed = 3f;
        public float zoomSpeed = 0.12f;
        public float panSpeed = 0.5f;
        [Tooltip("Downward tilt (degrees) applied when focusing a model.")]
        public float focusPitch = 15f;
        [Tooltip("When enabled, the camera keeps re-framing the target if it moves (e.g. auto-follow after load).")]
        public bool followTarget = true;

        // --- mobile multi-touch pinch-zoom state ---
        private float _lastPinchDistance;

        private Vector3 _lastMousePosition;
        private float _lastClickTime;
        private Vector3 _lastClickPosition;

        private void Awake()
        {
            // If the controller exists, listen for model loads so we auto-frame the model.
            MMDPlayerController controller = FindObjectOfType<MMDPlayerController>();
            if (controller != null)
            {
                controller.OnModelLoaded += OnModelLoaded;
                controller.OnError += OnControllerError;
            }
        }

        private void OnDestroy()
        {
            MMDPlayerController controller = FindObjectOfType<MMDPlayerController>();
            if (controller != null)
            {
                controller.OnModelLoaded -= OnModelLoaded;
                controller.OnError -= OnControllerError;
            }
        }

        private void OnControllerError(string message)
        {
            // Physics errors etc. do not change the camera; deliberately left empty so the camera
            // keeps whatever view it had.
        }

        private void LateUpdate()
        {
            if (target == null)
            {
                return;
            }

            HandleInput();
            ApplyTransform();
        }

        private void HandleInput()
        {
            // Mobile pinch-to-zoom (two fingers). When pinching we skip the single-finger orbit/pan so the
            // two gestures never fight each other.
            if (Input.touchCount >= 2)
            {
                HandlePinchZoom();
                return;
            }

            // Not pinching: reset the pinch baseline so the next pinch starts fresh.
            _lastPinchDistance = 0f;

            // Double-click to reset.
            if (Input.GetMouseButtonDown(0))
            {
                if (Time.unscaledTime - _lastClickTime < 0.3f &&
                    (Input.mousePosition - _lastClickPosition).sqrMagnitude < 25f)
                {
                    ResetView();
                }
                _lastClickTime = Time.unscaledTime;
                _lastClickPosition = Input.mousePosition;
            }

            if (Input.GetMouseButtonDown(0))
            {
                _lastMousePosition = Input.mousePosition;
            }

            if (Input.GetMouseButton(0))
            {
                Vector3 delta = Input.mousePosition - _lastMousePosition;
                _lastMousePosition = Input.mousePosition;

                orbitAngles.y += delta.x * orbitSpeed;
                orbitAngles.x -= delta.y * orbitSpeed;
                orbitAngles.x = Mathf.Clamp(orbitAngles.x, -89f, 89f);
            }
            else if (Input.GetMouseButton(1) || Input.GetMouseButton(2))
            {
                Vector3 delta = Input.mousePosition - _lastMousePosition;
                _lastMousePosition = Input.mousePosition;

                Pan(delta);
            }
            else
            {
                _lastMousePosition = Input.mousePosition;
            }

            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.0001f)
            {
                distance = Mathf.Clamp(distance * (1f - scroll * zoomSpeed * 10f), minDistance, maxDistance);
            }
        }

        private void Pan(Vector3 screenDelta)
        {
            // Move the focus point along the camera's right and up axes.
            Vector3 right = transform.right;
            Vector3 up = transform.up;
            float scale = distance * 0.001f;

            focusOffset += right * (-screenDelta.x * scale * panSpeed);
            focusOffset += up * (screenDelta.y * scale * panSpeed);
        }

        /// <summary>Two-finger pinch-zoom: the distance between the fingers scales the camera distance.</summary>
        private void HandlePinchZoom()
        {
            Touch a = Input.GetTouch(0);
            Touch b = Input.GetTouch(1);

            float currentDistance = Vector2.Distance(a.position, b.position);
            if (currentDistance <= 0.0001f)
            {
                return;
            }

            if (_lastPinchDistance > 0.0001f)
            {
                float delta = currentDistance - _lastPinchDistance;
                distance = Mathf.Clamp(distance * (1f - delta * zoomSpeed * 0.02f), minDistance, maxDistance);
                ApplyTransform();
            }

            _lastPinchDistance = currentDistance;
        }

        /// <summary>Switches to a named camera viewpoint, framing the current target if one is set.</summary>
        public void SetView(CameraView view)
        {
            if (view == CameraView.Reset)
            {
                ResetView();
                return;
            }

            (float pitch, float yaw) = GetViewAngles(view);

            // When we have a valid target, re-center the focus on the model's bounds height so the
            // chosen viewpoint actually frames the model.
            if (target != null)
            {
                Bounds bounds = ComputeBounds(target);
                if (bounds.extents.sqrMagnitude > 0.0001f)
                {
                    focusOffset = new Vector3(0f, bounds.center.y - target.position.y, 0f);
                }
            }

            orbitAngles = new Vector2(pitch, yaw);
            ApplyTransform();
        }

        private static (float pitch, float yaw) GetViewAngles(CameraView view)
        {
            switch (view)
            {
                case CameraView.Back: return (15f, 180f);
                case CameraView.Left: return (15f, -90f);
                case CameraView.Right: return (15f, 90f);
                case CameraView.Top: return (88f, 0f);
                case CameraView.ThreeQuarter: return (18f, 35f);
                case CameraView.Front:
                default:
                    return (15f, 0f);
            }
        }

        /// <summary>Sets the orbit distance directly, clamped to the configured range.</summary>
        public void SetZoom(float value)
        {
            distance = Mathf.Clamp(value, minDistance, maxDistance);
        }

        private void ApplyTransform()
        {
            Quaternion rotation = Quaternion.Euler(orbitAngles.x, orbitAngles.y, 0f);
            Vector3 lookAtPoint = target.position + focusOffset;
            Vector3 position = lookAtPoint - rotation * new Vector3(0f, 0f, -distance);

            transform.position = position;
            transform.rotation = rotation;
        }

        /// <summary>Focuses the camera on a transform using its renderer bounds.</summary>
        public void FocusOn(Transform newTarget)
        {
            target = newTarget;
            if (target == null)
            {
                return;
            }

            Bounds bounds = ComputeBounds(target);
            FocusOn(bounds);
        }

        /// <summary>Frames a bounding box so the whole model is visible, looking down ~15 degrees.</summary>
        public void FocusOn(Bounds bounds)
        {
            float radius = Mathf.Max(bounds.extents.magnitude, 0.5f);
            float verticalFov = Mathf.Deg2Rad * (Camera.main != null ? Camera.main.fieldOfView : 60f);
            float halfFov = verticalFov * 0.5f;
            float distanceForHeight = radius / Mathf.Sin(halfFov);

            distance = Mathf.Clamp(distanceForHeight * 1.15f, minDistance, maxDistance);
            orbitAngles = new Vector2(focusPitch, orbitAngles.y);

            // Look at the center of the bounds, expressed as an offset from the target.
            focusOffset = bounds.center - target.position;
        }

        /// <summary>Resets the orbit angles and distance while keeping the current target.</summary>
        public void ResetView()
        {
            if (target == null)
            {
                return;
            }

            orbitAngles = new Vector2(15f, 0f);
            focusOffset = new Vector3(0f, 1f, 0f);
            distance = Mathf.Clamp(4f, minDistance, maxDistance);
            ApplyTransform();
        }

        private void OnModelLoaded()
        {
            if (!followTarget)
            {
                return;
            }

            MMDPlayerController controller = FindObjectOfType<MMDPlayerController>();
            if (controller == null)
            {
                return;
            }

            Transform modelRoot = controller.TransformManager != null ? controller.TransformManager.transform : null;
            if (modelRoot == null)
            {
                return;
            }

            FocusOn(modelRoot);
        }

        private static Bounds ComputeBounds(Transform current)
        {
            Renderer[] renderers = current.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0)
            {
                return new Bounds(current.position, Vector3.one);
            }

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; ++i)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }
            return bounds;
        }
    }
}
