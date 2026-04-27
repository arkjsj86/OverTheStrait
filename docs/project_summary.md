# OverTheStrait 프로젝트 요약

작성일: 2026-04-15

## 한 줄 요약

이 프로젝트는 Unity 6와 ML-Agents를 사용해 자율 주행 선박이 호르무즈 해협을 통과하도록 학습시키는 강화학습 시뮬레이션입니다.

## 프로젝트 목적

- 실제 지형 기반의 해협 맵에서 선박이 목표 지점까지 도달하도록 학습
- 단계적으로 난이도를 올리며 장애물 회피 능력 학습
- 최종적으로는 함선별 성향을 부여해 멀티 AI 레이스 형태로 확장

## 현재 확인된 기술 스택

- Engine: Unity 6 (`6000.3.5f2`)
- AI: Unity ML-Agents Toolkit
- Language: C#
- 보조 스크립트: Python
- 주요 패키지:
  - `com.unity.ml-agents`
  - `com.unity.cinemachine`
  - `com.unity.ai.navigation`
  - `com.unity.render-pipelines.universal`

## 저장소 구조 요약

- `UnityProject/`
  - 실제 Unity 프로젝트 본체
- `config/hormuz_stage1.yaml`
  - ML-Agents 학습 설정
- `tools/generate_heightmap.py`
  - Copernicus DEM 데이터를 Unity용 하이트맵으로 변환
- `docs/project_goals.md`
  - 단계별 목표와 최종 확장 방향 정리
- `blueprint.md`
  - 전체 설계 방향과 보상/관측/행동 설계 문서
- `results/`
  - 학습 실행 결과물
- `logs/`
  - 학습 세대별 기록 CSV

## 현재 구현 상태

### 1. Stage 1 환경 구성 완료

현재 Unity 씬에는 Stage 1 기본 항법 학습을 위한 환경이 갖춰져 있습니다.

- Terrain
- WaterPlane
- SpawnPoint
- GoalTrigger
- BoundaryWalls
- OverviewCamera

관련 자동화 코드는 아래 파일에서 확인할 수 있습니다.

- `UnityProject/Assets/Scripts/Editor/HormuzSceneBuilder.cs`
- `UnityProject/Assets/Scripts/Editor/HormuzTrainingSetup.cs`

### 2. 강화학습 에이전트 기본 구조 구현됨

핵심 선박 에이전트는 이미 ML-Agents 루프에 맞게 구현되어 있습니다.

- 관측:
  - 전방/좌우 레이캐스트
  - 목표 방향
  - 현재 속도
  - 수심 비율
  - 수로 폭 비율
  - 체력 비율
  - 함선 타입
- 행동:
  - 전진/감속
  - 조향
- 보상:
  - 목표 접근 보상
  - 방향 정렬 보상
  - 수심/수로폭 품질 보상
  - 충돌/타임아웃 패널티
  - 목표 도달 보상

핵심 파일:

- `UnityProject/Assets/Scripts/Agent/ShipAgent.cs`
- `UnityProject/Assets/Scripts/Data/ShipStatsSO.cs`

### 3. 병렬 학습용 환경 관리 코드 존재

여러 선박을 동시에 생성하고 세대 단위로 리셋/집계하는 구조가 들어가 있습니다.

- `AgentPopulator.cs`: 학습용 ShipAgent 여러 대 생성
- `SpawnManager.cs`: 스폰 위치 관리
- `GenerationManager.cs`: 세대 집계 및 CSV 로그 기록

관련 파일:

- `UnityProject/Assets/Scripts/Environment/AgentPopulator.cs`
- `UnityProject/Assets/Scripts/Environment/SpawnManager.cs`
- `UnityProject/Assets/Scripts/Environment/GenerationManager.cs`

### 4. 함선 타입 확장 준비됨

다음 타입이 이미 분리되어 있습니다.

- `KoreaShip`
- `JapanShip`
- `ChinaShip`

아직 동작 차이는 크지 않지만, 향후 타입별 특성을 학습시키기 위한 구조는 준비된 상태입니다.

## 학습 실행 상태

학습 결과물은 이미 한 번 생성되었습니다.

- 결과 폴더: `results/hormuz_run1/`
- 모델 파일:
  - `results/hormuz_run1/HormuzShip.onnx`
  - `results/hormuz_run1/HormuzShip/checkpoint.pt`
- 학습 로그:
  - `logs/training_history.csv`

현재 로그 기준으로는 첫 세대 결과만 기록되어 있으며, 아직 목표 도달은 나오지 않았습니다.

- Generation 1
- GoalReached: `0 / 50`
- AvgReward: `-1.0000`

즉, 현재 상태는 "학습 파이프라인은 연결되었고, 정책 성능은 앞으로 튜닝해야 하는 단계"로 보는 것이 맞습니다.

## 현재 프로젝트를 한 문장으로 정리하면

OverTheStrait는 실제 지형 데이터를 바탕으로 해협 항로를 만들고, Unity와 ML-Agents로 선박 자율항해 AI를 학습시키며, 이후 함선별 성향과 레이스 시스템까지 확장하려는 프로젝트입니다.

## 집에서 먼저 보면 좋은 파일

빠르게 파악하려면 아래 순서로 보면 됩니다.

1. `docs/project_summary.md`
2. `docs/project_goals.md`
3. `blueprint.md`
4. `UnityProject/Assets/Scripts/Agent/ShipAgent.cs`
5. `config/hormuz_stage1.yaml`
