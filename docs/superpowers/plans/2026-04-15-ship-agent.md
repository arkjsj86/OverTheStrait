# ShipAgent System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** ML-Agents 공유 모델로 학습하는 ShipAgent 베이스 클래스와 KoreaShip/JapanShip/ChinaShip 서브클래스를 구현한다.

**Architecture:** ScriptableObject(ShipStatsSO)가 함선별 스탯과 상황별 multiplier를 보유하며, ShipAgent가 수심·수로폭·체력을 센싱해 실효 스탯을 계산한다. 세 서브클래스는 SO 할당 외 추가 코드 없이 구분된다.

**Tech Stack:** Unity 6 (URP 17), ML-Agents release_20 (`Unity.MLAgents`), C#, Python (mlagents-learn)

---

## 파일 맵

| 경로 | 역할 |
|------|------|
| `Assets/Scripts/Data/ShipStatsSO.cs` | 스탯 데이터 + multiplier 계산 (SO) |
| `Assets/Scripts/Agent/ShipAgent.cs` | ML-Agents 베이스 에이전트 |
| `Assets/Scripts/Agent/KoreaShip.cs` | SO 할당 전용 서브클래스 |
| `Assets/Scripts/Agent/JapanShip.cs` | SO 할당 전용 서브클래스 |
| `Assets/Scripts/Agent/ChinaShip.cs` | SO 할당 전용 서브클래스 |
| `Assets/Tests/EditMode/HormuzAI.Tests.EditMode.asmdef` | 테스트 어셈블리 정의 |
| `Assets/Tests/EditMode/ShipStatsSOTests.cs` | Edit Mode 유닛 테스트 |
| `config/hormuz_stage1.yaml` | ML-Agents 학습 설정 |

---

## Task 1: ShipStatsSO

**Files:**
- Create: `Assets/Scripts/Data/ShipStatsSO.cs`

- [ ] **Step 1: 스크립트 파일 생성**

`Assets/Scripts/Data/ShipStatsSO.cs`:

```csharp
using UnityEngine;

namespace HormuzAI.Data
{
    [CreateAssetMenu(fileName = "ShipStats", menuName = "HormuzAI/Ship Stats")]
    public class ShipStatsSO : ScriptableObject
    {
        [Header("Base Stats")]
        public float maxSpeed  = 10f;
        public float turnRate  = 1f;
        public float maxHealth = 100f;

        [Header("Depth Multipliers")]
        public float shallowSpeedMult = 0.7f;   // depthRatio < 0.33
        public float deepSpeedMult    = 1.2f;   // depthRatio > 0.67

        [Header("Width Multipliers")]
        public float narrowTurnMult = 1.3f;     // widthRatio < 0.5
        public float wideTurnMult   = 1.0f;

        [Header("Health Multipliers")]
        public float damagedSpeedMult  = 0.8f;  // healthRatio <= 0.5
        public float criticalSpeedMult = 0.5f;  // healthRatio <= 0.2

        /// <summary>정규화된 수심 비율(0=얕음, 1=깊음)로 속도 multiplier 반환.</summary>
        public float GetDepthMult(float depthRatio)
        {
            if (depthRatio < 0.33f) return shallowSpeedMult;
            if (depthRatio > 0.67f) return deepSpeedMult;
            return 1.0f;
        }

        /// <summary>정규화된 수로폭 비율(0=좁음, 1=넓음)로 선회율 multiplier 반환.</summary>
        public float GetWidthMult(float widthRatio)
        {
            return widthRatio < 0.5f ? narrowTurnMult : wideTurnMult;
        }

        /// <summary>체력 비율(0=사망, 1=만땅)로 속도 multiplier 반환.</summary>
        public float GetHealthMult(float healthRatio)
        {
            if (healthRatio <= 0.2f) return criticalSpeedMult;
            if (healthRatio <= 0.5f) return damagedSpeedMult;
            return 1.0f;
        }

        /// <summary>실효 속도 = maxSpeed × 수심 mult × 체력 mult.</summary>
        public float GetEffectiveSpeed(float depthRatio, float healthRatio)
            => maxSpeed * GetDepthMult(depthRatio) * GetHealthMult(healthRatio);

        /// <summary>실효 선회율 = turnRate × 수로폭 mult.</summary>
        public float GetEffectiveTurnRate(float widthRatio)
            => turnRate * GetWidthMult(widthRatio);
    }
}
```

- [ ] **Step 2: Unity Editor에서 컴파일 확인**

