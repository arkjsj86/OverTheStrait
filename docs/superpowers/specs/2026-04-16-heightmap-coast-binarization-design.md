# Heightmap 해안선 이진화 — 설계 문서

작성일: 2026-04-16
스테이지: Stage 1 학습 정상화를 위한 지형 데이터 수정

## 문제 정의

현재 `tools/generate_heightmap.py`로 생성된 heightmap은 **해수면 ±5m 애매 영역이 전체의 약 40%**를 차지한다. 이 영역은 시각적으론 지형으로 보이지만 Unity 터레인 높이가 배의 CapsuleCollider(반경 15m) 충돌 범위와 아슬아슬하게 겹쳐서, **배가 경로에 따라 뚫고 지나가거나 걸리는 비일관적 동작**이 발생한다.

### 진단 근거

`UnityProject/Assets/Terrain/heightmap_hormuz.raw` 분석 결과 (`logs/terrain_check.png`):

| 구역 | 해수면(10.69m) 이상 비율 | 비고 |
|------|------|------|
| Q1 (Z=0~10km, 화면 하단, UAE 사막) | 100% | 확실한 육지 |
| Q2 (Z=10~20km, Goal 부근) | 70% | **애매 영역 많음** |
| Q3 (Z=20~30km, 해협 중앙) | 29% | **애매 영역 많음** |
| Q4 (Z=30~40km, Spawn 근처) | 43% | 확실한 육지/해협 혼재 |

Spawn→Goal 직선 경로 5군데 샘플 모두 Unity 높이 **10.7m** — 해수면 10.69m와 0.01m 차이. CapsuleCollider 반경 15m와 겹치는 영역이 경로 전반에 존재.

### 원인

`generate_heightmap.py`의 min-max 정규화:

1. Copernicus DEM의 해양 영역은 `nodata=0`으로 채워짐 (0m)
2. `normalize_and_resize()`는 전체 data min(~0) ~ max(~1981m)로 정규화
3. 해수면(10.69m)은 정규화값 0.0054에 불과한 좁은 band
4. LANCZOS 리샘플링이 해안선 경계를 뭉개면서 이 좁은 band에 해수면 근처 값이 광범위하게 분포

## 접근 방식 선택

### 검토한 대안

| 옵션 | 설명 | 판단 |
|------|------|------|
| A. 완전 이진화 | 해수면 이하=0m, 이상=100m 벽 | ❌ Stage 2 암초 회피 학습이 `_depthRatio` gradient에 의존 — 0/1 두 값만 나오면 무의미 |
| **B. 해안선만 이분화 + 육지는 원본 유지** | 저지대 제거 + 육지 lift | ✅ **채택** |
| C. 전체 육지 과장 lift | 모든 육지 +100m | ❌ 지리가 비현실적, Stage 3+ 경마 게임 시각 품질 저하 |

### B 선택 이유

- Stage 1의 목표(해안선 명확한 충돌)를 달성
- Stage 2의 수심/수로폭 센싱 gradient를 보존 (육지 형상은 원본 + 상수 offset)
- heightmap 재생성 1회로 Stage 2 진입 시 재작업 불필요

## 알고리즘

`tools/generate_heightmap.py`의 `convert_to_raw()` 진입 시점에 전처리 1줄:

```python
COAST_THRESHOLD = 5.0   # 이하 = 바다로 취급
LAND_LIFT       = 25.0  # 육지는 +25m lift → 해수면과 강제 이격

data = np.where(data < COAST_THRESHOLD, 0.0, data + LAND_LIFT)
```

### 파라미터 정당화

**COAST_THRESHOLD = 5m**
- Copernicus DEM 수직 오차 ~3m + LANCZOS 리샘플링 번짐 여유
- 실제 해안 저지대(5m 미만)는 Stage 1에선 해협을 불필요하게 좁히는 원인 → 바다 처리가 학습에 유리

**LAND_LIFT = 25m**
- 배 CapsuleCollider 반경 15m + 안전 마진 10m
- 육지 최저점 = (원본 5m + lift 25m) = Unity y=30m (정규화 후)
- 해수면 기준 +30m 위에서 시작 → 배가 해수면에 떠있어도 확실한 벽에 충돌

