using UnityEngine;

namespace MMDGiantGame
{
    /// <summary>
    /// Follow camera for the giant stomp game. Keeps the giant framed from a high behind/side angle,
    /// smoothly lerping to keep her in view, and supports two-finger pinch zoom on mobile. This is a
    /// dedicated game camera (distinct from the Phase 1 orbit camera) because a stomp game wants a
    /// stable, player-centred view rather than free orbit.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MMDGiantCamera : MonoBehaviour
    {
        [Header("Target")]
        public Transform target;

        [Header("Framing")]
        [Tooltip("Horizontal distance behind the giant.")]
        public float distance = 96f;
        [Tooltip("Height of the camera above the giant.")]
        public float height = 52f;
        [Tooltip("Height on the target that the camera looks at.")]
        public float lookHeight = 20f;
        [Tooltip("How quickly the camera catches up with a moving giant (higher = snappier).")]
        public float followLerp = 5f;

        [Header("Zoom")]
        public float minDistance = 30f;
        public float maxDistance = 320f;
        public float pinchZoomSpeed = 0.02f;

        private float _lastPinch;

        private void LateUpdate()
        {
            if (target == null)
            {
                return;
            }

            Vector3 desired = target.position + new Vector3(0f, height, -distance);
            transform.position = Vector3.Lerp(transform.position, desired, followLerp * Time.deltaTime);

            Vector3 look = target.position + new Vector3(0f, lookHeight, 0f);
            Vector3 forward = look - transform.position;
            if (forward.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
            }

            HandlePinchZoom();
        }

        private void HandlePinchZoom()
        {
            if (Input.touchCount >= 2)
            {
                Touch a = Input.GetTouch(0);
                Touch b = Input.GetTouch(1);
                float currentDistance = Vector2.Distance(a.position, b.position);

                if (_lastPinch > 0.0001f)
                {
                    float delta = currentDistance - _lastPinch;
                    distance = Mathf.Clamp(distance * (1f - delta * pinchZoomSpeed * 10f), minDistance, maxDistance);
                }

                _lastPinch = currentDistance;
            }
            else
            {
                _lastPinch = 0f;
            }
        }

        /// <summary>Immediately snaps the camera to the ideal framing of the given target.</summary>
        public void SnapTo(Transform newTarget)
        {
            target = newTarget;
            if (target == null)
            {
                return;
            }

            Vector3 desired = target.position + new Vector3(0f, height, -distance);
            transform.position = desired;

            Vector3 look = target.position + new Vector3(0f, lookHeight, 0f);
            Vector3 forward = look - transform.position;
            if (forward.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
            }
        }
    }
}
