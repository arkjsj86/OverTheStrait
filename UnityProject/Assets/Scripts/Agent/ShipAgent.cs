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

        [Header("Episode Settings")]
        [SerializeField] float maxEpisodeTime = 150f;   // 게임 내 초 (timeScale=20 → 실제 7.5초)

        [Header("Sensing Thresholds")]
        [SerializeField] float shallowThreshold = 500f;
        [SerializeField] float deepThreshold    = 1500f;
        [SerializeField] float narrowThreshold  = 3000f;
        [SerializeField] float raycastMaxDist   = 5000f;
        [SerializeField] LayerMask sensorLayerMask = Physics.DefaultRaycastLayers;

        [Header("Scene References")]
        [SerializeField] SpawnManager      spawnManager;
        [SerializeField] Transform         goal;
        [SerializeField] GenerationManager generationManager;

        // ── 마커 색상 ─────────────────────────────────────────────────────
        static readonly Color ColorAlive   = new Color(0.1f,  1.0f,  0.35f); // 녹색 — 생존
        static readonly Color ColorDead    = new Color(1.0f,  0.1f,  0.1f);  // 빨간색 — 충돌/타임아웃
        static readonly Color ColorSuccess = new Color(1.0f,  0.85f, 0.0f);  // 금색 — 목표 도달

        // ── Private state ─────────────────────────────────────────────────

        Rigidbody    _rb;
        float        _currentHealth;
        ShipState    _state;
        float        _prevDistToGoal;
        bool         _initialized;
        float        _depthRatio;
        float        _widthRatio;
        float        _episodeTimer;
        bool         _waitingForGenReset;   // 세대 리셋 대기 중 (재스폰 억제)
        Material     _markerMat;
        Material     _trailMat;
        TrailRenderer _trail;

        // ── Unity / ML-Agents lifecycle ───────────────────────────────────

        public void SetRefs(SpawnManager sm, Transform g, ShipStatsSO s, GenerationManager gm)
        {
            spawnManager      = sm;
            goal              = g;
            stats             = s;
            generationManager = gm;
        }

        public override void Initialize()
        {
            _rb = GetComponent<Rigidbody>();
            _rb.constraints = RigidbodyConstraints.FreezePositionY
                            | RigidbodyConstraints.FreezeRotationX
                            | RigidbodyConstraints.FreezeRotationZ;
            _state = ShipState.Idle;
            _initialized = true;

            // 마커 / 트레일 참조 캐시
            var markerGO = transform.Find("Marker");
            if (markerGO != null)
            {
                var mr = markerGO.GetComponent<MeshRenderer>();
                if (mr != null) _markerMat = mr.material;   // 인스턴스 복사
            }
            _trail = GetComponent<TrailRenderer>();
            if (_trail != null) _trailMat = _trail.material;
        }

        public override void OnEpisodeBegin()
        {
            if (!_initialized) return;

            // 세대 리셋 대기 중 → 재스폰하지 않고 제자리 동결
            if (_waitingForGenReset)
            {
                _rb.linearVelocity  = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
                return;
            }

            if (stats == null)
            {
                Debug.LogError($"[ShipAgent] stats not assigned on '{name}'.", this);
                _state = ShipState.Idle;
                return;
            }

            _currentHealth = stats.maxHealth;
            _state         = ShipState.Navigating;
            _episodeTimer  = 0f;
            SetMarkerColor(ColorAlive);

            Vector3    spawnPos = spawnManager != null
                ? spawnManager.GetRandomSpawnPosition()
                : transform.position;
            Quaternion spawnRot = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            transform.SetPositionAndRotation(spawnPos, spawnRot);

            _rb.linearVelocity  = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;

            _prevDistToGoal = goal != null
                ? Vector3.Distance(transform.position, goal.position)
                : 0f;
        }

        // ── Observations (9개) ────────────────────────────────────────────

        public override void CollectObservations(VectorSensor sensor)
        {
            // 비활성 상태일 때는 0으로 채워 관측 크기 유지
            if (!_initialized || _state == ShipState.Crashed || _state == ShipState.Success)
            {
                for (int i = 0; i < 9; i++) sensor.AddObservation(0f);
                return;
            }

            sensor.AddObservation(ObstacleRay(Quaternion.Euler(0, -30, 0) * transform.forward));
            sensor.AddObservation(ObstacleRay(transform.forward));
            sensor.AddObservation(ObstacleRay(Quaternion.Euler(0,  30, 0) * transform.forward));

            Vector3 toGoal = goal != null
                ? (goal.position - transform.position).normalized
                : transform.forward;
            float angle = Vector3.SignedAngle(transform.forward, toGoal, Vector3.up);
            sensor.AddObservation(angle / 180f);

            sensor.AddObservation(Mathf.Clamp01(_rb.linearVelocity.magnitude / stats.maxSpeed));

            _depthRatio = GetDepthRatio();
            _widthRatio = GetWidthRatio();
            sensor.AddObservation(_depthRatio);
            sensor.AddObservation(_widthRatio);

            sensor.AddObservation(_currentHealth / stats.maxHealth);
            sensor.AddObservation((float)shipType / 2f);
        }

        // ── Actions ───────────────────────────────────────────────────────

        public override void OnActionReceived(ActionBuffers actions)
        {
            if (!_initialized) return;
            if (_state != ShipState.Navigating) return;

            // 타임아웃 체크
            _episodeTimer += Time.fixedDeltaTime;
            if (_episodeTimer >= maxEpisodeTime)
            {
                HandleDeath(false, -0.1f);
                return;
            }

            float throttle = actions.ContinuousActions[0];
            float steering = actions.ContinuousActions[1];

            float depthRatio  = _depthRatio;
            float widthRatio  = _widthRatio;
            float healthRatio = _currentHealth / stats.maxHealth;

            float speed    = stats.GetEffectiveSpeed(depthRatio, healthRatio);
            float turnRate = stats.GetEffectiveTurnRate(widthRatio);

            float clampedThrottle = (throttle + 1f) * 0.5f;
            _rb.linearVelocity = Vector3.Lerp(
                _rb.linearVelocity,
                transform.forward * clampedThrottle * speed,
                Time.fixedDeltaTime * 5f);
            transform.Rotate(Vector3.up, steering * turnRate * 45f * Time.fixedDeltaTime);

            AddReward(-0.0002f);

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

        // ── 사망 처리 ──────────────────────────────────────────────────────

        /// <summary>
        /// 충돌·타임아웃·목표 도달 모두 이 메서드를 통해 처리한다.
        /// 빨간색(실패) 또는 금색(성공)으로 변경 후 제자리 동결,
        /// GenerationManager에 보고, 세대 리셋을 기다린다.
        /// </summary>
        void HandleDeath(bool reachedGoal, float reward)
        {
            if (_state != ShipState.Navigating) return;

            _state = reachedGoal ? ShipState.Success : ShipState.Crashed;
            SetReward(reward);

            _waitingForGenReset = true;
            SetMarkerColor(reachedGoal ? ColorSuccess : ColorDead);

            _rb.linearVelocity  = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;

            generationManager?.ReportEpisodeEnd(reachedGoal, reward);
            EndEpisode();   // ML-Agents가 경험 데이터를 처리하도록 호출
                            // → OnEpisodeBegin() 자동 호출되지만 _waitingForGenReset=true 이므로 동결
        }

        /// <summary>GenerationManager가 세대 종료 시 호출한다.</summary>
        public void NotifyGenerationReset()
        {
            _waitingForGenReset = false;
            SetMarkerColor(ColorAlive);
            EndEpisode();   // → OnEpisodeBegin() 정상 실행 → 재스폰
        }

        // ── Collision / Trigger ───────────────────────────────────────────

        void OnTriggerEnter(Collider other)
        {
            if (other.CompareTag("Goal"))
                HandleDeath(true, 1f);
        }

        void OnCollisionEnter(Collision collision)
        {
            bool isBoundary = collision.gameObject.CompareTag("Boundary");
            bool isTerrain  = collision.gameObject.layer == LayerMask.NameToLayer("Terrain");

            if (isBoundary || isTerrain)
                HandleDeath(false, -0.5f);
        }

        // ── 시각 헬퍼 ─────────────────────────────────────────────────────

        void SetMarkerColor(Color c)
        {
            if (_markerMat  != null) _markerMat.color  = c;
            if (_trailMat   != null) _trailMat.color   = c;
        }

        // ── Sensing helpers ───────────────────────────────────────────────

        float ObstacleRay(Vector3 direction)
        {
            return Physics.Raycast(transform.position, direction, out RaycastHit hit, raycastMaxDist, sensorLayerMask)
                ? 1f - (hit.distance / raycastMaxDist)
                : 0f;
        }

        float RaycastDistance(Vector3 direction)
        {
            return Physics.Raycast(transform.position, direction, out RaycastHit hit, raycastMaxDist, sensorLayerMask)
                ? hit.distance
                : raycastMaxDist;
        }

        float GetDepthRatio()
        {
            if (!Physics.Raycast(transform.position, Vector3.down, out RaycastHit hit, deepThreshold + 100f, sensorLayerMask))
                return 1f;
            float depth = hit.distance;
            if (depth <= shallowThreshold) return 0f;
            if (depth >= deepThreshold)    return 1f;
            return (depth - shallowThreshold) / (deepThreshold - shallowThreshold);
        }

        float GetWidthRatio()
        {
            float left  = RaycastDistance(-transform.right);
            float right = RaycastDistance( transform.right);
            return Mathf.Clamp01((left + right) * 0.5f / narrowThreshold);
        }
    }
}
