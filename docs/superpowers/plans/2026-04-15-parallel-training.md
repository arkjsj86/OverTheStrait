# Parallel Training System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 단일 Unity 씬에서 ShipAgent 50개를 반경 500m 내 무작위 위치에서 동시 학습시키며, GenerationManager가 세대별 성과(Best/Avg/Goal 도달)를 Console 출력 + CSV 저장으로 가시화한다.

**Architecture:** SpawnManager에 랜덤 스폰 추가 → ShipAgent에 SetRefs(gm 포함) + 랜덤 스폰 적용 → GenerationManager가 에피소드 결과를 수집해 세대 집계 → AgentPopulator가 Start()에서 N개 에이전트를 생성하며 모든 레퍼런스를 주입. YAML은 50 에이전트 병렬 학습에 최적화.

**Tech Stack:** Unity 6, C#, Unity ML-Agents release_20, NUnit (Unity Test Runner Edit Mode), System.IO (CSV)

---

## 파일 구조

| 동작 | 경로 |
|------|------|
| Modify | `UnityProject/Assets/Scripts/Environment/SpawnManager.cs` |
| Modify | `UnityProject/Assets/Scripts/Agent/ShipAgent.cs` |
| Create | `UnityProject/Assets/Scripts/Environment/GenerationManager.cs` |
| Create | `UnityProject/Assets/Scripts/Environment/AgentPopulator.cs` |
| Modify | `config/hormuz_stage1.yaml` |
| Create | `UnityProject/Assets/Tests/EditMode/SpawnManagerTests.cs` |
| Create | `UnityProject/Assets/Tests/EditMode/ShipAgentSetRefsTests.cs` |
| Create | `UnityProject/Assets/Tests/EditMode/GenerationManagerTests.cs` |
| Create | `UnityProject/Assets/Tests/EditMode/AgentPopulatorTests.cs` |

---

### Task 1: SpawnManager — 랜덤 스폰 메서드

**Files:**
- Modify: `UnityProject/Assets/Scripts/Environment/SpawnManager.cs`
- Create: `UnityProject/Assets/Tests/EditMode/SpawnManagerTests.cs`

- [ ] **Step 1: 테스트 파일 생성**

`UnityProject/Assets/Tests/EditMode/SpawnManagerTests.cs`:

```csharp
using NUnit.Framework;
using UnityEngine;
using HormuzAI.Environment;

public class SpawnManagerTests
{
    GameObject _root;

    [SetUp]
    public void SetUp() => _root = new GameObject("Root");

    [TearDown]
    public void TearDown() => Object.DestroyImmediate(_root);

    SpawnManager CreateSpawnManager(Vector3 center, float radius = 500f)
    {
        var child = new GameObject("SP0");
        child.transform.SetParent(_root.transform);
        child.transform.position = center;

        var sm = _root.AddComponent<SpawnManager>();
        typeof(SpawnManager)
            .GetField("spawnRadius",
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance)
            .SetValue(sm, radius);
        return sm;
    }

    [Test]
    public void GetRandomSpawnPosition_ReturnsWithinRadius()
    {
        var center = new Vector3(1000f, 10.68f, 39000f);
        var sm = CreateSpawnManager(center, 500f);

        for (int i = 0; i < 50; i++)
        {
            Vector3 pos = sm.GetRandomSpawnPosition();
            float xzDist = Vector2.Distance(
                new Vector2(pos.x, pos.z),
                new Vector2(center.x, center.z));
            Assert.LessOrEqual(xzDist, 500f + 0.01f,
                $"Iteration {i}: 반경 초과 ({xzDist:F2}m)");
        }
    }

    [Test]
    public void GetRandomSpawnPosition_PreservesYCoordinate()
    {
        var center = new Vector3(1000f, 10.68f, 39000f);
        var sm = CreateSpawnManager(center);

        for (int i = 0; i < 10; i++)
        {
            Vector3 pos = sm.GetRandomSpawnPosition();
            Assert.AreEqual(center.y, pos.y, 0.0001f,
                $"Iteration {i}: Y 좌표 변경됨");
        }
    }

    [Test]
    public void GetRandomSpawnPosition_FallsBackToTransformWhenNoChildren()
    {
        _root.transform.position = new Vector3(500f, 5f, 1000f);
        var sm = _root.AddComponent<SpawnManager>();
        typeof(SpawnManager)
            .GetField("spawnRadius",
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance)
            .SetValue(sm, 100f);

        Vector3 pos = sm.GetRandomSpawnPosition();
        float xzDist = Vector2.Distance(
            new Vector2(pos.x, pos.z),
            new Vector2(500f, 1000f));
        Assert.LessOrEqual(xzDist, 100f + 0.01f);
    }
}
```

