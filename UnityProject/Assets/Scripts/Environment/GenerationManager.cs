// UnityProject/Assets/Scripts/Environment/GenerationManager.cs
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using HormuzAI.Agent;

namespace HormuzAI.Environment
{
    [Serializable]
    public class TrainingRuntimeStatus
    {
        public int currentGeneration;
        public int episodesCompletedThisGeneration;
        public int totalEpisodesThisGeneration;
        public int goalReachedThisGeneration;
        public int collisionThisGeneration;
        public int timeoutThisGeneration;
        public float generationElapsedRealSeconds;
        public float maxEpisodeGameSeconds;
        public float maxEpisodeRealSeconds;
        public float timeScale;
        public string lastUpdateLocal;
        public TrainingCompletedGenerationSnapshot lastCompletedGeneration;
        public TrainingBestEpisodeSnapshot runBestEpisode;
    }

    [Serializable]
    public class TrainingCompletedGenerationSnapshot
    {
        public int generation;
        public float bestReward;
        public float avgReward;
        public int goalReached;
        public int collisionCount;
        public int timeoutCount;
        public int total;
        public string bestAgent;
        public string bestEndReason;
        public string completedAtLocal;
    }

    [Serializable]
    public class TrainingBestEpisodeSnapshot
    {
        public int generation;
        public float score;
        public string agentName;
        public string endReason;
    }

