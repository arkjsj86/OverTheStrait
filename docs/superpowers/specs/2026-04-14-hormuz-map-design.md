# Hormuz-AI-Nav: 맵 시스템 설계 스펙

**날짜:** 2026-04-14  
**상태:** 승인됨  
**범위:** Stage 1 지형 환경 구성 (Terrain + Water + 씬 오브젝트)

---

## 1. 목표

호르무즈 해협 핵심 수로 구간을 실제 지형 데이터 기반으로 Unity에서 구현한다. ML-Agents 학습에 최적화된 성능 우선 환경으로 구성하며, 추후 다수 AI 레이싱 형태로 확장 가능한 구조를 갖춘다.

---

## 2. 데이터 파이프라인

### 소스 데이터
- **GEBCO 2023** (General Bathymetric Chart of the Oceans)
  - 무료 공개 데이터, 전 세계 수심/고도 포함
  - 포맷: NetCDF (.nc) 또는 GeoTIFF

### 처리 영역
- **위도:** 26.35°N ~ 26.75°N (약 44km)
- **경도:** 56.05°E ~ 56.50°E (약 44km)
- **Unity 맵 출력:** 40km × 20km (가로:세로 2:1 비율로 크롭, 핵심 수로 중심)

### Python 변환 스크립트 (`tools/generate_heightmap.py`)
1. GEBCO NetCDF 로드 (`netCDF4`, `numpy`)
2. 지정 좌표 범위 크롭
3. 수심 데이터 정규화 (육지=높음, 깊은 바다=낮음)
4. 해수면 기준: 전체 범위의 50% 높이값으로 설정
5. 16-bit grayscale PNG 출력 (`1024×512`)
6. 출력 파일: `UnityProject/Assets/Terrain/heightmap_hormuz.png`

---

## 3. Unity 씬 구성

### Terrain 설정
| 항목 | 값 |
|------|----|
| Width / Length | 40,000 × 20,000 (m) |
| Height | 500 (m) |
| Heightmap Resolution | 1025 × 513 |
| 데이터 소스 | `heightmap_hormuz.png` (16-bit PNG import) |

### 씬 오브젝트 계층 구조
```
HormuzScene
├── Terrain                  ← GEBCO 기반 지형
├── WaterPlane               ← 해수면 평면 (y=250, Lit Shader)
├── SpawnPoints              ← 배열 구조 (레이싱 확장 대비)
│   ├── SpawnPoint_0         ← 서쪽 입구
│   └── SpawnPoint_1~N       ← (추후 멀티 에이전트용)
├── GoalTrigger              ← 동쪽 출구 도착 판정 (BoxCollider IsTrigger)
└── BoundaryWalls            ← 맵 경계 Invisible Collider 4면
```

### 오브젝트별 상세
| 오브젝트 | 컴포넌트 | 비고 |
|---------|---------|------|
| WaterPlane | MeshRenderer, MeshFilter | URP Lit, 알파 반투명 |
| SpawnPoints | Transform 배열 | `SpawnManager.cs`에서 관리 |
| GoalTrigger | BoxCollider (IsTrigger=true) | Tag: `"Goal"` |
| BoundaryWalls | BoxCollider (IsTrigger=false) | Layer: `"Boundary"`, Invisible |

---

## 4. 레이어 및 태그 설정

| 태그/레이어 | 용도 |
|------------|------|
| Tag: `Goal` | GoalTrigger 식별 |
| Tag: `Boundary` | 맵 경계 식별 |
| Layer: `Water` | 물 레이어 (Raycast 제외용) |
| Layer: `Terrain` | 지형 충돌 레이어 |

---

## 5. 파일 구조

```
UnityProject/Assets/
├── Scenes/
│   └── HormuzStage1.unity
├── Terrain/
│   └── heightmap_hormuz.png
├── Materials/
│   └── WaterMaterial.mat
└── Scripts/
    └── Environment/
        └── SpawnManager.cs

tools/
└── generate_heightmap.py
```

---

## 6. 확장성 고려사항 (레이싱 모드)

- `SpawnPoints`는 단일 Transform이 아닌 배열로 구현 → N개 에이전트 동시 spawn
- Terrain은 모든 에이전트가 공유하는 정적 오브젝트
- `GoalTrigger`는 먼저 도착한 에이전트를 식별하는 로직 추가 예정

---

## 7. 범위 외 (이 스펙에 포함되지 않음)

- ShipAgent.cs (별도 스펙)
- ML-Agents 학습 설정 yaml (별도 스펙)
- 암초, 미사일 시스템 (Stage 2, 3)
- 비주얼 업그레이드 (물 셰이더, 포스트 프로세싱)