- [ ] **Step 2: Unity Test Runner에서 실패 확인**

`Window > General > Test Runner > EditMode`.  
`SpawnManagerTests` 선택 → Run Selected.  
예상: `GetRandomSpawnPosition` 없음으로 컴파일 오류.

- [ ] **Step 3: SpawnManager 전체 교체**

`UnityProject/Assets/Scripts/Environment/SpawnManager.cs`:

```csharp
// UnityProject/Assets/Scripts/Environment/SpawnManager.cs
using UnityEngine;

namespace HormuzAI.Environment
{
    /// <summary>
    /// SpawnPoints 오브젝트의 자식 Transform 을 스폰 위치로 관리한다.
    /// Awake 에서 자식을 자동 수집하므로 Inspector 할당 불필요.
    /// 멀티 에이전트 레이싱을 위해 인덱스 순환을 지원한다.
    /// </summary>
    public class SpawnManager : MonoBehaviour
    {
        [SerializeField] float spawnRadius = 500f;

        private Transform[] _spawnPoints;

        private void Awake()
        {
            _spawnPoints = new Transform[transform.childCount];
            for (int i = 0; i < transform.childCount; i++)
                _spawnPoints[i] = transform.GetChild(i);
        }

        /// <summary>index 번째 스폰 포인트를 반환한다 (범위 초과 시 순환).</summary>
        public Transform GetSpawnPoint(int index = 0)
        {
            if (_spawnPoints == null || _spawnPoints.Length == 0)
                return transform;
            return _spawnPoints[index % _spawnPoints.Length];
        }

        /// <summary>
        /// 첫 번째 스폰 포인트를 중심으로 spawnRadius 반경 내 무작위 XZ 위치를 반환한다.
        /// Y 좌표는 스폰 포인트와 동일 (해수면 높이 유지).
        /// 스폰 포인트가 없으면 transform.position 을 중심으로 사용한다.
        /// </summary>
        public Vector3 GetRandomSpawnPosition()
        {
            Vector3 center = (_spawnPoints != null && _spawnPoints.Length > 0)
                ? _spawnPoints[0].position
                : transform.position;

            Vector2 circle = Random.insideUnitCircle * spawnRadius;
            return center + new Vector3(circle.x, 0f, circle.y);
        }

        /// <summary>등록된 스폰 포인트 수.</summary>
        public int SpawnCount => _spawnPoints?.Length ?? 0;
    }
}
```

- [ ] **Step 4: 테스트 통과 확인**

Test Runner에서 `SpawnManagerTests` 실행.  
예상: 3개 모두 통과.

- [ ] **Step 5: 커밋**

```bash
git add UnityProject/Assets/Scripts/Environment/SpawnManager.cs \
        UnityProject/Assets/Tests/EditMode/SpawnManagerTests.cs
git commit -m "feat: add random spawn radius to SpawnManager"
```

---

### Task 2: GenerationManager — 세대 추적 + CSV 기록

**Files:**
- Create: `UnityProject/Assets/Scripts/Environment/GenerationManager.cs`
- Create: `UnityProject/Assets/Tests/EditMode/GenerationManagerTests.cs`

- [ ] **Step 1: 테스트 파일 생성**

`UnityProject/Assets/Tests/EditMode/GenerationManagerTests.cs`:

