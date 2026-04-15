// UnityProject/Assets/Scripts/Editor/HormuzTrainingSetup.cs
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using HormuzAI.Data;
using HormuzAI.Environment;

namespace HormuzAI.Editor
{
    /// <summary>
    /// 씬에 GenerationManager / AgentPopulator를 자동으로 추가하고
    /// Inspector 레퍼런스를 모두 할당한다.
    /// 메뉴: Hormuz > Setup Parallel Training
    /// </summary>
    public static class HormuzTrainingSetup
    {
        private const string STATS_ASSET_PATH = "Assets/Data/Ships/KoreaShipStats.asset";

        [MenuItem("Hormuz/Setup Parallel Training")]
        public static void SetupParallelTraining()
        {
            // ── 1. ShipStatsSO 에셋 확인 / 생성 ─────────────────────────────
            var stats = AssetDatabase.LoadAssetAtPath<ShipStatsSO>(STATS_ASSET_PATH);
            if (stats == null)
            {
                System.IO.Directory.CreateDirectory(
                    System.IO.Path.Combine(Application.dataPath, "Data/Ships"));
                stats = ScriptableObject.CreateInstance<ShipStatsSO>();
                AssetDatabase.CreateAsset(stats, STATS_ASSET_PATH);
                AssetDatabase.SaveAssets();
                Debug.Log($"[HormuzTrainingSetup] KoreaShipStats.asset 생성: {STATS_ASSET_PATH}");
            }

            // ── 2. 씬에서 SpawnPoints / GoalTrigger 찾기 ───────────────────
            var spawnPointsGO = GameObject.Find("SpawnPoints");
            if (spawnPointsGO == null)
            {
                Debug.LogError("[HormuzTrainingSetup] 'SpawnPoints' GameObject를 씬에서 찾지 못했습니다. " +
                               "Hormuz > Build Scene을 먼저 실행하세요.");
                return;
            }
            var spawnManager = spawnPointsGO.GetComponent<SpawnManager>();
            if (spawnManager == null)
            {
                Debug.LogError("[HormuzTrainingSetup] SpawnPoints에 SpawnManager 컴포넌트가 없습니다.");
                return;
            }

            var goalGO = GameObject.Find("GoalTrigger");
            if (goalGO == null)
            {
                Debug.LogError("[HormuzTrainingSetup] 'GoalTrigger' GameObject를 씬에서 찾지 못했습니다.");
                return;
            }

            // ── 3. GenerationManager GO 생성 (이미 있으면 재사용) ───────────
            var genMgrGO = GameObject.Find("GenerationManager");
            if (genMgrGO == null)
            {
                genMgrGO = new GameObject("GenerationManager");
                Undo.RegisterCreatedObjectUndo(genMgrGO, "Create GenerationManager");
            }
            var genMgr = genMgrGO.GetComponent<GenerationManager>();
            if (genMgr == null)
                genMgr = Undo.AddComponent<GenerationManager>(genMgrGO);

            // agentsPerGeneration = 50
            var genMgrSO = new SerializedObject(genMgr);
            genMgrSO.FindProperty("agentsPerGeneration").intValue = 50;
            genMgrSO.ApplyModifiedProperties();

            // ── 4. AgentPopulator GO 생성 (이미 있으면 재사용) ──────────────
            var populatorGO = GameObject.Find("AgentPopulator");
            if (populatorGO == null)
            {
                populatorGO = new GameObject("AgentPopulator");
                Undo.RegisterCreatedObjectUndo(populatorGO, "Create AgentPopulator");
            }
            var populator = populatorGO.GetComponent<AgentPopulator>();
            if (populator == null)
                populator = Undo.AddComponent<AgentPopulator>(populatorGO);

            // Inspector 레퍼런스 할당
            var popSO = new SerializedObject(populator);
            popSO.FindProperty("agentCount").intValue        = 50;
            popSO.FindProperty("stats").objectReferenceValue = stats;
            popSO.FindProperty("spawnManager").objectReferenceValue = spawnManager;
            popSO.FindProperty("goal").objectReferenceValue         = goalGO.transform;
            popSO.FindProperty("generationManager").objectReferenceValue = genMgr;
            popSO.ApplyModifiedProperties();

            // ── 5. 개요 카메라 설정 (2560×1440 전체 씬 조망) ──────────────
            // 지형: X=0~56000, Z=0~40000 / 직교 카메라 정중앙 하향
            var cam = Camera.main;
            if (cam != null)
            {
                Undo.RecordObject(cam,               "Setup Overview Camera");
                Undo.RecordObject(cam.transform,     "Setup Overview Camera Transform");
                cam.orthographic     = true;
                cam.orthographicSize = 22000f;   // 세로 44km — 지형 40km 커버
                cam.farClipPlane     = 15000f;
                cam.transform.SetPositionAndRotation(
                    new Vector3(28000f, 12000f, 20000f),
                    Quaternion.Euler(90f, 0f, 0f));  // 정수직 하향
                Debug.Log("[HormuzTrainingSetup] 카메라: 직교 22000, 정수직 하향 설정 완료.");
            }
            else
            {
                Debug.LogWarning("[HormuzTrainingSetup] Main Camera를 찾지 못했습니다. 카메라를 수동으로 설정하세요.");
            }

            // ── 6. 씬 저장 ─────────────────────────────────────────────────
            EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();

            Debug.Log("[HormuzTrainingSetup] 완료. Play를 눌러 학습을 시작하세요.");
        }
    }
}
