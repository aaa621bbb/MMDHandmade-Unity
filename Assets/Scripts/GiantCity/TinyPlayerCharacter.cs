using System.Collections;
using UnityEngine;

namespace GiantCity
{
    /// <summary>
    /// The procedural tiny protagonist. Built from primitives (body capsule + head sphere) so no art assets
    /// are required. Handles hit feedback: flash red briefly, knock back, and respawn. A caller (the
    /// <see cref="TinyPlayerController"/>) decides when a stomp counts.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TinyPlayerCharacter : MonoBehaviour
    {
        [Tooltip("Height of the character in the small world (a normal human before scaling).")]
        public float height = 1.7f;
        [Tooltip("How long the red hit flash lasts.")]
        public float flashDuration = 0.5f;
        [Tooltip("How far the character is knocked back on a hit (small-world units).")]
        public float knockbackDistance = 2.5f;

        private Renderer _bodyRenderer;
        private Color _bodyColor;
        private bool _flashing;

        /// <summary>Builds the primitive-based look. Called once by the controller at setup.</summary>
        public void Build()
        {
            // Body.
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            body.transform.SetParent(transform, false);
            body.transform.localPosition = new Vector3(0f, height * 0.5f, 0f);
            body.transform.localScale = new Vector3(0.5f, height * 0.5f, 0.5f);

            // Remove the auto collider on the body so a kinematic transform move is not blocked by it.
            Collider bodyCol = body.GetComponent<Collider>();
            if (bodyCol != null)
            {
                Destroy(bodyCol);
            }

            _bodyRenderer = body.GetComponent<Renderer>();
            if (_bodyRenderer != null)
            {
                _bodyColor = new Color(0.85f, 0.4f, 0.5f, 1f);
                _bodyRenderer.material = MakeMaterial("PlayerBody", _bodyColor);
            }

            // Head.
            GameObject head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.name = "Head";
            head.transform.SetParent(transform, false);
            head.transform.localPosition = new Vector3(0f, height * 1.05f, 0f);
            head.transform.localScale = Vector3.one * 0.45f;

            Renderer headR = head.GetComponent<Renderer>();
            if (headR != null)
            {
                headR.material = MakeMaterial("PlayerHead", new Color(1f, 0.92f, 0.85f, 1f));
            }

            // A small collider on the root so the player can be found and kept out of buildings.
            CapsuleCollider col = gameObject.AddComponent<CapsuleCollider>();
            col.height = height;
            col.radius = height * 0.25f;
            col.center = new Vector3(0f, height * 0.5f, 0f);
        }

        /// <summary>Briefly flashes the body red and knocks the character back along a direction.</summary>
        public void HitFeedback(Vector3 knockDirection)
        {
            if (_flashing)
            {
                return;
            }
            StartCoroutine(FlashAndKnockback(knockDirection));
        }

        private IEnumerator FlashAndKnockback(Vector3 knockDirection)
        {
            _flashing = true;

            if (_bodyRenderer != null && _bodyRenderer.material != null)
            {
                _bodyRenderer.material.color = Color.red;
            }

            if (knockDirection.sqrMagnitude > 0.0001f)
            {
                Vector3 start = transform.localPosition;
                transform.localPosition = start + knockDirection.normalized * knockbackDistance;
            }

            yield return new WaitForSeconds(flashDuration);

            if (_bodyRenderer != null && _bodyRenderer.material != null)
            {
                _bodyRenderer.material.color = _bodyColor;
            }
            _flashing = false;
        }

        private static Material MakeMaterial(string name, Color color)
        {
            Shader shader = Shader.Find("Standard");
            if (shader == null)
            {
                return null;
            }
            Material material = new Material(shader);
            material.name = name;
            material.color = color;
            return material;
        }
    }
}