```csharp
using NUnit.Framework;
using System.IO;
using System.Reflection;
using UnityEngine;
using HormuzAI.Environment;

public class GenerationManagerTests
{
    GameObject _go;
    string _tmpCsv;

    [SetUp]
    public void SetUp()
    {
        _go     = new GameObject("GM");
        _tmpCsv = Path.Combine(Path.GetTempPath(), $"test_gen_{System.Guid.NewGuid()}.csv");
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(_go);
        if (File.Exists(_tmpCsv)) File.Delete(_tmpCsv);
    }

    GenerationManager CreateGM(int agentsPerGen, string csvPath = null)
    {
        var gm   = _go.AddComponent<GenerationManager>();
        var bind = BindingFlags.NonPublic | BindingFlags.Instance;
        var t    = typeof(GenerationManager);
        t.GetField("agentsPerGeneration", bind).SetValue(gm, agentsPerGen);
        t.GetField("_csvPath",            bind).SetValue(gm, csvPath ?? _tmpCsv);
        t.GetField("_currentGeneration",  bind).SetValue(gm, 1);
        // InitGeneration 호출로 집계 변수 초기화
        t.GetMethod("InitGeneration", bind).Invoke(gm, null);
        return gm;
    }

    [Test]
    public void ReportEpisodeEnd_CountsGoalReached()
    {
        var gm = CreateGM(3);
        gm.ReportEpisodeEnd(true,  1f);
        gm.ReportEpisodeEnd(false, -0.5f);
        gm.ReportEpisodeEnd(false, -0.1f);

        var bind = BindingFlags.NonPublic | BindingFlags.Instance;
        int goalCount = (int)typeof(GenerationManager)
            .GetField("_goalReachedCount", bind).GetValue(gm);
        Assert.AreEqual(1, goalCount);
    }

    [Test]
    public void ReportEpisodeEnd_TracksBestReward()
    {
        var gm = CreateGM(3);
        gm.ReportEpisodeEnd(false, -0.5f);
        gm.ReportEpisodeEnd(true,   1f);
        gm.ReportEpisodeEnd(false, -0.1f);

        var bind = BindingFlags.NonPublic | BindingFlags.Instance;
        float best = (float)typeof(GenerationManager)
            .GetField("_bestReward", bind).GetValue(gm);
        Assert.AreEqual(1f, best, 0.0001f);
    }

    [Test]
    public void EndGeneration_WritesCSVRow()
    {
        var gm = CreateGM(2, _tmpCsv);
        gm.ReportEpisodeEnd(true,  1f);
        gm.ReportEpisodeEnd(false, -0.5f);
        // agentsPerGeneration=2 이므로 EndGeneration 자동 호출됨

        Assert.IsTrue(File.Exists(_tmpCsv), "CSV 파일이 생성되지 않음");
        var lines = File.ReadAllLines(_tmpCsv);
        Assert.GreaterOrEqual(lines.Length, 2, "헤더 + 데이터 행 최소 2줄 필요");
        StringAssert.Contains("1,", lines[1], "세대 번호 1이 포함되어야 함");
    }

    [Test]
    public void EndGeneration_IncrementsGenerationNumber()
    {
        var gm = CreateGM(2, _tmpCsv);
        gm.ReportEpisodeEnd(true,  1f);
        gm.ReportEpisodeEnd(false, -0.5f);

        var bind = BindingFlags.NonPublic | BindingFlags.Instance;
        int gen = (int)typeof(GenerationManager)
            .GetField("_currentGeneration", bind).GetValue(gm);
        Assert.AreEqual(2, gen);
    }
}
```

- [ ] **Step 2: Unity Test Runner에서 실패 확인**

Test Runner에서 `GenerationManagerTests` 실행.  
예상: `GenerationManager` 클래스 없음으로 컴파일 오류.

- [ ] **Step 3: GenerationManager 구현**

`UnityProject/Assets/Scripts/Environment/GenerationManager.cs`:

```csharp
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

        void InitGeneration()
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
```

- [ ] **Step 4: 테스트 통과 확인**

Test Runner에서 `GenerationManagerTests` 실행.  
예상: 4개 모두 통과.

- [ ] **Step 5: 커밋**

