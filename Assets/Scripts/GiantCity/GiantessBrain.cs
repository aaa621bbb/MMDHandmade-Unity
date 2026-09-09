using UnityEngine;

namespace GiantCity
{
    /// <summary>
    /// The giantess's chase-and-stomp AI. A small state machine that drives the *whole model root* only
    /// (position / yaw / a subtle bob & lean) — it never writes individual bones, which stays the sole
    /// responsibility of UMT's <see cref="MMDTransformManager"/>. This is what keeps the MMD physics natural:
    /// the root moves, the solver and Bullet react, and a stomp is just a quick root drop that the
    /// <see cref="StompDetector"/> turns into a hit.
    ///
    /// States: Patrol → Chase → Windup → Stomp → Recover (then back to Chase/Patrol).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GiantessBrain : MonoBehaviour
    {
        public enum State { Patrol, Chase, Windup, Stomp, Recover }

        [Header("Targets")]
        public Transform player;

        [Header("Patrol")]
        public float patrolRadius = 40f;
        public float patrolSpeed = 3f;

        [Header("Chase")]
        public float detectRange = 90f;
        public float chaseSpeed = 6.5f;
        public float runSpeed = 9.5f;
        public float stompTriggerDistance = 14f;

        [Header("Timing")]
        public float windupDuration = 0.9f;
        public float stompDuration = 0.25f;
        public float recoverDuration = 0.8f;

        [Header("Model Root")]
        [Tooltip("Yaw offset (deg) to correct the MMD model's rest facing. Most MMD models face +Z, so 0 is usually right.")]
        public float modelForwardOffset = 0f;
        public float turnSpeed = 160f;

        /// <summary>Current AI state, exposed for HUD/debugging.</summary>
        public State CurrentState { get; private set; } = State.Patrol;

        private Transform _root;
        private float _stateTimer;
        private Vector3 _patrolTarget;
        private float _bobTime;
        private float _currentSpeed;
        private float _leanPitch;
        private float _stompDrop;

        /// <summary>Initialises the brain with the model root it moves and the player it chases.</summary>
        public void Configure(Transform root, Transform targetPlayer)
        {
            _root = root;
            player = targetPlayer;
            PickPatrolTarget();
            CurrentState = State.Patrol;
            _stateTimer = 0f;
        }

        private void Update()
        {
            if (_root == null || player == null)
            {
                return;
            }

            _stateTimer += Time.deltaTime;

            switch (CurrentState)
            {
                case State.Patrol: TickPatrol(); break;
                case State.Chase: TickChase(); break;
                case State.Windup: TickWindup(); break;
                case State.Stomp: TickStomp(); break;
                case State.Recover: TickRecover(); break;
            }

            ApplyRootMotion();
        }

        // ---------------------------------------------------------------------
        //  State behaviours
        // ---------------------------------------------------------------------

        private void TickPatrol()
        {
            float dist = Vector3.Distance(_root.position, player.position);
            if (dist <= detectRange)
            {
                Enter(State.Chase);
                return;
            }

            Vector3 toTarget = _patrolTarget - _root.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude < 1f)
            {
                PickPatrolTarget();
                return;
            }

            MoveToward(_patrolTarget, patrolSpeed);
            _leanPitch = 2f;
        }

        private void TickChase()
        {
            float dist = Vector3.Distance(_root.position, player.position);
            if (dist > detectRange * 1.5f)
            {
                Enter(State.Patrol);
                return;
            }

            if (dist <= stompTriggerDistance)
            {
                Enter(State.Windup);
                return;
            }

            // Run when reasonably far, walk when closing, so the approach reads naturally.
            float speed = dist > stompTriggerDistance * 2.2f ? runSpeed : chaseSpeed;
            MoveToward(player.position, speed);
            _leanPitch = 4f;
        }

        private void TickWindup()
        {
            // Stop moving so she plants her feet before the stomp; raise up and lean forward.
            _currentSpeed = 0f;
            _leanPitch = Mathf.Lerp(_leanPitch, 10f, 6f * Time.deltaTime);
            _stompDrop = Mathf.Lerp(_stompDrop, 1.2f, 4f * Time.deltaTime);
            if (_stateTimer >= windupDuration)
            {
                Enter(State.Stomp);
            }
        }

        private void TickStomp()
        {
            // The whole root drops fast so the feet descend; the StompDetector reads the feet Y descent.
            _leanPitch = Mathf.Lerp(_leanPitch, 12f, 10f * Time.deltaTime);
            _stompDrop = Mathf.Lerp(_stompDrop, -3f, 10f * Time.deltaTime);
            if (_stateTimer >= stompDuration)
            {
                Enter(State.Recover);
            }
        }

        private void TickRecover()
        {
            _leanPitch = Mathf.Lerp(_leanPitch, 0f, 6f * Time.deltaTime);
            _stompDrop = Mathf.Lerp(_stompDrop, 0f, 6f * Time.deltaTime);
            if (_stateTimer >= recoverDuration)
            {
                float dist = Vector3.Distance(_root.position, player.position);
                Enter(dist <= detectRange ? State.Chase : State.Patrol);
            }
        }

        // ---------------------------------------------------------------------
        //  Root motion
        // ---------------------------------------------------------------------

        private void MoveToward(Vector3 target, float speed)
        {
            Vector3 toTarget = target - _root.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude < 0.0001f)
            {
                return;
            }

            _currentSpeed = speed;

            // Turn toward the target.
            float desiredYaw = Mathf.Atan2(toTarget.x, toTarget.z) * Mathf.Rad2Deg;
            Quaternion targetRot = Quaternion.Euler(0f, desiredYaw, 0f);
            _root.rotation = Quaternion.RotateTowards(_root.rotation, targetRot, turnSpeed * Time.deltaTime);
        }

        private void ApplyRootMotion()
        {
            if (_currentSpeed <= 0.01f)
            {
                // Idle bob decays.
                _bobTime = 0f;
                ApplyTransform(0f);
                return;
            }

            _bobTime += Time.deltaTime;
            float bob = Mathf.Sin(_bobTime * 6f) * GiantCityConfig.WalkBobAmplitude * Mathf.Clamp01(_currentSpeed / runSpeed);
            ApplyTransform(bob);
        }

        private void ApplyTransform(float bob)
        {
            Vector3 pos = _root.position;
            pos.y = GiantCityConfig.GiantGroundY + bob + _stompDrop;
            _root.position = pos;

            float yaw = _root.rotation.eulerAngles.y + modelForwardOffset;
            _root.rotation = Quaternion.Euler(_leanPitch, yaw, 0f);
        }

        private void PickPatrolTarget()
        {
            Vector2 rand = Random.insideUnitCircle * patrolRadius;
            _patrolTarget = new Vector3(rand.x, GiantCityConfig.GiantGroundY, rand.y);
        }

        private void Enter(State next)
        {
            CurrentState = next;
            _stateTimer = 0f;
        }
    }
}