Unity Editor 열기 → Console에 에러 없음 확인.

---

## Task 2: Edit Mode 테스트

**Files:**
- Create: `Assets/Tests/EditMode/HormuzAI.Tests.EditMode.asmdef`
- Create: `Assets/Tests/EditMode/ShipStatsSOTests.cs`

- [ ] **Step 1: 테스트 어셈블리 정의 파일 생성**

`Assets/Tests/EditMode/HormuzAI.Tests.EditMode.asmdef`:

```json
{
    "name": "HormuzAI.Tests.EditMode",
    "references": [
        "UnityEngine.TestRunner",
        "UnityEditor.TestRunner"
    ],
    "includePlatforms": ["Editor"],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": true,
    "precompiledReferences": ["nunit.framework.dll"],
    "autoReferenced": false,
    "defineConstraints": ["UNITY_INCLUDE_TESTS"],
    "versionDefines": [],
    "noEngineReferences": false
}
```

- [ ] **Step 2: 실패하는 테스트 작성**

`Assets/Tests/EditMode/ShipStatsSOTests.cs`:

```csharp
using NUnit.Framework;
using UnityEngine;
using HormuzAI.Data;

namespace HormuzAI.Tests
{
    public class ShipStatsSOTests
    {
        ShipStatsSO _stats;

        [SetUp]
        public void SetUp()
        {
            _stats = ScriptableObject.CreateInstance<ShipStatsSO>();
            // defaults: shallowSpeedMult=0.7, deepSpeedMult=1.2, criticalSpeedMult=0.5
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_stats);

        // ── GetDepthMult ────────────────────────────────────────────────

        [Test]
        public void GetDepthMult_ShallowRatio_ReturnsShallowMult()
        {
            Assert.AreEqual(_stats.shallowSpeedMult, _stats.GetDepthMult(0.1f), 0.001f);
        }

        [Test]
        public void GetDepthMult_NormalRatio_ReturnsOne()
        {
            Assert.AreEqual(1.0f, _stats.GetDepthMult(0.5f), 0.001f);
        }

        [Test]
        public void GetDepthMult_DeepRatio_ReturnsDeepMult()
        {
            Assert.AreEqual(_stats.deepSpeedMult, _stats.GetDepthMult(0.9f), 0.001f);
        }

        // ── GetWidthMult ────────────────────────────────────────────────

        [Test]
        public void GetWidthMult_NarrowRatio_ReturnsNarrowMult()
        {
            Assert.AreEqual(_stats.narrowTurnMult, _stats.GetWidthMult(0.3f), 0.001f);
        }

        [Test]
        public void GetWidthMult_WideRatio_ReturnsWideMult()
        {
            Assert.AreEqual(_stats.wideTurnMult, _stats.GetWidthMult(0.7f), 0.001f);
        }

        // ── GetHealthMult ───────────────────────────────────────────────

        [Test]
        public void GetHealthMult_CriticalHealth_ReturnsCriticalMult()
        {
            Assert.AreEqual(_stats.criticalSpeedMult, _stats.GetHealthMult(0.1f), 0.001f);
        }

        [Test]
        public void GetHealthMult_DamagedHealth_ReturnsDamagedMult()
        {
            Assert.AreEqual(_stats.damagedSpeedMult, _stats.GetHealthMult(0.4f), 0.001f);
        }

        [Test]
        public void GetHealthMult_FullHealth_ReturnsOne()
        {
            Assert.AreEqual(1.0f, _stats.GetHealthMult(1.0f), 0.001f);
        }

        // ── GetEffectiveSpeed ───────────────────────────────────────────

        [Test]
        public void GetEffectiveSpeed_DeepAndFullHealth_AppliesDeepMult()
        {
            float expected = _stats.maxSpeed * _stats.deepSpeedMult * 1.0f;
            Assert.AreEqual(expected, _stats.GetEffectiveSpeed(0.9f, 1.0f), 0.001f);
        }

        [Test]
        public void GetEffectiveSpeed_ShallowAndCritical_AppliesBothMults()
        {
            float expected = _stats.maxSpeed * _stats.shallowSpeedMult * _stats.criticalSpeedMult;
            Assert.AreEqual(expected, _stats.GetEffectiveSpeed(0.1f, 0.1f), 0.001f);
        }

        // ── GetEffectiveTurnRate ────────────────────────────────────────

        [Test]
        public void GetEffectiveTurnRate_NarrowChannel_AppliesNarrowMult()
        {
            float expected = _stats.turnRate * _stats.narrowTurnMult;
            Assert.AreEqual(expected, _stats.GetEffectiveTurnRate(0.2f), 0.001f);
        }
    }
}
```

