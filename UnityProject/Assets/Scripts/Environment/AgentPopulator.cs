// UnityProject/Assets/Scripts/Environment/AgentPopulator.cs
using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Policies;
using HormuzAI.Agent;
using HormuzAI.Data;

namespace HormuzAI.Environment
{
    /// <summary>
    /// 씬 시작 시 N개의 ShipAgent를 동적으로 생성한다.
    /// Inspector에서 agentCount, stats, spawnManager, goal, generationManager를
    /// 할당 후 Play하면 에이전트가 자동 배치되어 ML-Agents 학습이 시작된다.
    ///
    /// agentsPerGeneration(GenerationManager) = agentCount 로 일치시킬 것.
    /// </summary>
    public class AgentPopulator : MonoBehaviour
    {
        [Header("Agent Settings")]
        [SerializeField] int         agentCount = 50;
        [SerializeField] ShipStatsSO stats;

        [Header("Scene References")]
        [SerializeField] SpawnManager      spawnManager;
        [SerializeField] Transform         goal;
        [SerializeField] GenerationManager generationManager;

        void Start() => SpawnAgents();

        /// <summary>
        /// agentCount 만큼 ShipAgent GameObject를 생성한다.
        /// 테스트에서 직접 호출 가능 (Start() 대신 사용).
        /// </summary>
        public void SpawnAgents()
        {
            if (stats == null || spawnManager == null || goal == null || generationManager == null)
            {
                Debug.LogError("[AgentPopulator] Inspector references not fully assigned. Aborting spawn.", this);
                return;
            }

            if (transform.childCount > 0)
            {
                Debug.LogWarning("[AgentPopulator] SpawnAgents() called but children already exist. Skipping.", this);
                return;
            }

            // Shader.Find은 전체 셰이더 목록을 순회 — 루프 밖에서 한 번만 호출
            var unlitShader = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlitShader == null)
            {
                Debug.LogError("[AgentPopulator] URP Unlit 셰이더를 찾지 못했습니다. URP 패키지가 설치되어 있는지 확인하세요.", this);
                return;
            }

            for (int i = 0; i < agentCount; i++)
            {
                var go = new GameObject($"ShipAgent_{i:D3}");
                go.transform.SetParent(transform);

                // ★ 비활성 상태로 시작 — Agent.OnEnable()이 기본 BrainParameters(0,0)로
                //   Policy를 고정 생성하는 것을 막는다. 모든 설정 후 마지막에 활성화.
                go.SetActive(false);

                // Rigidbody 먼저 — useGravity=false
                var rb = go.AddComponent<Rigidbody>();
                rb.useGravity = false;
                rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

                // 함선 형태 근사 Collider (Z축 방향)
                var col       = go.AddComponent<CapsuleCollider>();
                col.radius    = 15f;
                col.height    = 60f;
                col.direction = 2;

                // BehaviorParameters — ShipAgent 추가 전에 명시적으로 먼저 추가하고 설정.
                // (ShipAgent는 RequireComponent로 BehaviorParameters를 자동 추가하지만,
                //  그 시점엔 BrainParameters가 기본값이라 Policy가 잘못된 spec으로 생성된다.)
                var bp = go.AddComponent<BehaviorParameters>();
                bp.BehaviorName = "HormuzShip";
                // ShipAgent.CollectObservations는 9개 float을 추가한다.
                bp.BrainParameters.VectorObservationSize = 13;
                // ShipAgent.OnActionReceived는 actions.ContinuousActions[0,1]을 읽는다 (throttle, steering).
                bp.BrainParameters.ActionSpec = ActionSpec.MakeContinuous(2);

                // ShipAgent — 위에서 설정한 BrainParameters로 Policy가 생성된다.
                var agent = go.AddComponent<ShipAgent>();
                agent.SetRefs(spawnManager, goal, stats, generationManager);
                generationManager?.RegisterAgent(agent);   // 세대 관리자에 등록

                // DecisionRequester — 없으면 OnActionReceived가 한 번도 호출되지 않아
                // 배가 움직이지 않고 per-step 보상도 누적되지 않는다.
                // DecisionPeriod=5 → 0.02s × 5 = 0.1s마다 결정 (180초 에피소드 = 1800 결정).
                var dr = go.AddComponent<DecisionRequester>();
                dr.DecisionPeriod = 5;
                dr.TakeActionsBetweenDecisions = true;

                // 타임아웃은 ShipAgent 내부 타이머로 처리 → MaxStep 불필요
                agent.MaxStep = 0;

                // ── 방향 화살표 마커 (상공 카메라에서 전진 방향 식별용) ──────
                var marker = new GameObject("Marker");
                marker.transform.SetParent(go.transform);
                marker.transform.localPosition = Vector3.zero;
                marker.transform.localRotation = Quaternion.identity;

                var arrowMesh = new Mesh { name = "Arrow" };
                // 위에서 볼 때 +Z(전방)를 가리키는 삼각형, Y=10 오프셋
                arrowMesh.vertices  = new Vector3[]
                {
                    new Vector3(   0f, 10f, 100f),   // 앞 꼭짓점
                    new Vector3( -30f, 10f, -60f),   // 뒤 왼쪽
                    new Vector3(  30f, 10f, -60f),   // 뒤 오른쪽
                };
                arrowMesh.triangles = new int[] { 0, 2, 1 };
                arrowMesh.RecalculateNormals();

                var mf  = marker.AddComponent<MeshFilter>();
                var mr  = marker.AddComponent<MeshRenderer>();
                mf.sharedMesh = arrowMesh;

                var mat = new Material(unlitShader);
                mat.color = new Color(0.1f, 1f, 0.35f);
                mr.sharedMaterial = mat;

                // ★ 모든 컴포넌트 설정 완료 후 활성화 — Agent.OnEnable()이 이제 호출되며
                //   올바른 BrainParameters(obs=9, continuous=2)로 Policy를 생성한다.
                go.SetActive(true);
            }
        }
    }
}