```bash
git add UnityProject/Assets/Scripts/Environment/GenerationManager.cs \
        UnityProject/Assets/Tests/EditMode/GenerationManagerTests.cs
git commit -m "feat: add GenerationManager for per-generation stats and CSV logging"
```

---

### Task 3: ShipAgent — spawnIndex 제거 + SetRefs(gm 포함) + 랜덤 스폰 + 결과 보고

**Files:**
- Modify: `UnityProject/Assets/Scripts/Agent/ShipAgent.cs`
- Create: `UnityProject/Assets/Tests/EditMode/ShipAgentSetRefsTests.cs`

- [ ] **Step 1: 테스트 파일 생성**

`UnityProject/Assets/Tests/EditMode/ShipAgentSetRefsTests.cs`:

```csharp
using NUnit.Framework;
using System.Reflection;
using UnityEngine;
using HormuzAI.Agent;
using HormuzAI.Data;
using HormuzAI.Environment;

public class ShipAgentSetRefsTests
{
    GameObject _agentGO;
    GameObject _smGO;
    GameObject _goalGO;
    GameObject _gmGO;

    [SetUp]
    public void SetUp()
    {
        _agentGO = new GameObject("Agent");
        _agentGO.AddComponent<Rigidbody>();

        _smGO = new GameObject("SpawnManager");
        new GameObject("SP0").transform.SetParent(_smGO.transform);

        _goalGO = new GameObject("Goal");
        _gmGO   = new GameObject("GM");
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(_agentGO);
        Object.DestroyImmediate(_smGO);
        Object.DestroyImmediate(_goalGO);
        Object.DestroyImmediate(_gmGO);
    }

    static object GetField(object obj, string name)
    {
        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        return obj.GetType().GetField(name, flags)?.GetValue(obj);
    }

    [Test]
    public void SetRefs_AssignsSpawnManager()
    {
        var agent = _agentGO.AddComponent<ShipAgent>();
        var sm    = _smGO.AddComponent<SpawnManager>();
        var stats = ScriptableObject.CreateInstance<ShipStatsSO>();
        var gm    = _gmGO.AddComponent<GenerationManager>();

        agent.SetRefs(sm, _goalGO.transform, stats, gm);

        Assert.AreEqual(sm, GetField(agent, "spawnManager"));
        Object.DestroyImmediate(stats);
    }

    [Test]
    public void SetRefs_AssignsGoal()
    {
        var agent = _agentGO.AddComponent<ShipAgent>();
        var sm    = _smGO.AddComponent<SpawnManager>();
        var stats = ScriptableObject.CreateInstance<ShipStatsSO>();
        var gm    = _gmGO.AddComponent<GenerationManager>();

        agent.SetRefs(sm, _goalGO.transform, stats, gm);

        Assert.AreEqual(_goalGO.transform, GetField(agent, "goal"));
        Object.DestroyImmediate(stats);
    }

    [Test]
    public void SetRefs_AssignsGenerationManager()
    {
        var agent = _agentGO.AddComponent<ShipAgent>();
        var sm    = _smGO.AddComponent<SpawnManager>();
        var stats = ScriptableObject.CreateInstance<ShipStatsSO>();
        var gm    = _gmGO.AddComponent<GenerationManager>();

        agent.SetRefs(sm, _goalGO.transform, stats, gm);

        Assert.AreEqual(gm, GetField(agent, "generationManager"));
        Object.DestroyImmediate(stats);
    }
}
```

- [ ] **Step 2: Unity Test Runner에서 실패 확인**

Test Runner에서 `ShipAgentSetRefsTests` 실행.  
예상: `SetRefs(sm, goal, stats, gm)` 시그니처 없음으로 컴파일 오류.

- [ ] **Step 3: ShipAgent — spawnIndex 필드 삭제**

`UnityProject/Assets/Scripts/Agent/ShipAgent.cs:22` 에서 아래 줄 삭제:

```csharp
[SerializeField] int spawnIndex = 0;
```

- [ ] **Step 4: ShipAgent — generationManager 필드 추가**

