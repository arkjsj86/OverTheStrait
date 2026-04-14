# 프로젝트 명: AI 기반 호르무즈 해협 통과 시뮬레이션 (Hormuz-AI-Nav)

이 문서는 강화학습(Reinforcement Learning)을 활용하여 자율 주행 선박이 복잡한 지형과 위협 요소를 극복하고 목표지점에 도달하는 시뮬레이션을 제작하기 위한 상세 기술 가이드입니다.

## 1. 기술 스택 (Technical Stack)
* **Engine:** Unity 6 (6000.3.5f2)
* **AI Framework:** Unity ML-Agents Toolkit
* **Language:** C#
* **Patterns:** Enum 기반 Finite State Machine (FSM), Observer Pattern, Singleton (Manager 클래스)
* **Camera:** Cinemachine 3.x (학습 관찰용)

## 2. 시뮬레이션 단계별 로드맵 (Curriculum Learning)

AI가 복잡한 환경을 한 번에 학습하기 어려우므로 단계를 나눕니다.

### Stage 1: 경로 탐색 및 지형 회피 (Terrain Avoidance)
* **목표:** 호르무즈 해협의 실제 지형 데이터를 기반으로 한 맵에서 해안선에 충돌하지 않고 골인 지점 도달.
* **환경 구성:** 실제 위도/경도 데이터를 기반으로 구현된 정적 지형 메쉬.

### Stage 2: 정적 장애물 극복 (Reef Navigation)
* **목표:** 수면 아래의 암초 및 좁은 수로를 통과.
* **환경 구성:** 경로상에 랜덤하거나 고정된 위치에 암초(Reef) 배치.

### Stage 3: 동적 위협 회피 (Missile Defense)
* **목표:** 배를 향해 날아오는 미사일을 감지하고 회피 기동.
* **환경 구성:** 일정 거리 진입 시 배를 추적하거나 특정 궤적으로 발사되는 미사일 오브젝트 추가.

---

## 3. 에이전트 설계 (Agent Design: Ship)

### 관측 (Observations - 무엇을 보는가?)
* **Raycast Sensors:** 배 전방 및 측면에 배치하여 지형 및 암초와의 거리 측정.
* **Relative Position:** 목표 지점(Goal)까지의 상대적 거리 및 각도.
* **Velocity/Direction:** 현재 배의 속도 및 선수 방향(Heading).
* **Missile Data (Stage 3):** 날아오는 미사일의 상대 위치 및 속도 벡터.

### 행동 (Actions - 무엇을 하는가?)
* **Continuous Actions:**
    * `Steer` (-1.0 ~ 1.0): 좌우 조타.
    * `Throttle` (0.0 ~ 1.0): 전진 가속.

### 상태 정의 (FSM)
* `IDLE`: 학습 대기 중.
* `NAVIGATING`: 목표를 향해 항해 중.
* `CRASHED`: 지형/암초 충돌 (에피소드 종료).
* `SUNK`: 미사일 피격 (에피소드 종료).
* `SUCCESS`: 목표지점 도달 (에피소드 종료).

---

## 4. 보상 시스템 (Reward Function)

| 항목 | 점수 | 조건 |
| :--- | :--- | :--- |
| **도착 보상** | +10.0 | 목표 지점 트리거 도달 |
| **전진 보상** | +0.01 | 매 프레임 목표 지점과 거리가 줄어들 때 |
| **생존 보상** | +0.001 | 매 프레임 생존 시 (빠른 통과를 유도하기 위해 소량 설정) |
| **지형 충돌** | -1.0 | 해안선이나 암초에 닿았을 때 (Episode 종료) |
| **미사일 피격** | -2.0 | 미사일에 맞았을 때 (Episode 종료) |
| **시간 초과** | -0.5 | 제한 시간 내 미사일/지형을 피하지 못하고 정체될 때 |

---

## 5. 추천 코드 아키텍처

AI 가독성과 유지보수를 위해 다음 구조를 권장합니다.

### ShipAgent.cs (C#)
* `OnEpisodeBegin()`: 배의 위치, 속도, 상태(Enum) 초기화.
* `CollectObservations()`: Raycast 및 물리 데이터 수집.
* `OnActionReceived()`: AI가 내린 결정을 물리 엔진(AddForce, AddTorque)에 적용.
* `OnTriggerEnter()`: 골인, 암초, 미사일 충돌 판정 및 보상 처리.

### MissileSystem.cs (Stage 3용)
* 배가 특정 영역에 들어오면 인스턴스화.
* Observer 패턴을 사용하여 배가 피격되거나 회피했을 때 이벤트를 전송.

### GameEventManager.cs (Observer)
* 배의 상태 변화(충돌, 성공)를 구독하는 클래스들에게 알림 (UI, 이펙트, 학습 엔진 등).

---

## 6. 클로드(Claude)와 작업 시 팁

이 기획서를 클로드에게 제공한 뒤 아래와 같이 요청하세요:

1.  **초기 설정:** "유니티 6에서 ML-Agents를 사용하여 배를 제어하는 `ShipAgent` 클래스를 만들어줘. Enum 기반 FSM을 사용하고 `OnActionReceived`에서 조타와 가속을 처리해줘."
2.  **센서 설정:** "배의 3D 모델 전방 180도 방향으로 7개의 Raycast를 쏘아 지형을 감지하는 설정을 `CollectObservations`에 넣어줘."
3.  **스테이지 제어:** "스테이지가 올라갈수록 암초와 미사일 시스템이 활성화되는 `StageManager`를 Singleton 패턴으로 작성해줘."
