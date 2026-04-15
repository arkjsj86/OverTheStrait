# Parallel Training System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 단일 Unity 씬에서 ShipAgent 50개(기본값)를 SpawnPoint 반경 500m 내 무작위 위치/방향에서 동시 학습시킨다.

**Architecture:** SpawnManager에 `GetRandomSpawnPosition()`을 추가하고, ShipAgent에서 고정 `spawnIndex`를 제거해 에피소드마다 랜덤 스폰하도록 변경한다. `AgentPopulator` MonoBehaviour가 `Start()`에서 N개 ShipAgent를 동적 생성하며, YAML 파라미터를 50 에이전트 병렬 학습에 맞게 조정한다.

**Tech Stack:** Unity 6, C#, Unity ML-Agents release_20, NUnit (Unity Test Runner Edit Mode)

---

## 파일 구조

| 동작 | 경로 |
|------|------|
| Modify | `UnityProject/Assets/Scripts/Environment/SpawnManager.cs` |
| Modify | `UnityProject/Assets/Scripts/Agent/ShipAgent.cs` |
| Create | `UnityProject/Assets/Scripts/Environment/AgentPopulator.cs` |
| Modify | `config/hormuz_stage1.yaml` |
| Create | `UnityProject/Assets/Tests/EditMode/SpawnManagerTests.cs` |
| Create | `UnityProject/Assets/Tests/EditMode/ShipAgentSetRefsTests.cs` |
| Create | `UnityProject/Assets/Tests/EditMode/AgentPopulatorTests.cs` |

---

### Task 1: SpawnManager — 랜덤 스폰 메서드

**Files:**
- Modify: `UnityProject/Assets/Scripts/Environment/SpawnManager.cs`
- Create: `UnityProject/Assets/Tests/EditMode/SpawnManagerTests.cs`

- [ ] **Step 1: 테스트 파일 생성**

`UnityProject/Assets/Tests/EditMode/SpawnManagerTests.cs` 를 아래 내용으로 생성:

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

`Window > General > Test Runner > EditMode` 탭.  
`SpawnManagerTests` 선택 후 Run Selected.  
예상: `GetRandomSpawnPosition` 메서드 없음으로 컴파일 오류 또는 실패.

- [ ] **Step 3: SpawnManager 수정**

`UnityProject/Assets/Scripts/Environment/SpawnManager.cs` 전체를 아래로 교체:

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

- [ ] **Step 4: Unity Test Runner에서 통과 확인**

Test Runner에서 `SpawnManagerTests` 실행.  
예상: 3개 테스트 모두 통과 (녹색).

- [ ] **Step 5: 커밋**

```bash
git add UnityProject/Assets/Scripts/Environment/SpawnManager.cs \
        UnityProject/Assets/Tests/EditMode/SpawnManagerTests.cs
git commit -m "feat: add random spawn radius to SpawnManager"
```

---

### Task 2: ShipAgent — spawnIndex 제거 + SetRefs + 랜덤 스폰

**Files:**
- Modify: `UnityProject/Assets/Scripts/Agent/ShipAgent.cs`
- Create: `UnityProject/Assets/Tests/EditMode/ShipAgentSetRefsTests.cs`

- [ ] **Step 1: 테스트 파일 생성**

