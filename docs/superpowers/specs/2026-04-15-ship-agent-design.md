# ShipAgent 시스템 설계 스펙

**날짜:** 2026-04-15  
**상태:** 승인됨  
**범위:** ShipAgent 베이스 클래스 + 함선별 서브클래스 (KoreaShip, JapanShip, ChinaShip) + ML-Agents 학습 설정

---

## 1. 목표

공유 ML 모델 하나로 여러 함선 타입을 학습시킨다. 각 함선은 ScriptableObject로 정의된 스탯을 가지며, 수심·수로폭·체력 상황에 따라 실효 스탯이 동적으로 변한다. 추후 스탯 추가·수정이 코드 변경 없이 가능하도록 유연하게 설계한다.

---

## 2. 파일 구조

```
Assets/Scripts/
├── Agent/
│   ├── ShipAgent.cs          ← ML-Agents Agent 베이스 클래스
│   ├── KoreaShip.cs          ← SO 할당 전용 서브클래스
│   ├── JapanShip.cs
│   └── ChinaShip.cs
├── Data/
│   └── ShipStatsSO.cs        ← ScriptableObject 정의
└── Environment/
    └── SpawnManager.cs       ← 기존 유지

Assets/Data/Ships/
├── KoreaShipStats.asset
├── JapanShipStats.asset
└── ChinaShipStats.asset

config/
└── hormuz_stage1.yaml
```

---

## 3. ShipStatsSO

```csharp
[CreateAssetMenu(fileName = "ShipStats", menuName = "HormuzAI/Ship Stats")]
public class ShipStatsSO : ScriptableObject
{
    [Header("Base Stats")]
    public float maxSpeed   = 10f;
    public float turnRate   = 1f;
    public float maxHealth  = 100f;

    [Header("Depth Multipliers")]
    public float shallowSpeedMult = 0.7f;   // 얕은 수심
    public float deepSpeedMult    = 1.2f;   // 깊은 수심

    [Header("Width Multipliers")]
    public float narrowTurnMult   = 1.3f;   // 좁은 수로
    public float wideTurnMult     = 1.0f;   // 넓은 수로

    [Header("Health Multipliers")]
    public float damagedSpeedMult  = 0.8f;  // 체력 50% 이하
    public float criticalSpeedMult = 0.5f;  // 체력 20% 이하
}
```

각 함선 서브클래스는 Inspector에서 해당 SO 에셋만 할당하면 된다.

```csharp
// KoreaShip.cs — 추가 코드 없음
public class KoreaShip : ShipAgent { }
```

---

## 4. ShipAgent 핵심 로직

### 4-1. FSM 상태

```
IDLE → NAVIGATING → CRASHED
                  → SUCCESS
```

### 4-2. 관측값 (Observations) — 9개

| # | 항목 | 설명 |
|---|------|------|
| 1~3 | 전방 레이캐스트 ×3 | 좌/정면/우 장애물까지 거리 (정규화) |
| 4 | 목표 방향 | 현재 진행 방향과 GoalTrigger 방향의 각도 차이 |
| 5 | 현재 속도 | 정규화된 현재 속도 |
| 6 | 수심 | 아래 방향 레이캐스트로 지형까지 거리 |
| 7 | 수로 폭 | 좌우 레이캐스트로 벽/육지까지 거리 평균 |
| 8 | 체력 비율 | currentHealth / maxHealth |
| 9 | 함선 타입 | KoreaShip=0, JapanShip=1, ChinaShip=2 |

### 4-3. 행동 (Actions) — Continuous 2개

| 축 | 범위 | 의미 |
|----|------|------|
| throttle | -1 ~ +1 | 전진 / 후진 |
| steering | -1 ~ +1 | 좌 / 우 회전 |

### 4-4. 보상 (Rewards)

| 상황 | 보상 |
|------|------|
| GoalTrigger 도달 | +1.0 (에피소드 종료) |
| 매 스텝 목표 접근 | +0.001 × 접근 거리 |
| 충돌 (BoundaryWall / Terrain) | -0.5 (에피소드 종료) |
| 시간 초과 | -0.1 |

### 4-5. 실효 스탯 계산

수심·수로폭·체력 상황을 복합 적용한다.

```csharp
float effectiveSpeed = stats.maxSpeed
    * GetDepthMultiplier()
    * GetHealthMultiplier();

float effectiveTurnRate = stats.turnRate
    * GetWidthMultiplier();
```

**임계값 기준** — `ShipAgent`의 `[SerializeField]` 필드로 선언 (SO가 아닌 에이전트에 귀속):
- 수심: `shallowThreshold = 500f` (m), `deepThreshold = 1500f` (m)
- 수로폭: `narrowThreshold = 3000f` (m)
- 체력: damaged = 50%, critical = 20%

---

## 5. ML-Agents YAML 설정

```yaml
# config/hormuz_stage1.yaml
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

**선택 이유:**
- `normalize: true` — 수심(0~2000m), 속도(0~15), 체력(0~1) 스케일 자동 정규화
- `beta: 0.005` — 좁은 수로 탐험 장려
- PPO + Continuous 2축 — 선박 조종에 적합

---

## 6. 확장성

- 새 함선 추가: 서브클래스 1개 + SO 에셋 1개만 생성
- 스탯 항목 추가: `ShipStatsSO`에 필드 추가 후 `ShipAgent`에서 `GetXxxMultiplier()` 추가
- 함선 타입 인코딩(관측값 #9)은 one-hot으로 전환 가능 (함선 증가 시)

---

## 7. 범위 외

- Stage 2 암초 시스템
- Stage 3 미사일 시스템
- 비주얼 업그레이드 (물 셰이더, 포스트 프로세싱)
- 레이싱 모드 멀티 에이전트 동시 학습 (별도 스펙)
