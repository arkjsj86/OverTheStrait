// UnityProject/Assets/Scripts/Environment/AgentPopulator.cs
using UnityEngine;
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
            for (int i = 0; i < agentCount; i++)
            {
                var go = new GameObject($"ShipAgent_{i:D3}");
                go.transform.SetParent(transform);

                // Rigidbody 먼저 — useGravity=false
                // ShipAgent.Initialize()는 constraints만 설정하므로 여기서 처리
                var rb = go.AddComponent<Rigidbody>();
                rb.useGravity = false;

                // 함선 형태 근사 Collider (Z축 방향)
                var col       = go.AddComponent<CapsuleCollider>();
                col.radius    = 15f;
                col.height    = 60f;
                col.direction = 2;

                // ShipAgent — ML-Agents가 Initialize()를 자동 호출
                // SetRefs는 즉시 호출되므로 첫 에피소드 시작 전에 모든 레퍼런스 할당됨
                var agent = go.AddComponent<ShipAgent>();
                agent.SetRefs(spawnManager, goal, stats, generationManager);
            }
        }
    }
}