`[Header("Scene References")]` 블록에 아래 줄 추가:

```csharp
[SerializeField] GenerationManager generationManager;
```

using 문 상단에 추가 (없으면):

```csharp
using HormuzAI.Environment;
```

- [ ] **Step 5: ShipAgent — Initialize() stats 가드 제거**

`Initialize()` 전체를 아래로 교체:

```csharp
public override void Initialize()
{
    _rb = GetComponent<Rigidbody>();
    _rb.constraints = RigidbodyConstraints.FreezePositionY
                    | RigidbodyConstraints.FreezeRotationX
                    | RigidbodyConstraints.FreezeRotationZ;
    _state = ShipState.Idle;
    _initialized = true;
}
```

- [ ] **Step 6: ShipAgent — SetRefs() 추가 (generationManager 포함)**

`Initialize()` 바로 위에 추가:

```csharp
/// <summary>AgentPopulator 등 런타임 생성기가 레퍼런스를 주입할 때 사용한다.</summary>
public void SetRefs(SpawnManager sm, Transform g, ShipStatsSO s, GenerationManager gm)
{
    spawnManager      = sm;
    goal              = g;
    stats             = s;
    generationManager = gm;
}
```

- [ ] **Step 7: ShipAgent — OnEpisodeBegin() 랜덤 스폰 + 타임아웃 보고로 교체**

`OnEpisodeBegin()` 전체를 아래로 교체:

```csharp
public override void OnEpisodeBegin()
{
    if (!_initialized) return;
    if (stats == null)
    {
        Debug.LogError($"[ShipAgent] stats not assigned on '{name}'.", this);
        return;
    }

    // 이전 에피소드가 타임아웃(MaxStep)으로 종료된 경우
    if (_state == ShipState.Navigating)
    {
        AddReward(-0.1f);
        generationManager?.ReportEpisodeEnd(false, -0.1f);
    }

    _currentHealth = stats.maxHealth;
    _state         = ShipState.Navigating;

    Vector3    spawnPos = spawnManager != null
        ? spawnManager.GetRandomSpawnPosition()
        : transform.position;
    Quaternion spawnRot = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
    transform.SetPositionAndRotation(spawnPos, spawnRot);

    _rb.linearVelocity  = Vector3.zero;
    _rb.angularVelocity = Vector3.zero;

    if (goal == null)
        Debug.LogWarning($"[ShipAgent] goal is not assigned on '{name}'.", this);

    _prevDistToGoal = goal != null
        ? Vector3.Distance(transform.position, goal.position)
        : 0f;
}
```

- [ ] **Step 8: ShipAgent — OnTriggerEnter() Goal 보고 추가**

`OnTriggerEnter` 내 Goal 처리 블록을 아래로 교체:

```csharp
if (other.CompareTag("Goal"))
{
    _state = ShipState.Success;
    SetReward(1f);
    generationManager?.ReportEpisodeEnd(true, 1f);
    EndEpisode();
}
```

- [ ] **Step 9: ShipAgent — OnCollisionEnter() Crash 보고 추가**

`OnCollisionEnter` 내 Boundary/Terrain 처리 블록을 아래로 교체:

```csharp
if (isBoundary || isTerrain)
{
    _state = ShipState.Crashed;
    SetReward(-0.5f);
    generationManager?.ReportEpisodeEnd(false, -0.5f);
    EndEpisode();
}
```

- [ ] **Step 10: 테스트 통과 확인**

Test Runner에서 `ShipAgentSetRefsTests` + 기존 `ShipStatsSO_Tests` 실행.  
예상: ShipAgentSetRefsTests 3개 + 기존 11개 이상 모두 통과.

- [ ] **Step 11: 커밋**

```bash
git add UnityProject/Assets/Scripts/Agent/ShipAgent.cs \
        UnityProject/Assets/Tests/EditMode/ShipAgentSetRefsTests.cs
git commit -m "feat: add GenerationManager reporting and random spawn to ShipAgent"
```

---

### Task 4: AgentPopulator — 런타임 N-에이전트 생성기

