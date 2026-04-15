// UnityProject/Assets/Scripts/Environment/GenerationManager.cs
using System;
using System.IO;
using UnityEngine;

namespace HormuzAI.Environment
{
    /// <summary>
    /// 에이전트 에피소드 결과를 세대 단위로 집계하고 CSV로 기록한다.
    ///
    /// 세대 정의: agentsPerGeneration 개의 에피소드가 완료된 시점.
    /// CSV 위치: {ProjectRoot}/logs/training_history.csv
    ///   (Application.dataPath 기준 ../../logs/)
    ///
    /// 재실행 시 CSV 데이터 행 수를 읽어 세대 번호를 이어받는다.
    /// </summary>
    public class GenerationManager : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField] int agentsPerGeneration = 50;

        // ── 집계 상태 ─────────────────────────────────────────────────────
        int   _currentGeneration;
        int   _completedThisGen;
        float _bestReward;
        float _totalReward;
        int   _goalReachedCount;

        // ── CSV 경로 (테스트에서 reflection으로 주입 가능) ─────────────────
        string _csvPath;

        // ── Unity 생명주기 ─────────────────────────────────────────────────

        void Start()
        {
            // 포커스를 잃어도 학습이 계속 진행되도록 설정
            Application.runInBackground = true;

            // {UnityProject}/Assets/../../logs/ = {ProjectRoot}/logs/
            string logsDir = Path.GetFullPath(
                Path.Combine(Application.dataPath, "..", "..", "logs"));
            Directory.CreateDirectory(logsDir);
            _csvPath = Path.Combine(logsDir, "training_history.csv");

            _currentGeneration = LoadCurrentGeneration();
            InitGeneration();

            Debug.Log($"[GenerationManager] 세대 {_currentGeneration}부터 시작. 기록: {_csvPath}");
        }

        // ── 외부 API ───────────────────────────────────────────────────────

        /// <summary>
        /// ShipAgent가 에피소드 종료 시 호출한다.
        /// </summary>
        /// <param name="reachedGoal">GoalTrigger 도달 여부</param>
        /// <param name="reward">에피소드 결과 보상 (1f / -0.5f / -0.1f)</param>
        public void ReportEpisodeEnd(bool reachedGoal, float reward)
        {
            _completedThisGen++;
            _totalReward += reward;
            if (reachedGoal) _goalReachedCount++;
            if (reward > _bestReward) _bestReward = reward;

            if (_completedThisGen >= agentsPerGeneration)
                EndGeneration();
        }

        // ── 내부 메서드 ────────────────────────────────────────────────────

        int LoadCurrentGeneration()
        {
            if (!File.Exists(_csvPath)) return 1;
            var lines = File.ReadAllLines(_csvPath);
            // lines[0] = 헤더, lines[1..] = 데이터
            int dataLines = Mathf.Max(0, lines.Length - 1);
            return dataLines + 1;
        }

        private void InitGeneration()
        {
            _completedThisGen = 0;
            _bestReward       = float.MinValue;
            _totalReward      = 0f;
            _goalReachedCount = 0;
        }

        void EndGeneration()
        {
            float avg  = _totalReward / _completedThisGen;
            int   goal = _goalReachedCount;
            int   gen  = _currentGeneration;
            int   tot  = agentsPerGeneration;

            Debug.Log($"[Gen {gen:D4}] Best: {_bestReward:F3} | Avg: {avg:F3} | Goal: {goal}/{tot}");

            AppendCsv(gen, _bestReward, avg, goal, tot);

            _currentGeneration++;
            InitGeneration();
        }

        void AppendCsv(int gen, float best, float avg, int goal, int total)
        {
            bool needsHeader = !File.Exists(_csvPath) ||
                               new FileInfo(_csvPath).Length == 0;

            using var writer = new StreamWriter(_csvPath, append: true,
                                                encoding: System.Text.Encoding.UTF8);
            if (needsHeader)
                writer.WriteLine("Generation,BestReward,AvgReward,GoalReached,Total,Timestamp");

            writer.WriteLine(
                $"{gen},{best:F4},{avg:F4},{goal},{total}," +
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        }
    }
}
