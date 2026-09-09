using UnityEngine;

namespace GiantCity
{
    /// <summary>
    /// Central tuning values for the Phase 2 giantess-city game. Kept as a single static config so the
    /// AI (state machine), stomp detector, tiny player, world scaler and city generator all read the same
    /// numbers without magic constants scattered across files. Each value can be overridden per-component
    /// on the inspector; this is the "CONFIG" the task refers to.
    ///
    /// All lengths are assumed to be in the *small* world (the world root's local space), except where a
    /// comment explicitly says it is measured in the giantess's world scale.
    /// </summary>
    public static class GiantCityConfig
    {
        // ---------------------------------------------------------------------
        //  Scale
        // ---------------------------------------------------------------------
        /// <summary>Height of the tiny player in the small world, before scaling (a normal human).</summary>
        public const float PlayerReferenceHeight = 1.7f;
        /// <summary>How many toe-lengths the player should be (1 = exactly one toe).</summary>
        public const float PlayerToFootRatio = 1f;
        /// <summary>Lower bound on the automatically computed world scale, to stay numerically safe.</summary>
        public const float MinWorldScale = 0.001f;
        /// <summary>Upper bound on the automatically computed world scale.</summary>
        public const float MaxWorldScale = 100f;

        // ---------------------------------------------------------------------
        //  Giantess AI
        // ---------------------------------------------------------------------
        public const float PatrolRadius = 40f;
        public const float PatrolSpeed = 3f;
        public const float DetectRange = 90f;
        public const float DetectConeDegrees = 120f;
        public const float ChaseSpeed = 6.5f;
        public const float ChaseSpeedRun = 9.5f;
        /// <summary>Distance at which the giantess stops chasing and winds up a stomp.</summary>
        public const float StompTriggerDistance = 14f;
        /// <summary>World y the giantess keeps its root at (so she does not sink into the ground).</summary>
        public const float GiantGroundY = 0f;
        /// <summary>Amplitude of the walk bob applied to the whole model to reduce "sliding".</summary>
        public const float WalkBobAmplitude = 0.35f;
        /// <summary>Turn speed (deg/s) toward the target.</summary>
        public const float TurnSpeed = 160f;

        // ---------------------------------------------------------------------
        //  Stomp detection
        // ---------------------------------------------------------------------
        /// <summary>Seconds the foot must be descending before a stomp counts, so the player can see it coming.</summary>
        public const float FootDescendWindow = 0.12f;
        /// <summary>Horizontal radius (in giantess world space) around the foot that counts as "under the foot".</summary>
        public const float StompHitRadius = 3.5f;
        /// <summary>Invulnerability (seconds) after being stomped, so a single event never double-hits.</summary>
        public const float HitInvulnerability = 2f;

        // ---------------------------------------------------------------------
        //  Tiny player
        // ---------------------------------------------------------------------
        public const float TinyMoveSpeed = 6f;
        public const float TinyRunSpeed = 10f;
        public const float TinySpawnY = 0.05f;
        /// <summary>Character radius used for obstacle avoidance in the small world.</summary>
        public const float TinyColliderRadius = 0.35f;

        // ---------------------------------------------------------------------
        //  City
        // ---------------------------------------------------------------------
        /// <summary>Half-extent (in small-world units) of the city block grid before scaling.</summary>
        public const float CityHalfSize = 90f;
        /// <summary>Base building footprint in small-world units.</summary>
        public const float BuildingBaseSize = 8f;
        public const float RoadWidth = 6f;
    }
}