**Files:**
- Create: `UnityProject/Assets/Scripts/Environment/AgentPopulator.cs`
- Create: `UnityProject/Assets/Tests/EditMode/AgentPopulatorTests.cs`

- [ ] **Step 1: 테스트 파일 생성**

`UnityProject/Assets/Tests/EditMode/AgentPopulatorTests.cs`:

```csharp
using NUnit.Framework;
using System.Reflection;
using UnityEngine;
using HormuzAI.Agent;
using HormuzAI.Data;
using HormuzAI.Environment;

public class AgentPopulatorTests
{
    GameObject _root;
    ShipStatsSO _stats;

    [SetUp]
    public void SetUp()
    {
        _root  = new GameObject("Root");
        _stats = ScriptableObject.CreateInstance<ShipStatsSO>();
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(_root);
        Object.DestroyImmediate(_stats);
    }

    AgentPopulator CreatePopulator(int count)
    {
        var smGO = new GameObject("SpawnManager");
        smGO.transform.SetParent(_root.transform);
        var sp = new GameObject("SP0");
        sp.transform.SetParent(smGO.transform);
        sp.transform.position = new Vector3(1000f, 10.68f, 39000f);
        var sm = smGO.AddComponent<SpawnManager>();

        var goalGO = new GameObject("Goal");
        goalGO.transform.SetParent(_root.transform);

        var gmGO = new GameObject("GM");
        gmGO.transform.SetParent(_root.transform);
        var gm = gmGO.AddComponent<GenerationManager>();

        var popGO = new GameObject("AgentPopulator");
        popGO.transform.SetParent(_root.transform);
        var pop = popGO.AddComponent<AgentPopulator>();

        var bind = BindingFlags.NonPublic | BindingFlags.Instance;
        var t    = typeof(AgentPopulator);
        t.GetField("agentCount",        bind).SetValue(pop, count);
        t.GetField("stats",             bind).SetValue(pop, _stats);
        t.GetField("spawnManager",      bind).SetValue(pop, sm);
        t.GetField("goal",              bind).SetValue(pop, goalGO.transform);
        t.GetField("generationManager", bind).SetValue(pop, gm);

        pop.SpawnAgents();
        return pop;
    }

    [Test]
    public void SpawnAgents_CreatesCorrectChildCount()
    {
        var pop = CreatePopulator(5);
        Assert.AreEqual(5, pop.transform.childCount);
    }

    [Test]
    public void SpawnAgents_EachChildHasShipAgent()
    {
        var pop = CreatePopulator(3);
        for (int i = 0; i < pop.transform.childCount; i++)
            Assert.IsNotNull(pop.transform.GetChild(i).GetComponent<ShipAgent>(),
                $"Child {i} has no ShipAgent");
    }

    [Test]
    public void SpawnAgents_EachChildHasRigidbodyWithNoGravity()
    {
        var pop = CreatePopulator(3);
        for (int i = 0; i < pop.transform.childCount; i++)
        {
            var rb = pop.transform.GetChild(i).GetComponent<Rigidbody>();
            Assert.IsNotNull(rb, $"Child {i} has no Rigidbody");
            Assert.IsFalse(rb.useGravity, $"Child {i} useGravity should be false");
        }
    }
}
```

- [ ] **Step 2: Unity Test Runner에서 실패 확인**

Test Runner에서 `AgentPopulatorTests` 실행.  
예상: `AgentPopulator` 없음으로 컴파일 오류.

- [ ] **Step 3: AgentPopulator 구현**

`UnityProject/Assets/Scripts/Environment/AgentPopulator.cs`:

```csharp
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
        [SerializeField] SpawnManager     spawnManager;
        [SerializeField] Transform        goal;
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
```

- [ ] **Step 4: 전체 테스트 통과 확인**

Test Runner에서 `Run All` 실행.  
예상: SpawnManagerTests 3 + GenerationManagerTests 4 + ShipAgentSetRefsTests 3 + AgentPopulatorTests 3 + ShipStatsSO_Tests 11 = **24개 이상 모두 통과**.

- [ ] **Step 5: 커밋**

