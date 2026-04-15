// UnityProject/Assets/Scripts/Environment/GenerationManager.cs
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using HormuzAI.Agent;

namespace HormuzAI.Environment
{
    /// <summary>
    /// 에이전트 에피소드 결과를 세대 단위로 집계하고 CSV로 기록한다.
    ///
    /// 세대 정의: 등록된 모든 에이전트가 사망(충돌/타임아웃) 또는 목표 도달로 종료된 시점.
    /// 모든 에이전트가 동시에 리셋되며, 각 에이전트는 사망 후 빨간색으로 대기한다.
    ///
    /// CSV 위치: {ProjectRoot}/logs/training_history.csv
    /// </summary>
    public class GenerationManager : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField] int   agentsPerGeneration = 50;
        [SerializeField] float timeScale           = 20f;

        // ── 에이전트 등록 ──────────────────────────────────────────────────
        readonly List<ShipAgent> _registeredAgents = new List<ShipAgent>();
        int _aliveThisGen;

        // ── 집계 상태 ─────────────────────────────────────────────────────
        int   _currentGeneration;
        int   _completedThisGen;
        float _bestReward;
        float _totalReward;
        int   _goalReachedCount;

        // ── CSV 경로 ──────────────────────────────────────────────────────
        string _csvPath;

        // ── Unity 생명주기 ─────────────────────────────────────────────────

        void Start()
        {
            Application.runInBackground = true;
            Time.timeScale = timeScale;

            string logsDir = Path.GetFullPath(
                Path.Combine(Application.dataPath, "..", "..", "logs"));
            Directory.CreateDirectory(logsDir);
            _csvPath = Path.Combine(logsDir, "training_history.csv");

            _currentGeneration = LoadCurrentGeneration();
            InitGeneration();

            Debug.Log($"[GenerationManager] 세대 {_currentGeneration}부터 시작. timeScale={timeScale}x");
        }

        // ── 외부 API ───────────────────────────────────────────────────────

        /// <summary>AgentPopulator가 에이전트 생성 시 호출한다.</summary>
        public void RegisterAgent(ShipAgent agent)
        {
            if (!_registeredAgents.Contains(agent))
                _registeredAgents.Add(agent);
        }

        /// <summary>ShipAgent가 에피소드 종료 시 호출한다.</summary>
        public void ReportEpisodeEnd(bool reachedGoal, float reward)
        {
            _completedThisGen++;
            _totalReward += reward;
            if (reachedGoal) _goalReachedCount++;
            if (reward > _bestReward) _bestReward = reward;

            _aliveThisGen--;
            if (_aliveThisGen <= 0)
                EndGeneration();
        }

        // ── 내부 메서드 ────────────────────────────────────────────────────

        int LoadCurrentGeneration()
        {
            if (!File.Exists(_csvPath)) return 1;
            var lines = File.ReadAllLines(_csvPath);
            int dataLines = Mathf.Max(0, lines.Length - 1);
            return dataLines + 1;
        }

        void InitGeneration()
        {
            _completedThisGen = 0;
            _bestReward       = float.MinValue;
            _totalReward      = 0f;
            _goalReachedCount = 0;
            // 등록된 에이전트 수 우선, 없으면 Inspector 값 사용
            _aliveThisGen = _registeredAgents.Count > 0
                ? _registeredAgents.Count
                : agentsPerGeneration;
        }

        void EndGeneration()
        {
            float avg  = _completedThisGen > 0 ? _totalReward / _completedThisGen : 0f;
            int   gen  = _currentGeneration;
            int   tot  = _completedThisGen;

            Debug.Log($"[Gen {gen:D4}] Best: {_bestReward:F3} | Avg: {avg:F3} | Goal: {_goalReachedCount}/{tot}");
            AppendCsv(gen, _bestReward, avg, _goalReachedCount, tot);

            _currentGeneration++;
            InitGeneration();

            // 모든 에이전트 동시 리셋
            foreach (var agent in _registeredAgents)
                agent.NotifyGenerationReset();
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