    /// <summary>
    /// 에이전트 에피소드 결과를 세대 단위로 집계하고 CSV로 기록한다.
    ///
    /// 세대 정의: 등록된 모든 에이전트가 에피소드를 완료한 시점.
    /// 에이전트는 사망 즉시 개별 재스폰되며, 동결 없이 학습이 연속된다.
    ///
    /// CSV 위치: {ProjectRoot}/logs/training_history.csv
    /// </summary>
    public class GenerationManager : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField] int   agentsPerGeneration    = 50;
        [SerializeField] float timeScale              = 10f;
        [SerializeField] float maxEpisodeGameSeconds  = 180f;  // ShipAgent.maxEpisodeTime 과 맞춤
        [SerializeField] bool  useStage1BootstrapDefaults = true;

        // ── 에이전트 등록 ──────────────────────────────────────────────────
        readonly List<ShipAgent> _registeredAgents = new List<ShipAgent>();
        int   _aliveThisGen;
        float _genElapsed;   // 세대 경과 실제 시간 (unscaled)

        // ── 집계 상태 ─────────────────────────────────────────────────────
        int   _currentGeneration;
        int   _completedThisGen;
        float _bestReward;
        string _bestAgentThisGen;
        EpisodeEndReason _bestEndReasonThisGen;
        float _totalReward;
        int   _goalReachedCount;
        int   _collisionCountThisGen;
        int   _timeoutCountThisGen;

        // ── 런 전체 최고 기록 ─────────────────────────────────────────────
        bool   _hasBestEpisode;
        float  _bestEpisodeScore;
        int    _bestEpisodeGeneration;
        string _bestEpisodeAgentName;
        EpisodeEndReason _bestEpisodeEndReason;

        // ── CSV 경로 ──────────────────────────────────────────────────────
        string _csvPath;
        string _statusPath;
        float _nextStatusSnapshotAt;
        TrainingCompletedGenerationSnapshot _lastCompletedSnapshot;

        // ── Unity 생명주기 ─────────────────────────────────────────────────

        // Awake() 사용: AgentPopulator.Start()보다 먼저 실행되어
        // _aliveThisGen 초기화를 보장한다 (Start() 실행 순서 의존성 제거).
        void Awake()
        {
            ApplyBootstrapDefaults();
            Application.runInBackground = true;

            string logsDir = Path.GetFullPath(
                Path.Combine(Application.dataPath, "..", "..", "logs"));
            Directory.CreateDirectory(logsDir);
            _csvPath = Path.Combine(logsDir, "training_history.csv");
            _statusPath = Path.Combine(logsDir, "training_runtime_status.json");

            _currentGeneration = LoadCurrentGeneration();
            _aliveThisGen = agentsPerGeneration;   // RegisterAgent 전 안전 초기값
            _nextStatusSnapshotAt = Time.realtimeSinceStartup + 1f;

            Debug.Log($"[GenerationManager] 세대 {_currentGeneration}부터 시작. timeScale={timeScale}x");
            WriteStatusSnapshot();
        }

        void Start()
        {
            // Awake()에서 등록된 에이전트 수로 재설정 (RegisterAgent가 Awake 이후 호출된 경우)
            InitGeneration();
        }

        void Update()
        {
            _genElapsed += Time.unscaledDeltaTime;
            if (Time.realtimeSinceStartup >= _nextStatusSnapshotAt)
            {
                WriteStatusSnapshot();
                _nextStatusSnapshotAt = Time.realtimeSinceStartup + 1f;
            }
        }

        // ── HUD용 공개 프로퍼티 ────────────────────────────────────────────
        public int   CurrentGeneration => _currentGeneration;
        public float GenElapsed        => _genElapsed;
        public float MaxRealSeconds    => maxEpisodeGameSeconds / Mathf.Max(1f, timeScale);
        public bool   HasBestEpisode         => _hasBestEpisode;
        public float  BestEpisodeScore       => _hasBestEpisode ? _bestEpisodeScore : 0f;
        public int    BestEpisodeGeneration  => _bestEpisodeGeneration;
        public string BestEpisodeAgentName   => _hasBestEpisode ? _bestEpisodeAgentName : "-";
        public string BestEpisodeEndReason   => _hasBestEpisode ? FormatEndReason(_bestEpisodeEndReason) : "-";

        // ── 외부 API ───────────────────────────────────────────────────────

        /// <summary>AgentPopulator가 에이전트 생성 시 호출한다.</summary>
        public void RegisterAgent(ShipAgent agent)
        {
            if (!_registeredAgents.Contains(agent))
                _registeredAgents.Add(agent);
        }

        /// <summary>ShipAgent가 에피소드 종료 시 호출한다.</summary>
        public void ReportEpisodeEnd(string agentName, EpisodeEndReason endReason, float episodeScore)
        {
            bool reachedGoal = endReason == EpisodeEndReason.GoalReached;
            _completedThisGen++;
            _totalReward += episodeScore;
            if (reachedGoal) _goalReachedCount++;
            else if (endReason == EpisodeEndReason.Collision) _collisionCountThisGen++;
            else _timeoutCountThisGen++;
            if (episodeScore > _bestReward)
            {
                _bestReward = episodeScore;
                _bestAgentThisGen = agentName;
                _bestEndReasonThisGen = endReason;
            }

            if (!_hasBestEpisode || episodeScore > _bestEpisodeScore)
            {
                _hasBestEpisode        = true;
                _bestEpisodeScore      = episodeScore;
                _bestEpisodeGeneration = _currentGeneration;
                _bestEpisodeAgentName  = agentName;
                _bestEpisodeEndReason  = endReason;

                Debug.Log(
                    $"[Run Best] Gen {_bestEpisodeGeneration:D4} | {agentName} | " +
                    $"Score {_bestEpisodeScore:F3} | {FormatEndReason(endReason)}");
            }

            WriteStatusSnapshot();
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
            _bestAgentThisGen = "-";
            _bestEndReasonThisGen = EpisodeEndReason.Timeout;
            _totalReward      = 0f;
            _goalReachedCount = 0;
            _collisionCountThisGen = 0;
            _timeoutCountThisGen = 0;
            _genElapsed       = 0f;
            // 등록된 에이전트 수 우선, 없으면 Inspector 값 사용
            _aliveThisGen = _registeredAgents.Count > 0
                ? _registeredAgents.Count
                : agentsPerGeneration;
        }

        void EndGeneration()
        {
            float avg = _completedThisGen > 0 ? _totalReward / _completedThisGen : 0f;
            string completedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            Debug.Log(
                $"[Gen {_currentGeneration:D4}] Best: {_bestReward:F3} " +
                $"({_bestAgentThisGen}, {FormatEndReason(_bestEndReasonThisGen)}) | " +
                $"Avg: {avg:F3} | Goal: {_goalReachedCount}/{_completedThisGen}");
            AppendCsv(_currentGeneration, _bestReward, avg, _goalReachedCount, _completedThisGen);

            _lastCompletedSnapshot = new TrainingCompletedGenerationSnapshot
            {
                generation = _currentGeneration,
                bestReward = _bestReward,
                avgReward = avg,
                goalReached = _goalReachedCount,
                collisionCount = _collisionCountThisGen,
                timeoutCount = _timeoutCountThisGen,
                total = _completedThisGen,
                bestAgent = _bestAgentThisGen,
                bestEndReason = FormatEndReason(_bestEndReasonThisGen),
                completedAtLocal = completedAt
            };

            _currentGeneration++;
            InitGeneration();
            WriteStatusSnapshot();
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

        static string FormatEndReason(EpisodeEndReason endReason)
        {
            switch (endReason)
            {
                case EpisodeEndReason.GoalReached:
                    return "Goal";
                case EpisodeEndReason.Collision:
                    return "Collision";
                default:
                    return "Timeout";
            }
        }

        void ApplyBootstrapDefaults()
        {
            if (!useStage1BootstrapDefaults) return;

            timeScale = Mathf.Max(timeScale, 10f);
            maxEpisodeGameSeconds = Mathf.Max(maxEpisodeGameSeconds, 180f);
            agentsPerGeneration = Mathf.Max(agentsPerGeneration, 50);
        }

        void WriteStatusSnapshot()
        {
            if (string.IsNullOrEmpty(_statusPath)) return;

            var status = new TrainingRuntimeStatus
            {
                currentGeneration = _currentGeneration,
                episodesCompletedThisGeneration = _completedThisGen,
                totalEpisodesThisGeneration = _aliveThisGen > 0
                    ? _completedThisGen + _aliveThisGen
                    : Mathf.Max(_completedThisGen, agentsPerGeneration),
                goalReachedThisGeneration = _goalReachedCount,
                collisionThisGeneration = _collisionCountThisGen,
                timeoutThisGeneration = _timeoutCountThisGen,
                generationElapsedRealSeconds = _genElapsed,
                maxEpisodeGameSeconds = maxEpisodeGameSeconds,
                maxEpisodeRealSeconds = MaxRealSeconds,
                timeScale = timeScale,
                lastUpdateLocal = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                lastCompletedGeneration = _lastCompletedSnapshot,
                runBestEpisode = _hasBestEpisode
                    ? new TrainingBestEpisodeSnapshot
                    {
                        generation = _bestEpisodeGeneration,
                        score = _bestEpisodeScore,
                        agentName = _bestEpisodeAgentName,
                        endReason = FormatEndReason(_bestEpisodeEndReason)
                    }
                    : null
            };

            File.WriteAllText(_statusPath, JsonUtility.ToJson(status, true));
        }
    }
}
