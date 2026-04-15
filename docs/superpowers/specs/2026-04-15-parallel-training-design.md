# Parallel Training System 설계 스펙

**날짜:** 2026-04-15  
**상태:** 승인됨  
**범위:** SpawnManager 랜덤 스폰 + ShipAgent 스폰 리팩터 + AgentPopulator + YAML 조정

---

## 1. 목표

단일 Unity 씬에서 ShipAgent 50개(기본값)를 동시에 학습시킨다.  
각 에이전트는 SpawnPoint 반경 500m 내 무작위 위치/방향에서 시작해 GoalTrigger 도달을 학습한다.  
씬 1개로 시각 확인용 학습이 가능하며, `--num-envs` 플래그로 수평 확장 가능하다.

---

## 2. 변경 파일 목록

```
Assets/Scripts/
├── Environment/
│   ├── SpawnManager.cs         ← 랜덤 스폰 메서드 추가
│   └── AgentPopulator.cs       ← 신규: 런타임 N-에이전트 생성기
└── Agent/
    └── ShipAgent.cs            ← spawnIndex 제거, 랜덤 스폰 적용

config/
└── hormuz_stage1.yaml          ← 병렬 학습 파라미터 조정
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

## 4. ShipAgent

### 제거
- `[SerializeField] int spawnIndex` 필드 삭제

### OnEpisodeBegin 변경
```csharp
// 기존 (spawnIndex 기반 고정 스폰)
Transform spawn = spawnManager != null
    ? spawnManager.GetSpawnPoint(spawnIndex)
    : transform;
transform.SetPositionAndRotation(spawn.position, spawn.rotation);

// 변경 (랜덤 반경 스폰)
Vector3 spawnPos = spawnManager != null
    ? spawnManager.GetRandomSpawnPosition()
    : transform.position;
Quaternion spawnRot = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
transform.SetPositionAndRotation(spawnPos, spawnRot);
```

---

## 5. AgentPopulator (신규)

### 역할
씬 실행 시(`Awake`) N개의 ShipAgent GameObject를 코드로 생성한다.  
Prefab 에셋 없이 동작하므로 씬 설정이 단순하다.

### 파일 경로
`Assets/Scripts/Environment/AgentPopulator.cs`

### 주요 필드
| 필드 | 타입 | 기본값 | 설명 |
|------|------|--------|------|
| `agentCount` | int | 50 | 생성할 에이전트 수 |
| `stats` | ShipStatsSO | — | 모든 에이전트에 공유 할당 |
| `spawnManager` | SpawnManager | — | 스폰 위치 소스 |
| `goal` | Transform | — | GoalTrigger Transform |

### Awake 동작
1. `agentCount`개의 GameObject 생성
2. 각각에 Rigidbody + CapsuleCollider + ShipAgent 컴포넌트 추가
3. `shipAgent.SetRefs(spawnManager, goal, stats)` 호출로 레퍼런스 주입  
   (Section 6의 SetRefs 메서드 사용)
4. 부모 Transform: AgentPopulator의 GameObject

### Rigidbody 설정 (AgentPopulator 담당)
```csharp
rb.isKinematic = false;
rb.useGravity  = false;   // ShipAgent.Initialize()에서 미설정이므로 여기서 처리
```
constraints는 ShipAgent.Initialize()에서 자동 설정되므로 중복 설정 불필요.

### Collider
CapsuleCollider: radius=15, height=60, direction=2 (Z축, 함선 형태 근사).

---

## 6. ShipAgent — SetRefs 메서드 추가

```csharp
/// <summary>AgentPopulator 등 런타임 생성 시 레퍼런스를 외부에서 주입한다.</summary>
public void SetRefs(SpawnManager sm, Transform g, ShipStatsSO s)
{
    spawnManager = sm;
    goal         = g;
    stats        = s;
}
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

1. `AgentPopulator` 컴포넌트를 씬 내 빈 GameObject에 추가
2. Inspector에서 `stats`, `spawnManager`, `goal` 레퍼런스 할당
3. `agentCount` 원하는 수로 설정 (기본 50)
4. Play → Awake에서 에이전트 자동 생성

---

## 9. 학습 실행 명령

```bash
# 단일 씬 (시각 확인)
mlagents-learn config/hormuz_stage1.yaml --run-id=hormuz_run1

# 병렬 5개 인스턴스 (1개 시각 + 4개 headless)
mlagents-learn config/hormuz_stage1.yaml --run-id=hormuz_run1 --num-envs=5
```

---

## 10. 범위 외

- 에이전트별 시각적 구분 (색상 등)
- 학습 진행 UI / TensorBoard 연동 가이드
- SpawnPoint 위치 자동 보정 (수심 체크)
- 경마 멀티 에이전트 레이싱 모드
