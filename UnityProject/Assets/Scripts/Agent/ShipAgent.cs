// UnityProject/Assets/Scripts/Agent/ShipAgent.cs
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;
using HormuzAI.Data;
using HormuzAI.Environment;

namespace HormuzAI.Agent
{
    public enum ShipType  { Korea = 0, Japan = 1, China = 2 }
    public enum ShipState { Idle, Navigating, Crashed, Success }

    [RequireComponent(typeof(Rigidbody))]
    public class ShipAgent : Unity.MLAgents.Agent
    {
        // ── Inspector ────────────────────────────────────────────────────

        [Header("Ship Identity")]
        [SerializeField] protected ShipStatsSO stats;
        [SerializeField] protected ShipType shipType;
        [SerializeField] int spawnIndex = 0;

        [Header("Sensing Thresholds")]
        [SerializeField] float shallowThreshold = 500f;   // m
        [SerializeField] float deepThreshold    = 1500f;  // m
        [SerializeField] float narrowThreshold  = 3000f;  // m
        [SerializeField] float raycastMaxDist   = 5000f;  // m
        [SerializeField] LayerMask sensorLayerMask = Physics.DefaultRaycastLayers;

        [Header("Scene References")]
        [SerializeField] SpawnManager spawnManager;
        [SerializeField] Transform    goal;

        // ── Private state ─────────────────────────────────────────────────

        Rigidbody _rb;
        float     _currentHealth;
        ShipState _state;
        float     _prevDistToGoal;
        float     _depthRatio;
        float     _widthRatio;

        // ── Unity / ML-Agents lifecycle ───────────────────────────────────

        public override void Initialize()
        {
            if (stats == null)
            {
                Debug.LogError($"[ShipAgent] stats (ShipStatsSO) is not assigned on '{name}'. Agent will not function.", this);
                return;
            }
            _rb = GetComponent<Rigidbody>();
            _rb.constraints = RigidbodyConstraints.FreezePositionY
                            | RigidbodyConstraints.FreezeRotationX
                            | RigidbodyConstraints.FreezeRotationZ;
            _state = ShipState.Idle;
        }

        public override void OnEpisodeBegin()
        {
            // 이전 에피소드가 타임아웃으로 끝났으면 소폭 패널티
            if (_state == ShipState.Navigating)
                AddReward(-0.1f);

            _currentHealth = stats.maxHealth;
            _state         = ShipState.Navigating;

            Transform spawn = spawnManager != null
                ? spawnManager.GetSpawnPoint(spawnIndex)
                : transform;

            transform.SetPositionAndRotation(spawn.position, spawn.rotation);
            _rb.linearVelocity  = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;

            if (goal == null)
                Debug.LogWarning($"[ShipAgent] goal is not assigned on '{name}'. Approach reward will not function.", this);

            _prevDistToGoal = goal != null
                ? Vector3.Distance(transform.position, goal.position)
                : 0f;
        }

        // ── Observations (9개) ────────────────────────────────────────────

        public override void CollectObservations(VectorSensor sensor)
        {
            // 1~3: 전방 레이캐스트 (좌 30°, 정면, 우 30°) — 0=장애물 없음, 1=바로 앞
            sensor.AddObservation(ObstacleRay(Quaternion.Euler(0, -30, 0) * transform.forward));
            sensor.AddObservation(ObstacleRay(transform.forward));
            sensor.AddObservation(ObstacleRay(Quaternion.Euler(0,  30, 0) * transform.forward));

            // 4: 목표 방향 (-1~+1, 정규화된 각도차)
            Vector3 toGoal = goal != null
                ? (goal.position - transform.position).normalized
                : transform.forward;
            float angle = Vector3.SignedAngle(transform.forward, toGoal, Vector3.up);
            sensor.AddObservation(angle / 180f);

            // 5: 현재 속도 (정규화)
            sensor.AddObservation(Mathf.Clamp01(_rb.linearVelocity.magnitude / stats.maxSpeed));

            // 6: 수심 비율 (0=얕음, 1=깊음)
            _depthRatio = GetDepthRatio();
            _widthRatio = GetWidthRatio();
            sensor.AddObservation(_depthRatio);

            // 7: 수로폭 비율 (0=좁음, 1=넓음)
            sensor.AddObservation(_widthRatio);

            // 8: 체력 비율
            sensor.AddObservation(_currentHealth / stats.maxHealth);

            // 9: 함선 타입 (0=Korea, 0.5=Japan, 1=China)
            sensor.AddObservation((float)shipType / 2f);
        }

