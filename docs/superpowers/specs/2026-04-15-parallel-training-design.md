# Parallel Training System 설계 스펙

**날짜:** 2026-04-15  
**상태:** 승인됨 (v2 — GenerationManager 추가)  
**범위:** SpawnManager 랜덤 스폰 + ShipAgent 스폰 리팩터 + AgentPopulator + GenerationManager + YAML 조정

---

## 1. 목표

단일 Unity 씬에서 ShipAgent 50개(기본값)를 동시에 학습시킨다.  
각 에이전트는 SpawnPoint 반경 500m 내 무작위 위치/방향에서 시작해 GoalTrigger 도달을 학습한다.  
GenerationManager가 세대별 성과(Best/Avg/Goal 도달 수)를 Console에 출력하고 CSV로 기록해 학습 진행을 가시적으로 확인할 수 있다.

---

## 2. 변경 파일 목록

```
Assets/Scripts/
├── Environment/
│   ├── SpawnManager.cs         ← 랜덤 스폰 메서드 추가
│   ├── AgentPopulator.cs       ← 신규: 런타임 N-에이전트 생성기
│   └── GenerationManager.cs   ← 신규: 세대 추적 + CSV 기록
└── Agent/
    └── ShipAgent.cs            ← spawnIndex 제거, 랜덤 스폰, SetRefs 갱신

config/
└── hormuz_stage1.yaml          ← 병렬 학습 파라미터 조정

logs/
└── training_history.csv        ← 자동 생성 (세대별 히스토리)
```

---

## 3. SpawnManager

### 추가 필드
```csharp
[SerializeField] float spawnRadius = 500f;
```

### 추가 메서드
```csharp
/// <summary>
/// 첫 번째 스폰 포인트를 중심으로 spawnRadius 반경 내 무작위 위치를 반환한다.
/// Y 좌표는 스폰 포인트와 동일 (해수면 높이).
/// </summary>
public Vector3 GetRandomSpawnPosition()
{
    Vector3 center = _spawnPoints != null && _spawnPoints.Length > 0
        ? _spawnPoints[0].position
        : transform.position;
    Vector2 circle = Random.insideUnitCircle * spawnRadius;
    return center + new Vector3(circle.x, 0f, circle.y);
}
```

`GetSpawnPoint(int index)` 기존 메서드 유지 (경마 단계용).

---

## 4. GenerationManager (신규)

### 역할
ShipAgent로부터 에피소드 종료 결과를 수집해 세대 단위로 집계한다.  
세대가 완료될 때마다 Console 출력 + CSV 저장으로 학습 진행 상황을 가시화한다.  
CSV는 세션 간 이어쓰기되므로 Play를 껐다 켜도 기록이 누적된다.

### 파일 경로
`Assets/Scripts/Environment/GenerationManager.cs`

### 주요 필드
| 필드 | 타입 | 기본값 | 설명 |
|------|------|--------|------|
| `agentsPerGeneration` | int | 50 | AgentPopulator.agentCount와 동일하게 설정 |

### CSV 저장 위치
```
{ProjectRoot}/logs/training_history.csv
```
헤더: `Generation,BestReward,AvgReward,GoalReached,Total,Timestamp`

예시:
```
Generation,BestReward,AvgReward,GoalReached,Total,Timestamp
1,0.0123,0.0031,0,50,2026-04-15 14:30:00
2,0.1240,0.0450,1,50,2026-04-15 14:32:11
47,0.9800,0.7620,41,50,2026-04-15 15:44:03
```

### 세대 번호 이어받기
`Start()`에서 CSV 파일의 데이터 행 수를 읽어 다음 세대 번호를 결정한다.  
CSV 없음 → 1세대부터 시작. 있음 → (데이터 행 수 + 1)세대부터 시작.

### ReportEpisodeEnd(bool reachedGoal, float reward)
ShipAgent가 에피소드 종료 시 호출한다.

| 에피소드 결과 | reachedGoal | reward |
|-------------|-------------|--------|
| Goal 도달    | true        | 1.0f   |
| 충돌 (Crash) | false       | -0.5f  |
| 타임아웃     | false       | -0.1f  |

