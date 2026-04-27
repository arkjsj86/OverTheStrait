// UnityProject/Assets/Scripts/Agent/ShipAgent.cs
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;
using HormuzAI.Data;
using HormuzAI.Environment;

namespace HormuzAI.Agent
{
    public enum EpisodeEndReason { GoalReached, Collision, Timeout }
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
        [SerializeField] float maxEpisodeTime = 180f;
        [SerializeField] bool  startFacingGoal = true;
        [SerializeField] float startHeadingJitterDegrees = 45f;

        [Header("Sensing Thresholds")]
        [SerializeField] float shallowThreshold = 500f;
        [SerializeField] float deepThreshold    = 1500f;
        [SerializeField] float narrowThreshold  = 3000f;
        [SerializeField] float raycastMaxDist   = 750f;
        [SerializeField] LayerMask sensorLayerMask = Physics.DefaultRaycastLayers;

        [Header("Terrain Collision")]
        [SerializeField] LayerMask terrainLayerMask;
        [SerializeField] LayerMask waterLayerMask;

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
        float        _episodeStartTime;     // Time.time 기준 — Python 연결 없이도 타임아웃 동작
        int          _episodeCount;         // OnEpisodeBegin 호출 횟수 (첫 번째 에피소드 식별용)
        float        _firstEpisodeOffset;   // 첫 에피소드만 적용되는 무작위 시간 오프셋 (동시 사망 방지)
        bool         _gate1Passed;
        bool         _gate2Passed;
        Coroutine    _stuckDetection;
        float        _currentSteering;
        Material     _markerMat;

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
            _episodeCount = 0;
            // 첫 에피소드 타임아웃에 무작위 오프셋 적용 → 50개 에이전트가 동시에 죽지 않도록 분산
            _firstEpisodeOffset = Random.Range(0f, maxEpisodeTime * 0.9f);

            if (terrainLayerMask == 0)
                terrainLayerMask = LayerMask.GetMask("Terrain");
            if (waterLayerMask == 0)
                waterLayerMask = LayerMask.GetMask("Water");

            _initialized = true;

