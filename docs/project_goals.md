# Project Goals — Over The Strait

## 학습 단계 (3단계)

### Stage 1 — 기본 항법
**목표:** SpawnPoint에서 GoalTrigger까지 도착

- 에이전트가 해협을 통과하는 기본 경로 학습
- 장애물 없음, 순수 항법 능력 습득
- **완료 기준:** GoalReached 비율이 안정적으로 높아지는 것 확인

---

### Stage 2 — 정적 장애물 회피
**목표:** 경로 상에 암초(정적 장애물) 투입 후 회피 학습

- 암초는 씬에 고정 배치 (Rigidbody 없음, BoxCollider/MeshCollider)
- 에이전트는 레이캐스트 센서로 암초 감지 후 우회 경로 선택
- Stage 1 checkpoint를 초기값으로 fine-tuning
- **완료 기준:** 암초 배치 환경에서도 GoalReached 비율 유지

---

### Stage 3 — 동적 장애물 회피
**목표:** 미사일(동적 장애물) 투입 후 회피 학습

- 미사일은 일정 간격 또는 트리거 기반으로 발사
- 에이전트는 미사일의 속도/방향을 관측값으로 받아 회피 행동 학습
- Stage 2 checkpoint를 초기값으로 fine-tuning
- **완료 기준:** 미사일 환경에서도 GoalReached 비율 유지

---

## 완수 후 목표 — 특성 부여 및 레이스

### Step 1 — 함선별 특성 정의
각 함선 타입(KoreaShip, JapanShip, ChinaShip 등)에 고유한 행동 성향 부여

| 함선 | 특성 예시 |
|------|-----------|
| KoreaShip | 균형형 — 속도/회피 모두 표준 |
| JapanShip | 회피 우선형 — 위험 감지 시 감속·우회 |
| ChinaShip | 돌파형 — 장애물 도달 시간이 있으면 급가속 후 통과 |

> 특성의 핵심 개념: **같은 관측값에 다른 행동이 최적**이 되도록 보상 함수 또는 행동 파라미터를 함선별로 다르게 설계

---

### Step 2 — 특성 학습
- 함선 타입별로 별도 policy 학습 (BehaviorName을 타입별로 분리)
- 또는 동일 policy에 함선 타입을 observation으로 주입해 하나의 policy가 타입에 따라 다르게 행동하도록 학습
- Stage 3 checkpoint를 베이스로 fine-tuning

---

### Step 3 — 배 레이스
- 학습 완료된 KoreaShip / JapanShip / ChinaShip 등을 동일 씬에 배치
- 동일 SpawnPoint에서 GoalTrigger까지 동시 출발
- 각 배의 특성이 드러나는 레이스 시연
- 최종 목표: 경마형 멀티 AI 레이싱 게임으로 발전

---

## 전체 흐름 요약

```
Stage 1 (기본 항법)
    ↓ checkpoint 이어받기
Stage 2 (정적 장애물)
    ↓ checkpoint 이어받기
Stage 3 (동적 장애물)
    ↓ checkpoint 이어받기
특성 학습 (타입별 policy)
    ↓
레이스 시연
```