`UnityProject/Assets/Tests/EditMode/ShipAgentSetRefsTests.cs` 를 아래 내용으로 생성:

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

    [SetUp]
    public void SetUp()
    {
        _agentGO = new GameObject("Agent");
        _agentGO.AddComponent<Rigidbody>();

        _smGO = new GameObject("SpawnManager");
        var child = new GameObject("SP0");
        child.transform.SetParent(_smGO.transform);

        _goalGO = new GameObject("Goal");
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(_agentGO);
        Object.DestroyImmediate(_smGO);
        Object.DestroyImmediate(_goalGO);
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

        agent.SetRefs(sm, _goalGO.transform, stats);

        Assert.AreEqual(sm, GetField(agent, "spawnManager"));
        Object.DestroyImmediate(stats);
    }

    [Test]
    public void SetRefs_AssignsGoal()
    {
        var agent = _agentGO.AddComponent<ShipAgent>();
        var sm    = _smGO.AddComponent<SpawnManager>();
        var stats = ScriptableObject.CreateInstance<ShipStatsSO>();

        agent.SetRefs(sm, _goalGO.transform, stats);

        Assert.AreEqual(_goalGO.transform, GetField(agent, "goal"));
        Object.DestroyImmediate(stats);
    }

    [Test]
    public void SetRefs_AssignsStats()
    {
        var agent = _agentGO.AddComponent<ShipAgent>();
        var sm    = _smGO.AddComponent<SpawnManager>();
        var stats = ScriptableObject.CreateInstance<ShipStatsSO>();

        agent.SetRefs(sm, _goalGO.transform, stats);

        Assert.AreEqual(stats, GetField(agent, "stats"));
        Object.DestroyImmediate(stats);
    }
}
```

- [ ] **Step 2: Unity Test Runner에서 실패 확인**

Test Runner에서 `ShipAgentSetRefsTests` 실행.  
예상: `SetRefs` 메서드 없음으로 컴파일 오류.

- [ ] **Step 3: ShipAgent 수정 — spawnIndex 제거**

`UnityProject/Assets/Scripts/Agent/ShipAgent.cs:22` 에서 아래 줄 삭제:

```csharp
[SerializeField] int spawnIndex = 0;
```

- [ ] **Step 4: ShipAgent 수정 — Initialize() stats 가드 제거**

`UnityProject/Assets/Scripts/Agent/ShipAgent.cs`의 `Initialize()` 를 아래로 교체:

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

- [ ] **Step 5: ShipAgent 수정 — OnEpisodeBegin() 랜덤 스폰으로 교체**

`UnityProject/Assets/Scripts/Agent/ShipAgent.cs`의 `OnEpisodeBegin()` 를 아래로 교체:

```csharp
public override void OnEpisodeBegin()
{
    if (!_initialized) return;
    if (stats == null)
    {
        Debug.LogError($"[ShipAgent] stats not assigned on '{name}'.", this);
        return;
    }

    if (_state == ShipState.Navigating)
        AddReward(-0.1f);

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

- [ ] **Step 6: ShipAgent 수정 — SetRefs() 추가**

`OnEpisodeBegin()` 바로 위에 아래 메서드 추가:

```csharp
/// <summary>
/// AgentPopulator 등 런타임 생성기가 레퍼런스를 주입할 때 사용한다.
/// ML-Agents Initialize() 호출 이후에도 호출 가능하다.
/// </summary>
public void SetRefs(SpawnManager sm, Transform g, ShipStatsSO s)
{
    spawnManager = sm;
    goal         = g;
    stats        = s;
}
```

- [ ] **Step 7: Unity Test Runner에서 통과 확인**

Test Runner에서 `ShipAgentSetRefsTests` + 기존 `ShipStatsSO_Tests` 모두 실행.  
예상: ShipAgentSetRefsTests 3개 + 기존 11개 = 14개 이상 통과.

- [ ] **Step 8: 커밋**

```bash
git add UnityProject/Assets/Scripts/Agent/ShipAgent.cs \
        UnityProject/Assets/Tests/EditMode/ShipAgentSetRefsTests.cs
git commit -m "feat: replace fixed spawnIndex with random spawn and add SetRefs"
```

---

### Task 3: AgentPopulator — 런타임 N-에이전트 생성기

**Files:**
- Create: `UnityProject/Assets/Scripts/Environment/AgentPopulator.cs`
- Create: `UnityProject/Assets/Tests/EditMode/AgentPopulatorTests.cs`

- [ ] **Step 1: 테스트 파일 생성**

`UnityProject/Assets/Tests/EditMode/AgentPopulatorTests.cs` 를 아래 내용으로 생성:

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

        var popGO = new GameObject("AgentPopulator");
        popGO.transform.SetParent(_root.transform);
        var pop = popGO.AddComponent<AgentPopulator>();

        var bind = BindingFlags.NonPublic | BindingFlags.Instance;
        var t    = typeof(AgentPopulator);
        t.GetField("agentCount",   bind).SetValue(pop, count);
        t.GetField("stats",        bind).SetValue(pop, _stats);
        t.GetField("spawnManager", bind).SetValue(pop, sm);
        t.GetField("goal",         bind).SetValue(pop, goalGO.transform);

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
        {
            var child = pop.transform.GetChild(i);
            Assert.IsNotNull(child.GetComponent<ShipAgent>(),
                $"Child {i} has no ShipAgent");
        }
    }

    [Test]
    public void SpawnAgents_EachChildHasRigidbody()
    {
        var pop = CreatePopulator(3);
        for (int i = 0; i < pop.transform.childCount; i++)
        {
            var child = pop.transform.GetChild(i);
            var rb = child.GetComponent<Rigidbody>();
            Assert.IsNotNull(rb, $"Child {i} has no Rigidbody");
            Assert.IsFalse(rb.useGravity, $"Child {i} useGravity should be false");
        }
    }
}
```

- [ ] **Step 2: Unity Test Runner에서 실패 확인**

Test Runner에서 `AgentPopulatorTests` 실행.  
예상: `AgentPopulator` 클래스 없음으로 컴파일 오류.

- [ ] **Step 3: AgentPopulator 구현**

`UnityProject/Assets/Scripts/Environment/AgentPopulator.cs` 를 생성:

```csharp
// UnityProject/Assets/Scripts/Environment/AgentPopulator.cs
using UnityEngine;
using HormuzAI.Agent;
using HormuzAI.Data;