            // 마커 / 트레일은 SetMarkerColor에서 lazy init (AgentPopulator가 Marker를 나중에 추가하므로)
        }

        // void Update() — 타임아웃 비활성화: 속도 대비 맵이 너무 넓어 제한 시간 내 도달 불가

        public override void OnEpisodeBegin()
        {
            if (!_initialized) return;

            if (stats == null)
            {
                Debug.LogError($"[ShipAgent] stats not assigned on '{name}'.", this);
                _state = ShipState.Idle;
                return;
            }

            _episodeCount++;
            _currentHealth    = stats.maxHealth;
            _state            = ShipState.Navigating;
            _episodeStartTime = Time.time;
            SetMarkerColor(ColorAlive);

            Vector3    spawnPos = spawnManager != null
                ? spawnManager.GetRandomSpawnPosition()
                : transform.position;
            Quaternion spawnRot = GetSpawnRotation(spawnPos);
            transform.SetPositionAndRotation(spawnPos, spawnRot);

            _rb.linearVelocity  = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;

            _prevDistToGoal = goal != null
                ? Vector3.Distance(transform.position, goal.position)
                : 0f;

            _gate1Passed     = false;
            _gate2Passed     = false;
            _currentSteering = 0f;

            if (_stuckDetection != null) StopCoroutine(_stuckDetection);
            _stuckDetection = StartCoroutine(StuckDetectionLoop());
        }

        System.Collections.IEnumerator StuckDetectionLoop()
        {
            const float checkInterval  = 2f;   // 게임 시간 기준 — GC 정지 중 흐르지 않아 오판 없음
            const float minMovement    = 100f; // 속도 3000 기준 2게임초 이동량(6000)의 1.7% — 충분히 보수적
            const float warmupSeconds  = 3f;

            yield return new WaitForSeconds(warmupSeconds);
            while (_state == ShipState.Navigating)
            {
                Vector3 posBefore = transform.position;
                yield return new WaitForSeconds(checkInterval);
                if (_state != ShipState.Navigating) yield break;
                if (Vector3.Distance(posBefore, transform.position) < minMovement)
                    HandleDeath(EpisodeEndReason.Collision, -5.0f);
            }
        }

        // ── Observations (13개: ray×7 + goal_angle + velocity + depth + width + health + shipType) ──

        public override void CollectObservations(VectorSensor sensor)
        {
            // 비활성 상태일 때는 0으로 채워 관측 크기 유지
            if (!_initialized || _state == ShipState.Crashed || _state == ShipState.Success)
            {
                for (int i = 0; i < 13; i++) sensor.AddObservation(0f);
                return;
            }

            sensor.AddObservation(ObstacleRay(Quaternion.Euler(0, -90, 0) * transform.forward));
            sensor.AddObservation(ObstacleRay(Quaternion.Euler(0, -60, 0) * transform.forward));
            sensor.AddObservation(ObstacleRay(Quaternion.Euler(0, -30, 0) * transform.forward));
            sensor.AddObservation(ObstacleRay(transform.forward));
            sensor.AddObservation(ObstacleRay(Quaternion.Euler(0,  30, 0) * transform.forward));
            sensor.AddObservation(ObstacleRay(Quaternion.Euler(0,  60, 0) * transform.forward));
            sensor.AddObservation(ObstacleRay(Quaternion.Euler(0,  90, 0) * transform.forward));

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

            // IsOnIsland() — 절벽형 지형으로 OnCollisionEnter가 작동하므로 비활성
            // if (IsOnIsland()) { HandleDeath(EpisodeEndReason.Collision, -5.0f); return; }

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
            _currentSteering = Mathf.Lerp(_currentSteering, steering, Time.fixedDeltaTime * 3f);
            transform.Rotate(Vector3.up, _currentSteering * turnRate * 45f * Time.fixedDeltaTime);

            // ── 매 스텝 보상 ──────────────────────────────────────────────
            // 1. 생존 패널티 — "정지 = local optimum" 탈출 (9000스텝 누적 -9)
            AddReward(-0.001f);

            // 2. 목표 접근 보상 — 거리 차분 (최대 전진 시 스텝당 +0.05, 신호/노이즈 50×)
            if (goal != null)
            {
                float dist = Vector3.Distance(transform.position, goal.position);
                AddReward((_prevDistToGoal - dist) * 0.0002f);
                _prevDistToGoal = dist;
            }

            // 3. 목표 방향 정렬 보상 — 목표를 정면으로 향할수록 최대
            if (goal != null)
            {
                Vector3 toGoal = (goal.position - transform.position).normalized;
                float   angle  = Vector3.SignedAngle(transform.forward, toGoal, Vector3.up);
                AddReward(0.0005f * (1f - Mathf.Abs(angle) / 180f));
            }

            // 4. 수심 품질 보상 — 깊은 수로 선호 유도 (Stage 2 암초 회피 기반 학습)
            AddReward(0.0001f * _depthRatio);

            // 5. 수로폭 품질 보상 — 넓은 곳 선호 유도
            AddReward(0.00005f * _widthRatio);
        }

        // public override void Heuristic(in ActionBuffers actionsOut)
        // {
        //     var ca = actionsOut.ContinuousActions;
        //     ca[0] = Input.GetAxis("Vertical");
        //     ca[1] = Input.GetAxis("Horizontal");
        // }

        // ── 사망 처리 ──────────────────────────────────────────────────────

        /// <summary>
        /// 충돌·타임아웃·목표 도달 모두 이 메서드를 통해 처리한다.
        /// GenerationManager에 보고한 뒤 EndEpisode() → ML-Agents가 즉시 재스폰.
        /// </summary>
        void HandleDeath(EpisodeEndReason endReason, float terminalReward)
        {
            if (_state != ShipState.Navigating) return;

            bool reachedGoal = endReason == EpisodeEndReason.GoalReached;
            _state = reachedGoal ? ShipState.Success : ShipState.Crashed;
            if (_stuckDetection != null) { StopCoroutine(_stuckDetection); _stuckDetection = null; }
            AddReward(terminalReward);
            SetMarkerColor(reachedGoal ? ColorSuccess : ColorDead);

            _rb.linearVelocity  = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;

            float episodeScore = GetCumulativeReward();
            generationManager?.ReportEpisodeEnd(GetEpisodeLabel(), endReason, episodeScore);
            StartCoroutine(RespawnAfterDelay());
        }

        System.Collections.IEnumerator RespawnAfterDelay()
        {
            yield return new WaitForSecondsRealtime(1f);
            EndEpisode();
        }

        // ── Collision / Trigger ───────────────────────────────────────────

        void OnCollisionEnter(Collision collision)
        {
            if (collision.gameObject.CompareTag("Boundary"))
            {
                HandleDeath(EpisodeEndReason.Collision, -5.0f);
                return;
            }
            if (collision.gameObject.layer == LayerMask.NameToLayer("Terrain"))
                HandleDeath(EpisodeEndReason.Collision, -5.0f);
        }

        void OnTriggerEnter(Collider other)
        {
            if (other.CompareTag("Goal"))
            {
                HandleDeath(EpisodeEndReason.GoalReached, 50f);
                return;
            }
            if (other.CompareTag("Gate1") && !_gate1Passed)
            {
                _gate1Passed = true;
                AddReward(5f);
                return;
            }
            if (other.CompareTag("Gate2") && !_gate2Passed)
            {
                _gate2Passed = true;
                AddReward(5f);
            }
        }

        // ── 시각 헬퍼 ─────────────────────────────────────────────────────

        void SetMarkerColor(Color c)
        {
            // Lazy init — AgentPopulator.Start()가 AddComponent 후 Marker를 추가하므로
            // Initialize() 시점에는 아직 자식이 없다.
            if (_markerMat == null)
            {
                var markerGO = transform.Find("Marker");
                if (markerGO != null)
                {
                    var mr = markerGO.GetComponent<MeshRenderer>();
                    if (mr != null) _markerMat = mr.material;
                }
            }
            if (_markerMat != null) _markerMat.color = c;
        }

        // ── Sensing helpers ───────────────────────────────────────────────

        bool IsOnIsland()
        {
            if (terrainLayerMask == 0 || waterLayerMask == 0) return false;
            var origin = new Vector3(transform.position.x, transform.position.y + 2000f, transform.position.z);
            int combinedMask = terrainLayerMask | waterLayerMask;
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 4000f, combinedMask))
                return (terrainLayerMask & (1 << hit.collider.gameObject.layer)) != 0;
            return false;
        }

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

        string GetEpisodeLabel() => $"{name} [{shipType}]";

        Quaternion GetSpawnRotation(Vector3 spawnPos)
        {
            if (!startFacingGoal || goal == null)
                return Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

            Vector3 flatToGoal = goal.position - spawnPos;
            flatToGoal.y = 0f;
            if (flatToGoal.sqrMagnitude < 0.001f)
                return Quaternion.identity;

            float jitter = Mathf.Clamp(startHeadingJitterDegrees, 0f, 180f);
            float yawOffset = Random.Range(-jitter, jitter);
            return Quaternion.LookRotation(flatToGoal.normalized, Vector3.up) *
                   Quaternion.Euler(0f, yawOffset, 0f);
        }
    }
}