```bash
git add UnityProject/Assets/Scripts/Environment/AgentPopulator.cs \
        UnityProject/Assets/Tests/EditMode/AgentPopulatorTests.cs
git commit -m "feat: add AgentPopulator with GenerationManager injection"
```

---

### Task 5: YAML — 병렬 학습 파라미터 조정

**Files:**
- Modify: `config/hormuz_stage1.yaml`

- [ ] **Step 1: YAML 전체 교체**

`config/hormuz_stage1.yaml`:

```yaml
# Contract: Unity BehaviorParameters must match exactly
#   Behavior Name                : HormuzShip
#   Vector Observation Space Size: 9
#   Continuous Actions           : 2
#   (See ShipAgent.cs CollectObservations for observation layout)
#
# 병렬 학습 설정 기준: agentCount=50, --num-envs=1
#   --num-envs=5 추가 시 실질 250 에이전트 병렬
#
# 학습 실행 명령:
#   mlagents-learn config/hormuz_stage1.yaml --run-id=hormuz_run1
#   mlagents-learn config/hormuz_stage1.yaml --run-id=hormuz_run1 --num-envs=5
behaviors:
  HormuzShip:
    trainer_type: ppo
    hyperparameters:
      batch_size: 1024
      buffer_size: 10240
      learning_rate: 3.0e-4
      beta: 0.005
      epsilon: 0.2
      lambd: 0.95
      num_epoch: 3
    network_settings:
      normalize: true
      hidden_units: 128
      num_layers: 2
    reward_signals:
      extrinsic:
        gamma: 0.95
        strength: 1.0
    max_steps: 5000000
    time_horizon: 64
    summary_freq: 10000
    checkpoint_interval: 50000
    keep_checkpoints: 5
```

- [ ] **Step 2: 커밋**

```bash
git add config/hormuz_stage1.yaml
git commit -m "config: tune yaml for 50-agent parallel training"
```

---

### Task 6: Play Mode 씬 설정 및 검증

Unity Editor에서 수동 진행.

- [ ] **Step 1: HormuzStage1 씬 열기**

`Assets/Scenes/HormuzStage1.unity` 더블클릭. 씬 없으면 `Hormuz > Build Scene` 실행.

- [ ] **Step 2: GenerationManager 오브젝트 추가**

Hierarchy 우클릭 → Create Empty → 이름 `GenerationManager`.  
Inspector → Add Component → `GenerationManager`.  
`Agents Per Generation` = `50`.

- [ ] **Step 3: AgentPopulator 오브젝트 추가**

Hierarchy 우클릭 → Create Empty → 이름 `AgentPopulator`.  
Inspector → Add Component → `AgentPopulator`.

| 필드 | 할당 |
|------|------|
| Agent Count | `50` |
| Stats | `Assets/Data/Ships/KoreaShipStats.asset` |
| Spawn Manager | Hierarchy의 `HormuzScene/SpawnPoints` 드래그 |
| Goal | Hierarchy의 `HormuzScene/GoalTrigger` 드래그 |
| Generation Manager | Hierarchy의 `GenerationManager` 드래그 |

- [ ] **Step 4: Play 버튼으로 검증**

Play 클릭 후 확인:
- Hierarchy에 `AgentPopulator/ShipAgent_000` ~ `ShipAgent_049` 50개 생성됨
- 에이전트들이 SpawnPoint 반경 500m 내 각기 다른 위치/방향에서 전진함
- Console 오류 없음 (`[ShipAgent] stats not assigned` 미출력)
- 에피소드 50개 완료 후 Console에 아래 형태 로그 출력됨:
  ```
  [Gen 0001] Best: -0.100 | Avg: -0.098 | Goal: 0/50
  ```
- `{ProjectRoot}/logs/training_history.csv` 파일 생성 및 데이터 기록됨

- [ ] **Step 5: 씬 저장 후 커밋**

`Ctrl+S`.

```bash
git add UnityProject/Assets/Scenes/HormuzStage1.unity
git commit -m "scene: add GenerationManager and AgentPopulator to HormuzStage1"
```
