using UnityEngine;

namespace GiantCity
{
    /// <summary>
    /// Scales the *world* (city + tiny player) to match the giantess without ever scaling the giantess.
    ///
    /// The key to stable MMD physics is that the giantess is imported at its native scale and its Bullet
    /// bodies/joints are left alone. Instead we shrink everything else: the world root is set to a scale
    /// such that a 1.7-unit tiny player ends up roughly one toe of the giantess.
    ///
    /// The scale is auto-derived from the giantess's renderer bounds (height * a foot-length factor) when
    /// <see cref="autoCompute"/> is on, otherwise it uses <see cref="manualScale"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WorldScaler : MonoBehaviour
    {
        [Tooltip("Root that holds the whole miniature world (city + tiny player). Its scale is what we change.")]
        public Transform worldRoot;

        [Tooltip("When enabled, derive the scale from the giantess's height (see footLengthFactor).")]
        public bool autoCompute = true;

        [Tooltip("Giantess height (m) used when autoCompute is off.")]
        public float manualGiantHeight = 1.7f;

        [Tooltip("Giantess foot length as a fraction of her height, used to estimate a single toe.")]
        public float footLengthFactor = 0.15f;

        [Tooltip("How many toes tall the tiny player should be.")]
        public float playerToFootRatio = 1f;

        /// <summary>The last applied world scale.</summary>
        public float AppliedScale { get; private set; } = 1f;

        /// <summary>Applies the world scale based on the given giantess bounds (or the manual height).</summary>
        public float Apply(Bounds giantBounds)
        {
            float giantHeight = autoCompute ? giantBounds.size.y : manualGiantHeight;
            if (giantHeight <= 0f)
            {
                giantHeight = manualGiantHeight;
            }

            float footLength = giantHeight * footLengthFactor;
            float desiredPlayerWorldHeight = footLength * playerToFootRatio;
            float scale = desiredPlayerWorldHeight / GiantCityConfig.PlayerReferenceHeight;

            scale = Mathf.Clamp(scale, GiantCityConfig.MinWorldScale, GiantCityConfig.MaxWorldScale);

            if (worldRoot != null)
            {
                worldRoot.localScale = Vector3.one * scale;
            }
            AppliedScale = scale;
            return scale;
        }
    }
}