동작:
1. `_completedThisGen++`
2. `_totalReward += reward`
3. `_goalReachedCount += reachedGoal ? 1 : 0`
4. `if (reward > _bestReward) _bestReward = reward`
5. `if (_completedThisGen >= agentsPerGeneration)` → `EndGeneration()`

### EndGeneration()
1. `Debug.Log($"[Gen {_currentGeneration:D4}] Best: {best:F3} | Avg: {avg:F3} | Goal: {goal}/{total}")`
2. CSV 한 줄 append
3. `_currentGeneration++`
4. 집계 변수 초기화

---

## 5. ShipAgent 변경사항

### 제거
- `[SerializeField] int spawnIndex` 필드

### 추가 필드
```csharp
[SerializeField] GenerationManager generationManager;
```

### SetRefs 시그니처 갱신
```csharp
public void SetRefs(SpawnManager sm, Transform g, ShipStatsSO s, GenerationManager gm)
{
    spawnManager      = sm;
    goal              = g;
    stats             = s;
    generationManager = gm;
}
```

### Initialize() — stats null 가드 제거
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

### OnEpisodeBegin() — 랜덤 스폰 + 타임아웃 보고
```csharp
public override void OnEpisodeBegin()
{
    if (!_initialized) return;
    if (stats == null) { Debug.LogError(...); return; }

    // 이전 에피소드가 타임아웃으로 종료된 경우
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

    _prevDistToGoal = goal != null
        ? Vector3.Distance(transform.position, goal.position)
        : 0f;
}
```

### OnTriggerEnter() — Goal 보고 추가
```csharp
if (other.CompareTag("Goal"))
{
    _state = ShipState.Success;
    SetReward(1f);
    generationManager?.ReportEpisodeEnd(true, 1f);
    EndEpisode();
}
```

### OnCollisionEnter() — Crash 보고 추가
```csharp
if (isBoundary || isTerrain)
{
    _state = ShipState.Crashed;
    SetReward(-0.5f);
    generationManager?.ReportEpisodeEnd(false, -0.5f);
    EndEpisode();
}
```

---

## 6. AgentPopulator 변경사항

### 추가 필드
```csharp
[SerializeField] GenerationManager generationManager;
```

### SpawnAgents() 갱신
```csharp
agent.SetRefs(spawnManager, goal, stats, generationManager);
```

---

## 7. hormuz_stage1.yaml

50 에이전트 기준 최적화.

| 파라미터 | 기존 | 변경 | 근거 |
|----------|------|------|------|
| `time_horizon` | 256 | 64 | 빠른 업데이트 주기, 많은 에이전트로 다양성 충분 |
| `batch_size` | 512 | 1024 | 버퍼 크기 대비 비율 유지 |
| `buffer_size` | 10240 | 10240 | 50×64=3,200/라운드 → 약 3 라운드 수집 후 학습 |
| `max_steps` | 500,000 | 5,000,000 | 에이전트당 ~100k steps |
| `summary_freq` | 5000 | 10000 | 로그 빈도 조정 |

`--num-envs=5` 사용 시 실질 250 에이전트 병렬.

---

## 8. 씬 설정 가이드

1. 빈 GameObject `GenerationManager` 생성 → `GenerationManager` 컴포넌트 추가  
   `agentsPerGeneration` = AgentPopulator의 `agentCount`와 동일한 값으로 설정
2. 빈 GameObject `AgentPopulator` 생성 → `AgentPopulator` 컴포넌트 추가  
   `stats`, `spawnManager`, `goal`, `generationManager` 레퍼런스 할당
3. Play → Start에서 에이전트 자동 생성, Console에 세대 로그 출력

---

## 9. 학습 실행 명령

```bash
# 단일 씬 (시각 확인 + Console 세대 로그)
mlagents-learn config/hormuz_stage1.yaml --run-id=hormuz_run1

# 병렬 5개 인스턴스
mlagents-learn config/hormuz_stage1.yaml --run-id=hormuz_run1 --num-envs=5
```

---

## 10. 범위 외

- 에이전트별 시각적 구분 (색상 등)
- 학습 진행 UI (화면 내 세대 오버레이)
- TensorBoard 연동 가이드
- SpawnPoint 위치 자동 보정 (수심 체크)
- 경마 멀티 에이전트 레이싱 모드