- [ ] **Step 3: Unity Test Runner에서 테스트 실행 (실패 확인)**

`Window → General → Test Runner → EditMode → Run All`

예상 결과: 어셈블리 오류 없이 테스트가 발견되어야 함. ShipStatsSO가 이미 구현됐으므로 Pass 예상.

- [ ] **Step 4: 모든 테스트 Pass 확인**

Test Runner에서 8개 테스트 모두 녹색(Pass) 확인.

- [ ] **Step 5: 커밋**

```bash
git add UnityProject/Assets/Scripts/Data/ShipStatsSO.cs \
        UnityProject/Assets/Tests/EditMode/HormuzAI.Tests.EditMode.asmdef \
        UnityProject/Assets/Tests/EditMode/ShipStatsSOTests.cs
git commit -m "feat: add ShipStatsSO with situational multipliers and edit mode tests"
```

---

## Task 3: ShipAgent 베이스 클래스

**Files:**
- Create: `Assets/Scripts/Agent/ShipAgent.cs`

- [ ] **Step 1: ShipAgent.cs 작성**

`Assets/Scripts/Agent/ShipAgent.cs`:

```csharp
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

        [Header("Sensing Thresholds")]
        [SerializeField] float shallowThreshold = 500f;   // m
        [SerializeField] float deepThreshold    = 1500f;  // m
        [SerializeField] float narrowThreshold  = 3000f;  // m
        [SerializeField] float raycastMaxDist   = 5000f;  // m

        [Header("Scene References")]
        [SerializeField] SpawnManager spawnManager;
        [SerializeField] Transform    goal;

        // ── Private state ─────────────────────────────────────────────────

        Rigidbody _rb;
        float     _currentHealth;
        ShipState _state;
        float     _prevDistToGoal;

        // ── Unity / ML-Agents lifecycle ───────────────────────────────────

        public override void Initialize()
        {
            _rb = GetComponent<Rigidbody>();
            _rb.constraints = RigidbodyConstraints.FreezePositionY
                            | RigidbodyConstraints.FreezeRotationX
                            | RigidbodyConstraints.FreezeRotationZ;
            _state = ShipState.Idle;
        }

        public override void OnEpisodeBegin()
        {
            // 이전 에피소드가 타임아웃으로 끝났으면 소폭 패널티
            if (_state == ShipState.Navigating)
                AddReward(-0.1f);

            _currentHealth = stats.maxHealth;
            _state         = ShipState.Navigating;

            Transform spawn = spawnManager != null
                ? spawnManager.GetSpawnPoint(0)
                : transform;

            transform.SetPositionAndRotation(spawn.position, spawn.rotation);
            _rb.linearVelocity  = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;

            _prevDistToGoal = goal != null
                ? Vector3.Distance(transform.position, goal.position)
                : 0f;
        }

        // ── Observations (9개) ────────────────────────────────────────────

        public override void CollectObservations(VectorSensor sensor)
        {
            // 1~3: 전방 레이캐스트 (좌 30°, 정면, 우 30°) — 0=장애물 없음, 1=바로 앞
            sensor.AddObservation(ObstacleRay(Quaternion.Euler(0, -30, 0) * transform.forward));
            sensor.AddObservation(ObstacleRay(transform.forward));
            sensor.AddObservation(ObstacleRay(Quaternion.Euler(0,  30, 0) * transform.forward));

            // 4: 목표 방향 (-1~+1, 정규화된 각도차)
            Vector3 toGoal = goal != null
                ? (goal.position - transform.position).normalized
                : transform.forward;
            float angle = Vector3.SignedAngle(transform.forward, toGoal, Vector3.up);
            sensor.AddObservation(angle / 180f);

            // 5: 현재 속도 (정규화)
            sensor.AddObservation(Mathf.Clamp01(_rb.linearVelocity.magnitude / stats.maxSpeed));

            // 6: 수심 비율 (0=얕음, 1=깊음)
            sensor.AddObservation(GetDepthRatio());

            // 7: 수로폭 비율 (0=좁음, 1=넓음)
            sensor.AddObservation(GetWidthRatio());

            // 8: 체력 비율
            sensor.AddObservation(_currentHealth / stats.maxHealth);

            // 9: 함선 타입 (0=Korea, 0.5=Japan, 1=China)
            sensor.AddObservation((float)shipType / 2f);
        }

        // ── Actions ───────────────────────────────────────────────────────

        public override void OnActionReceived(ActionBuffers actions)
        {
            if (_state != ShipState.Navigating) return;

            float throttle = actions.ContinuousActions[0]; // -1 ~ +1
            float steering = actions.ContinuousActions[1]; // -1 ~ +1

            float depthRatio  = GetDepthRatio();
            float widthRatio  = GetWidthRatio();
            float healthRatio = _currentHealth / stats.maxHealth;

            float speed    = stats.GetEffectiveSpeed(depthRatio, healthRatio);
            float turnRate = stats.GetEffectiveTurnRate(widthRatio);

            _rb.linearVelocity = transform.forward * throttle * speed;
            transform.Rotate(Vector3.up, steering * turnRate * 45f * Time.fixedDeltaTime);

            // 목표 접근 보상
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

        // ── Collision / Trigger ───────────────────────────────────────────

        void OnTriggerEnter(Collider other)
        {
            if (_state != ShipState.Navigating) return;

            if (other.CompareTag("Goal"))
            {
                _state = ShipState.Success;
                SetReward(1f);
                EndEpisode();
            }
        }

        void OnCollisionEnter(Collision collision)
        {
            if (_state != ShipState.Navigating) return;

            bool isBoundary = collision.gameObject.CompareTag("Boundary");
            bool isTerrain  = collision.gameObject.layer == LayerMask.NameToLayer("Terrain");

            if (isBoundary || isTerrain)
            {
                _state = ShipState.Crashed;
                SetReward(-0.5f);
                EndEpisode();
            }
        }

        // ── Sensing helpers ───────────────────────────────────────────────

        /// <summary>0=장애물 없음(멀거나 미검출), 1=바로 앞. 관측값용.</summary>
        float ObstacleRay(Vector3 direction)
        {
            return Physics.Raycast(transform.position, direction, out RaycastHit hit, raycastMaxDist)
                ? 1f - (hit.distance / raycastMaxDist)
                : 0f;
        }

        /// <summary>실제 거리 반환. 미검출 시 raycastMaxDist. 수로폭 계산용.</summary>
        float RaycastDistance(Vector3 direction)
        {
            return Physics.Raycast(transform.position, direction, out RaycastHit hit, raycastMaxDist)
                ? hit.distance
                : raycastMaxDist;
        }

        /// <summary>0=얕음, 1=깊음. shallowThreshold~deepThreshold 사이를 선형 정규화.</summary>
        float GetDepthRatio()
        {
            if (!Physics.Raycast(transform.position, Vector3.down, out RaycastHit hit, deepThreshold + 100f))
                return 1f;

            float depth = hit.distance;
            if (depth <= shallowThreshold) return 0f;
            if (depth >= deepThreshold)    return 1f;
            return (depth - shallowThreshold) / (deepThreshold - shallowThreshold);
        }

        /// <summary>0=좁음(양쪽 평균 < narrowThreshold), 1=넓음.</summary>
        float GetWidthRatio()
        {
            float left  = RaycastDistance(-transform.right);
            float right = RaycastDistance( transform.right);
            float avg   = (left + right) * 0.5f;
            return Mathf.Clamp01(avg / narrowThreshold);
        }
    }
}
```