### 부수 효과: Sea Level Y 자동 이동

전처리 후 `data.min() = 0`이 되므로 `convert_to_raw()`의 sea_level_y 계산이 자동으로 **10.69m → 0m**로 갱신된다:

```python
sea_level_y = (-min_val / span) * UNITY_TERRAIN_H  # min_val=0 → sea_level_y=0
```

기존 코드(`HormuzSceneBuilder.ReadSeaLevel()`)는 `heightmap_meta.txt`에서 Sea Level Y를 읽어 모든 씬 객체(WaterPlane, SpawnPoint, Goal)의 y좌표에 자동 반영한다. **따라서 Unity C# 코드 수정은 불필요**.

## 예상 결과

| 항목 | 현재 | 수정 후 |
|------|------|---------|
| 해수면 ±5m 애매 영역 | 약 40% | **0%** |
| Sea Level Y (Unity) | 10.69m | **0m** (meta 자동 갱신) |
| 바다 영역 Unity y | 0m | 0m (해수면과 동일) |
| 육지 최저 Unity y | 10.1m (해수면과 같음) | **30m** (해수면+30m) |
| 해수면↔육지 여유 | ~0m (애매) | **+30m 확정** |
| 배 CapsuleCollider 여유 | 없음 (뚫림 가능) | 15m (반경 초과) |
| `_depthRatio` gradient | 망가짐 | 보존 (상수 offset) |
| `_widthRatio` 측정 | 부정확 | 정확 (해안선 분명) |
| 배 뚫림 현상 | 빈발 | 해소 |

## 영향 파일 & 작업 순서

1. **코드 수정**: `tools/generate_heightmap.py` — `convert_to_raw()` 진입점에 np.where 1줄
2. **Heightmap 재생성**: `python tools/generate_heightmap.py`
   - 출력: `UnityProject/Assets/Terrain/heightmap_hormuz.raw` (재생성)
   - 출력: `UnityProject/Assets/Terrain/heightmap_meta.txt` (Sea Level Y=0으로 갱신)
   - `tools/tmp_tiles/`에 타일 캐시 있으면 재다운로드 skip
3. **진단 이미지 재생성**: `logs/terrain_check.png` 비교 — 빨강(애매 영역) 소멸 확인
4. **Unity 씬 재빌드**: Unity Editor 메뉴 `Hormuz > Build Scene`
   - TerrainData / WaterPlane / SpawnPoint / Goal 위치 모두 자동 재생성 (Sea Level Y=0 반영)
5. **mlagents-learn 재시작**: `hormuz_run1 --force`로 덮어쓰기
6. **검증**: 첫 세대 AvgReward의 per-step shaping 누적 확인 (-5 ~ -9 범위면 정상)

**Unity C# 코드 수정 불필요**: 모든 y좌표 의존 코드가 이미 meta.txt 기반 동적 계산.

## 롤백 계획

기존 heightmap은 git에 있음:

```bash
git restore UnityProject/Assets/Terrain/heightmap_hormuz.raw UnityProject/Assets/Terrain/heightmap_meta.txt
```

Unity Editor에서 `Hormuz > Build Scene` 재실행 → 이전 상태 복구.

## 검증 기준

### 성공 신호
- `logs/terrain_check.png`의 빨강(해수면 ±5m) 영역 **< 5%**
- 학습 첫 세대 AvgReward가 **-0.3** 고정이 아닌 **-5 ~ -9** 범위 (per-step shaping 누적됨)
- 충돌(`-1.0`) 세대가 타임아웃 세대보다 많아지면 학습이 실제로 진행 중

### 재조정 필요 신호
- Goal 근처 해협이 너무 좁아 배가 진입 불가 → COAST_THRESHOLD 하향 (5→3)
- 육지 lift 부족으로 여전히 뚫림 → LAND_LIFT 상향 (25→40)

## Stage 2/3 호환성

- Stage 2 (암초 회피): `_depthRatio`는 raycast `Vector3.down` 기반 — 육지 gradient가 +25m offset만 추가되어 연속성 유지. 얕은 수로 vs 깊은 수로 구분 정상.
- Stage 3 (날씨): 지형 변경과 무관.