namespace HormuzAI.Environment
{
    /// <summary>
    /// 씬 시작 시 N개의 ShipAgent를 동적으로 생성한다.
    /// Inspector에서 agentCount, stats, spawnManager, goal을 할당 후 Play하면
    /// 에이전트가 자동 배치되어 ML-Agents 학습이 시작된다.
    /// </summary>
    public class AgentPopulator : MonoBehaviour
    {
        [Header("Agent Settings")]
        [SerializeField] int agentCount = 50;
        [SerializeField] ShipStatsSO stats;

        [Header("Scene References")]
        [SerializeField] SpawnManager spawnManager;
        [SerializeField] Transform    goal;

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

                // Rigidbody 먼저 — useGravity=false 설정
                // (ShipAgent.Initialize()는 constraints만 설정하므로 여기서 처리)
                var rb = go.AddComponent<Rigidbody>();
                rb.useGravity = false;

                // 함선 형태 근사 Collider
                var col       = go.AddComponent<CapsuleCollider>();
                col.radius    = 15f;
                col.height    = 60f;
                col.direction = 2; // Z축 (전진 방향)

                // ShipAgent — ML-Agents가 Initialize()를 자동 호출
                // SetRefs는 즉시 호출되므로 첫 에피소드 시작 전에 stats가 할당됨
                var agent = go.AddComponent<ShipAgent>();
                agent.SetRefs(spawnManager, goal, stats);
            }
        }
    }
}
```

- [ ] **Step 4: Unity Test Runner에서 통과 확인**

Test Runner에서 전체 테스트 실행 (`Run All`).  
예상: SpawnManagerTests 3개 + ShipAgentSetRefsTests 3개 + AgentPopulatorTests 3개 + ShipStatsSO_Tests 11개 = 20개 이상 모두 통과.

- [ ] **Step 5: 커밋**

```bash
git add UnityProject/Assets/Scripts/Environment/AgentPopulator.cs \
        UnityProject/Assets/Tests/EditMode/AgentPopulatorTests.cs
git commit -m "feat: add AgentPopulator for runtime N-agent creation"
```

---

### Task 4: YAML — 병렬 학습 파라미터 조정

**Files:**
- Modify: `config/hormuz_stage1.yaml`

- [ ] **Step 1: YAML 전체 교체**

`config/hormuz_stage1.yaml` 를 아래로 교체:

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

### Task 5: Play Mode 씬 설정 및 검증

Unity Editor에서 수동 진행.

- [ ] **Step 1: HormuzStage1 씬 열기**

`Assets/Scenes/HormuzStage1.unity` 더블클릭으로 열기.  
씬이 없으면 메뉴 `Hormuz > Build Scene` 실행.

- [ ] **Step 2: AgentPopulator 오브젝트 추가**

Hierarchy에서 빈 GameObject 생성 (`Ctrl+Shift+N`).  
이름을 `AgentPopulator` 으로 변경.  
Inspector에서 `Add Component > AgentPopulator` 선택.

- [ ] **Step 3: Inspector 레퍼런스 할당**

| 필드 | 할당 |
|------|------|
| Agent Count | `50` |
| Stats | `Assets/Data/Ships/KoreaShipStats.asset` |
| Spawn Manager | Hierarchy의 `HormuzScene/SpawnPoints` 오브젝트 드래그 |
| Goal | Hierarchy의 `HormuzScene/GoalTrigger` 오브젝트 드래그 |

- [ ] **Step 4: Play 버튼으로 검증**

Play 클릭 후 확인:
- Hierarchy에 `AgentPopulator/ShipAgent_000` ~ `ShipAgent_049` 50개 생성됨
- Scene View에서 선박 50척이 SpawnPoint 반경 500m 내 각기 다른 위치/방향에 배치됨
- 에이전트들이 전진하며 움직이기 시작함
- Console에 `[ShipAgent] stats not assigned` 오류 없음

- [ ] **Step 5: 씬 저장 후 커밋**

`Ctrl+S` 로 씬 저장.

```bash
git add UnityProject/Assets/Scenes/HormuzStage1.unity
git commit -m "scene: add AgentPopulator to HormuzStage1"
```