- [ ] **Step 2: Unity Editor에서 컴파일 확인**

Console에 에러 없음 확인. ML-Agents 패키지 미설치 에러가 있으면:  
`Window → Package Manager → + → Add package from git URL → https://github.com/Unity-Technologies/ml-agents.git?path=com.unity.ml-agents#release_20`

- [ ] **Step 3: 커밋**

```bash
git add UnityProject/Assets/Scripts/Agent/ShipAgent.cs
git commit -m "feat: add ShipAgent base class with ML-Agents observations and rewards"
```

---

## Task 4: Ship 서브클래스 3개

**Files:**
- Create: `Assets/Scripts/Agent/KoreaShip.cs`
- Create: `Assets/Scripts/Agent/JapanShip.cs`
- Create: `Assets/Scripts/Agent/ChinaShip.cs`

- [ ] **Step 1: KoreaShip.cs 작성**

`Assets/Scripts/Agent/KoreaShip.cs`:

```csharp
namespace HormuzAI.Agent
{
    /// <summary>KoreaShipStats.asset을 Inspector에서 할당한다.</summary>
    public class KoreaShip : ShipAgent { }
}
```

- [ ] **Step 2: JapanShip.cs 작성**

`Assets/Scripts/Agent/JapanShip.cs`:

```csharp
namespace HormuzAI.Agent
{
    /// <summary>JapanShipStats.asset을 Inspector에서 할당한다.</summary>
    public class JapanShip : ShipAgent { }
}
```