        // ── Actions ───────────────────────────────────────────────────────

        public override void OnActionReceived(ActionBuffers actions)
        {
            if (_state != ShipState.Navigating) return;

            float throttle = actions.ContinuousActions[0]; // -1 ~ +1
            float steering = actions.ContinuousActions[1]; // -1 ~ +1

            float depthRatio  = _depthRatio;
            float widthRatio  = _widthRatio;
            float healthRatio = _currentHealth / stats.maxHealth;

            float speed    = stats.GetEffectiveSpeed(depthRatio, healthRatio);
            float turnRate = stats.GetEffectiveTurnRate(widthRatio);

            float clampedThrottle = Mathf.Clamp01(throttle);
            _rb.linearVelocity = Vector3.Lerp(
                _rb.linearVelocity,
                transform.forward * clampedThrottle * speed,
                Time.fixedDeltaTime * 5f);
            transform.Rotate(Vector3.up, steering * turnRate * 45f * Time.fixedDeltaTime);

            // 목표 접근 보상
            if (goal != null)
            {
                float dist = Vector3.Distance(transform.position, goal.position);
                AddReward((_prevDistToGoal - dist) * 0.001f);
                _prevDistToGoal = dist;
            }
        }

        public override void Heuristic(in ActionBuffers actionsOut)
        {
            var ca = actionsOut.ContinuousActions;
            ca[0] = Input.GetAxis("Vertical");
            ca[1] = Input.GetAxis("Horizontal");
        }

        // ── Collision / Trigger ───────────────────────────────────────────

        void OnTriggerEnter(Collider other)
        {
            if (_state != ShipState.Navigating) return;

            if (other.CompareTag("Goal"))
            {
                _state = ShipState.Success;
                SetReward(1f);
                EndEpisode();
            }
        }

        void OnCollisionEnter(Collision collision)
        {
            if (_state != ShipState.Navigating) return;

            bool isBoundary = collision.gameObject.CompareTag("Boundary");
            bool isTerrain  = collision.gameObject.layer == LayerMask.NameToLayer("Terrain");

            if (isBoundary || isTerrain)
            {
                _state = ShipState.Crashed;
                SetReward(-0.5f);
                EndEpisode();
            }
        }

        // ── Sensing helpers ───────────────────────────────────────────────

        /// <summary>0=장애물 없음(멀거나 미검출), 1=바로 앞. 관측값용.</summary>
        float ObstacleRay(Vector3 direction)
        {
            return Physics.Raycast(transform.position, direction, out RaycastHit hit, raycastMaxDist, sensorLayerMask)
                ? 1f - (hit.distance / raycastMaxDist)
                : 0f;
        }

        /// <summary>실제 거리 반환. 미검출 시 raycastMaxDist. 수로폭 계산용.</summary>
        float RaycastDistance(Vector3 direction)
        {
            return Physics.Raycast(transform.position, direction, out RaycastHit hit, raycastMaxDist, sensorLayerMask)
                ? hit.distance
                : raycastMaxDist;
        }

        /// <summary>0=얕음, 1=깊음. shallowThreshold~deepThreshold 사이를 선형 정규화.</summary>
        float GetDepthRatio()
        {
            if (!Physics.Raycast(transform.position, Vector3.down, out RaycastHit hit, deepThreshold + 100f, sensorLayerMask))
                return 1f;

            float depth = hit.distance;
            if (depth <= shallowThreshold) return 0f;
            if (depth >= deepThreshold)    return 1f;
            return (depth - shallowThreshold) / (deepThreshold - shallowThreshold);
        }

        /// <summary>0=좁음(양쪽 평균 < narrowThreshold), 1=넓음.</summary>
        float GetWidthRatio()
        {
            float left  = RaycastDistance(-transform.right);
            float right = RaycastDistance( transform.right);
            float avg   = (left + right) * 0.5f;
            return Mathf.Clamp01(avg / narrowThreshold);
        }
    }
}