- [ ] **Step 3: ChinaShip.cs 작성**

`Assets/Scripts/Agent/ChinaShip.cs`:

```csharp
namespace HormuzAI.Agent
{
    /// <summary>ChinaShipStats.asset을 Inspector에서 할당한다.</summary>
    public class ChinaShip : ShipAgent { }
}
```

- [ ] **Step 4: 커밋**

```bash
git add UnityProject/Assets/Scripts/Agent/KoreaShip.cs \
        UnityProject/Assets/Scripts/Agent/JapanShip.cs \
        UnityProject/Assets/Scripts/Agent/ChinaShip.cs
git commit -m "feat: add KoreaShip, JapanShip, ChinaShip subclasses"
```

---

## Task 5: ML-Agents YAML 설정

**Files:**
- Create: `config/hormuz_stage1.yaml`

- [ ] **Step 1: yaml 파일 작성**

`config/hormuz_stage1.yaml`:

```yaml
behaviors:
  HormuzShip:
    trainer_type: ppo
    hyperparameters:
      batch_size: 512
      buffer_size: 4096
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
        gamma: 0.99
        strength: 1.0
    max_steps: 500000
    time_horizon: 64
    summary_freq: 5000
```

- [ ] **Step 2: 커밋**

```bash
git add config/hormuz_stage1.yaml
git commit -m "feat: add ML-Agents PPO config for HormuzShip"
```

---

## Task 6: Unity Inspector 설정 (수동)

코드 구현 완료 후 Unity Editor에서 아래를 수행한다.

- [ ] **Step 1: ShipStatsSO 에셋 3개 생성**

`Assets/Data/Ships/` 폴더 생성 후:
- 우클릭 → `Create → HormuzAI → Ship Stats` → `KoreaShipStats`
- 동일하게 `JapanShipStats`, `ChinaShipStats` 생성
- 각 에셋 선택 후 Inspector에서 스탯 값 설정 (일단 기본값 유지)

- [ ] **Step 2: Agent GameObject 설정**

Hierarchy에서 빈 GameObject 생성 → 이름 `KoreaShip_Agent`:
1. `Add Component → KoreaShip`
2. `Add Component → Rigidbody`
   - Mass: 1000, Drag: 2, Angular Drag: 5
   - Use Gravity: true
3. `Add Component → Behavior Parameters` (ML-Agents)
   - Behavior Name: `HormuzShip`
   - Vector Observation Space Size: `9`
   - Actions → Continuous Actions: `2`
4. `Add Component → Decision Requester`
   - Decision Period: `5`
5. Inspector에서 ShipAgent 컴포넌트:
   - Stats: `KoreaShipStats.asset` 할당
   - Ship Type: `Korea`
   - Spawn Manager: `SpawnPoints` 오브젝트 할당
   - Goal: `GoalTrigger` 오브젝트 할당

- [ ] **Step 3: JapanShip_Agent, ChinaShip_Agent 동일하게 설정**

각각 `JapanShip`, `ChinaShip` 컴포넌트 사용, 해당 SO 에셋 할당.

- [ ] **Step 4: 학습 실행 테스트**

```bash
cd D:/Project/OverTheStrait
mlagents-learn config/hormuz_stage1.yaml --run-id=stage1_v1
```

Unity Editor에서 Play → Console에 `[INFO] Connected to Unity environment` 확인.

---

## 자체 검토 (Spec Coverage)

| 스펙 항목 | 구현 태스크 |
|-----------|------------|
| ShipStatsSO (SO + multiplier) | Task 1 |
| Edit Mode 테스트 | Task 2 |
| ShipAgent (관측 9개, 행동 2개, 보상, FSM) | Task 3 |
| KoreaShip / JapanShip / ChinaShip | Task 4 |
| hormuz_stage1.yaml | Task 5 |
| Unity Inspector 설정 | Task 6 |
| 수심·수로폭·체력 복합 multiplier 계산 | Task 1 + Task 3 (GetDepthRatio, GetWidthRatio) |
| 공유 모델 (함선 타입 관측값 #9) | Task 3 |
