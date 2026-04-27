# Heightmap 해안선 이진화 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stage 1 학습에서 배가 터레인을 뚫고 지나가는 현상을 해소하기 위해 heightmap의 해수면 근처 애매 영역을 이진화로 제거한다.

**Architecture:** `tools/generate_heightmap.py`의 `convert_to_raw()` 진입점에 `np.where` 기반 전처리 1줄을 추가한다. 저지대(<5m)는 바다(0m)로, 그 이상 육지는 +25m lift한다. Sea Level Y는 meta 파일 기반 자동 계산이므로 Unity C# 코드 수정은 없다.

**Tech Stack:** Python 3.9 (venv_mlagents), NumPy, PIL, rasterio, Unity 6 ML-Agents

**Spec:** [docs/superpowers/specs/2026-04-16-heightmap-coast-binarization-design.md](../specs/2026-04-16-heightmap-coast-binarization-design.md)

---

## File Structure

| 경로 | 작업 | 책임 |
|------|------|------|
| `tools/generate_heightmap.py` | Modify | 저지대 이진화 + 육지 lift 전처리 추가 |
| `UnityProject/Assets/Terrain/heightmap_hormuz.raw` | Regenerate | 전처리된 raw binary (자동) |
| `UnityProject/Assets/Terrain/heightmap_meta.txt` | Regenerate | Sea Level Y=0 갱신 (자동) |
| `UnityProject/Assets/Terrain/HormuzTerrainData.asset` | Regenerate | Unity `Hormuz > Build Scene` 실행 (자동) |
| `logs/terrain_check.png` | Regenerate | 진단 시각화 이미지 |

---

## Task 1: `tools/generate_heightmap.py` 전처리 추가

**Files:**
- Modify: `tools/generate_heightmap.py` (`convert_to_raw()` 진입점, 158번째 줄 근처)

- [ ] **Step 1.1: 현재 `convert_to_raw()` 첫 2줄 확인**

Read `tools/generate_heightmap.py` lines 156-160:

```python
def convert_to_raw(data: np.ndarray, output_raw: str, output_meta: str) -> None:
    """2D float32 고도 배열을 Unity용 16-bit big-endian RAW + 메타데이터로 저장."""
    min_val, max_val = float(data.min()), float(data.max())
    print(f"고도 범위: {min_val:.1f}m ~ {max_val:.1f}m  |  입력 크기: {data.shape}")
```

- [ ] **Step 1.2: 함수 docstring 직후에 전처리 블록 삽입**

Use Edit tool on `tools/generate_heightmap.py`:

`old_string`:
```python
def convert_to_raw(data: np.ndarray, output_raw: str, output_meta: str) -> None:
    """2D float32 고도 배열을 Unity용 16-bit big-endian RAW + 메타데이터로 저장."""
    min_val, max_val = float(data.min()), float(data.max())
    print(f"고도 범위: {min_val:.1f}m ~ {max_val:.1f}m  |  입력 크기: {data.shape}")
```

`new_string`:
```python
def convert_to_raw(data: np.ndarray, output_raw: str, output_meta: str) -> None:
    """2D float32 고도 배열을 Unity용 16-bit big-endian RAW + 메타데이터로 저장."""
    # ── 해안선 이진화 전처리 ────────────────────────────────────────────────
    # 해수면 ±5m 애매 영역을 제거해 배 콜라이더(반경 15m)와 터레인의 간헐적
    # 관통 문제를 해소한다. Stage 2 수심 gradient는 상수 offset으로 보존.
    # spec: docs/superpowers/specs/2026-04-16-heightmap-coast-binarization-design.md
    COAST_THRESHOLD = 5.0   # 이하 = 바다로 취급 (DEM 수직 오차 + 리샘플링 여유)
    LAND_LIFT       = 25.0  # 육지 +25m lift (배 콜라이더 15m + 안전 마진 10m)
    before_below = int(np.sum(data < COAST_THRESHOLD))
    before_above = int(np.sum(data >= COAST_THRESHOLD))
    data = np.where(data < COAST_THRESHOLD, 0.0, data + LAND_LIFT)
    print(f"해안선 이진화: 바다 {before_below}px / 육지 {before_above}px (+{LAND_LIFT:.0f}m lift)")
    # ──────────────────────────────────────────────────────────────────

    min_val, max_val = float(data.min()), float(data.max())
    print(f"고도 범위: {min_val:.1f}m ~ {max_val:.1f}m  |  입력 크기: {data.shape}")
```

- [ ] **Step 1.3: 수정 결과 검증 (파일 읽기)**

Read `tools/generate_heightmap.py` lines 156-175. 확인:
- 함수 진입 직후 `COAST_THRESHOLD = 5.0` 라인 존재
- `data = np.where(...)` 라인 존재
- 기존 `min_val, max_val = ...` 라인이 수정된 data를 사용하도록 순서 유지

- [ ] **Step 1.4: Commit 코드 수정**

```bash
cd /d/Project/OverTheStrait
git add tools/generate_heightmap.py
git commit -m "$(cat <<'EOF'
feat: heightmap 전처리 — 해수면 근처 애매 영역 이진화

저지대(<5m) → 바다(0m), 그 외 육지 → +25m lift.
배 CapsuleCollider(반경 15m)의 간헐적 지형 관통 문제 해소.
Stage 2 수심 gradient는 상수 offset으로 보존.

Spec: docs/superpowers/specs/2026-04-16-heightmap-coast-binarization-design.md

Co-Authored-By: Claude Opus 4.6 <noreply@anthropic.com>
EOF
)"
```

---

## Task 2: Heightmap 재생성

**Files:**
- Regenerate: `UnityProject/Assets/Terrain/heightmap_hormuz.raw`
- Regenerate: `UnityProject/Assets/Terrain/heightmap_meta.txt`

- [ ] **Step 2.1: DEM 타일 캐시 존재 확인**

```bash
ls /d/Project/OverTheStrait/tools/tmp_tiles/ 2>/dev/null | head -5
```

Expected: 비어있거나 없음 → Copernicus DEM 타일을 **재다운로드 필요** (약 54개 타일, 네트워크 사용).

만약 캐시 있음: 다운로드 skip → 바로 병합.

- [ ] **Step 2.2: heightmap 재생성 실행**

```bash
cd /d/Project/OverTheStrait && venv_mlagents/Scripts/python.exe tools/generate_heightmap.py 2>&1 | tail -30
```

Expected output (주요 라인):
```
타일 목록 가져오는 중...
총 NNN개 타일 확인됨
bbox 내 NN개 셀 확인 중 ...
육지 타일: N개 다운로드 / 해양 타일: N개 (0으로 처리)
타일 병합 중...
해안선 이진화: 바다 NNN_NNNpx / 육지 NNN_NNNpx (+25m lift)
고도 범위: 0.0m ~ 2006.0m  |  입력 크기: (...)
RAW 저장: ...heightmap_hormuz.raw
메타데이터 저장: ...heightmap_meta.txt
Unity Import Raw 설정:
  ...
  해수면 Y: 0.00m
완료! Unity에서 Hormuz > Build Scene 메뉴를 실행하세요.
```

핵심 확인: **`해수면 Y: 0.00m`** 찍혀야 함 (이전엔 10.69m).

- [ ] **Step 2.3: meta 파일 검증**

```bash
cat /d/Project/OverTheStrait/UnityProject/Assets/Terrain/heightmap_meta.txt
```

Expected:
```
Width: 1025
Height: 1025
Bit Depth: 16
Byte Order: Mac (big-endian)
Terrain Height (Unity): 2000
Sea Level Y (Unity): 0.00
```

- [ ] **Step 2.4: 진단 이미지 재생성**

```bash
cd /d/Project/OverTheStrait && venv_mlagents/Scripts/python.exe -c "
import numpy as np
from PIL import Image
RAW = 'UnityProject/Assets/Terrain/heightmap_hormuz.raw'
data = np.fromfile(RAW, dtype='>u2').reshape(1025, 1025)
# Sea level Y = 0으로 이동함. Terrain Height 2000m 기준 정규화.
h = data.astype(np.float32) / 65535.0 * 2000.0
SEA = 0.0
vis = np.flipud(h)  # 상단=북쪽
r = np.where(vis > SEA, 200, 30).astype(np.uint8)
g = np.where(vis > SEA, 170, 90).astype(np.uint8)
b = np.where(vis > SEA,  90, 180).astype(np.uint8)
# 해수면 ±5m 내 픽셀 카운트 (뚫림 위험대)
risk = np.abs(vis - SEA) < 5.0
r[risk] = 255; g[risk] = 50; b[risk] = 50
Image.fromarray(np.dstack([r, g, b])).save('logs/terrain_check.png')
risk_ratio = 100.0 * np.mean(risk)
print(f'해수면 ±5m 위험대 비율: {risk_ratio:.3f}%')
print(f'육지 최저 Unity y: {h[h>0].min():.2f}m' if np.any(h>0) else '모든 값이 0')
print(f'육지 최대 Unity y: {h.max():.2f}m')
print(f'바다 픽셀: {int(np.sum(h==0))}/{h.size}')
print('이미지 저장: logs/terrain_check.png')
"
```

Expected:
- **`해수면 ±5m 위험대 비율: < 5%`** (이상적으론 0%)
- **`육지 최저 Unity y: ~30m`** (±1m 허용)
- 육지 최대 Unity y: ~2000m
- 바다 픽셀은 전체의 60% 이상

- [ ] **Step 2.5: terrain_check.png 시각 확인**

Read `logs/terrain_check.png`. 확인:
- 빨강(해수면 ±5m 애매 영역)이 **거의 없음** — 이전 이미지(40% 빨강)와 비교해 극적 감소
- 모래색(육지) vs 파랑(바다) 명확히 구분
- 호르무즈 해협 형태가 인식 가능 (북쪽 이란 본토, 남쪽 UAE/오만)

**실패 시 대응**:
- 위험대 비율 > 5% → COAST_THRESHOLD를 5.0 → 7.0으로 올리고 Task 1.2부터 재시도
- 육지 최저가 30m가 아님 → LAND_LIFT 값 검토

- [ ] **Step 2.6: 재생성 결과 Commit**

```bash
cd /d/Project/OverTheStrait
git add UnityProject/Assets/Terrain/heightmap_hormuz.raw UnityProject/Assets/Terrain/heightmap_meta.txt logs/terrain_check.png
git commit -m "$(cat <<'EOF'
chore: heightmap 재생성 — 해안선 이진화 적용

Sea Level Y: 10.69m → 0.00m
해수면 ±5m 애매 영역: ~40% → <5%
육지 최저 Unity y: ~10m → ~30m

Co-Authored-By: Claude Opus 4.6 <noreply@anthropic.com>
EOF
)"
```

---

## Task 3: Unity 씬 재빌드 (사용자 Action)

**Files:**
- Regenerate: `UnityProject/Assets/Terrain/HormuzTerrainData.asset`
- Regenerate: `UnityProject/Assets/Scenes/HormuzStage1.unity`

- [ ] **Step 3.1: 사용자에게 Unity 작업 안내**

출력할 메시지:

> Unity Editor에서 다음 메뉴를 실행해주세요:
> 1. 메뉴: **Hormuz > Clear Scene** (기존 씬 정리)
> 2. 메뉴: **Hormuz > Build Scene** (heightmap 반영해 재빌드)
> 3. (최근 커밋 1c1729c에 따라) Build Scene이 실패하면 **한 번 더** 실행 — AssetDatabase race로 두 번 호출 필요
> 4. Hierarchy에서 다음 객체들의 Y 좌표 확인:
>    - `WaterPlane` — Y=0
>    - `SpawnPoint_0` — Y=0
>    - `GoalTrigger` — Y=0
> 5. Scene 뷰에서 Terrain 표면이 해수면과 명확히 구분되는지 확인

- [ ] **Step 3.2: 사용자 응답 대기**

사용자가 "완료"라고 확인하면 Task 4로 진행.

- [ ] **Step 3.3: Unity Editor 씬 변경사항 Commit**

```bash
cd /d/Project/OverTheStrait
git add UnityProject/Assets/Terrain/HormuzTerrainData.asset UnityProject/Assets/Scenes/HormuzStage1.unity
git commit -m "$(cat <<'EOF'
chore: Unity 씬 재빌드 — 이진화 heightmap 반영

WaterPlane/SpawnPoint/Goal 위치가 Sea Level Y=0 기준으로 자동 갱신됨.

Co-Authored-By: Claude Opus 4.6 <noreply@anthropic.com>
EOF
)"
```

---

## Task 4: 학습 재시작 & 검증

**Files:**
- Regenerate: `results/hormuz_run1/` (--force 덮어쓰기)
- Monitor: `logs/training_history.csv`

- [ ] **Step 4.1: 기존 mlagents / Unity 프로세스 종료 확인**

```bash
cmd.exe /c "tasklist" 2>&1 | grep -iE "python|mlagents|unity" || echo "NO_MATCH"
```

Expected: `NO_MATCH` — 살아있는 프로세스가 있으면 사용자에게 Unity Editor 중지(Stop) 요청.

- [ ] **Step 4.2: mlagents-learn 재시작 (백그라운드, --force)**

```bash
cd /d/Project/OverTheStrait && rm -f logs/mlagents_startup.log && venv_mlagents/Scripts/mlagents-learn.exe config/hormuz_stage1.yaml --run-id=hormuz_run1 --force > logs/mlagents_startup.log 2>&1
```

`run_in_background: true`로 실행.

- [ ] **Step 4.3: mlagents listening 상태 확인**

실행 후 약 8초 대기 후:

```bash
head -3 /d/Project/OverTheStrait/logs/mlagents_startup.log
```

Expected:
```
[INFO] Listening on port 5004. Start training by pressing the Play button in the Unity Editor.
```

`Listening on port 5004`가 안 찍히면 다음을 의심:
- 포트 5004 점유 → `netstat -ano | findstr :5004`로 확인 후 해당 PID 종료
- venv 손상 → `venv_mlagents/Scripts/mlagents-learn.exe --version`으로 정상 여부 확인

- [ ] **Step 4.4: 사용자에게 Unity Play 안내**

출력할 메시지:

> Unity Editor에서 **Play 버튼**을 눌러주세요.
> - 연결 성공 시 mlagents 콘솔에 `Connected new brain: HormuzShip?team=0` 표시
> - 만약 `Restarting worker[0]` 에러 → Stop 후 다시 Play
> - 첫 세대는 약 18초 실시간 소요 (timeScale=10, 180s 게임 시간)

- [ ] **Step 4.5: 첫 세대 결과 검증**

세대 완료 후:

```bash
tail -5 /d/Project/OverTheStrait/logs/training_history.csv
```

검증 기준:

| 지표 | 통과 기준 | 실패 시 의미 |
|------|-----------|-------------|
| **AvgReward** | **-5 ~ -9 범위** (per-step shaping 누적됨) | -0.3 고정이면 OnActionReceived 호출 안 됨 (Python 연결 실패) |
| **BestReward** | AvgReward보다 높은 값 | 모든 에이전트 결과 동일 → 학습 다양성 없음 |
| 종료 이유 분포 | Timeout 우세 or Collision 섞임 | Collision만 압도 → 여전히 뚫림 있음 |

`training_runtime_status.json`에서:

```bash
cat /d/Project/OverTheStrait/logs/training_runtime_status.json
```

- `goalReachedThisGeneration`: Stage 1 초기엔 0, 나중에 1~5+ 예상
- `collisionThisGeneration`: 0이 아니면 좋음 (배가 움직이고 있음)
- `timeoutThisGeneration`: 50/50이면 아직 학습 초기, 점차 감소 예상

- [ ] **Step 4.6: 세대 3~5개 관찰 후 상태 판단**

세대 3~5개 관찰 후:
- **정상**: 보상 누적 확인, 학습 진행 중 → 작업 완료
- **비정상** (여전히 -0.3 고정): Python 연결 상태/DecisionRequester/BehaviorParameters 재점검 필요

---

## Task 5: 최종 검증 & 문서 정리

- [ ] **Step 5.1: Git 상태 최종 확인**

```bash
cd /d/Project/OverTheStrait && git status && git log --oneline -5
```

Expected: clean, 최근 3개 커밋이 Task 1/2/3의 커밋 메시지와 매칭.

- [ ] **Step 5.2: 학습 진행 백그라운드 유지**

mlagents-learn은 백그라운드에서 계속 실행. 사용자는 `Training_Status_Monitor.bat` 실행해 실시간 추적 가능.

---

## 롤백 절차 (필요 시)

문제 발생 시:

```bash
cd /d/Project/OverTheStrait
git log --oneline -10
git revert <task1-commit-hash> <task2-commit-hash> <task3-commit-hash>
# 또는 hard reset:
# git reset --hard <previous-commit>
python tools/generate_heightmap.py  # 원본 코드로 재생성
```

Unity: `Hormuz > Build Scene` 재실행.

---

## Self-Review

**Spec 커버리지 확인**:
- ✅ 알고리즘 (`np.where` 1줄) → Task 1.2
- ✅ Heightmap 재생성 → Task 2.2
- ✅ Sea Level Y 자동 갱신 → Task 2.3 meta 검증
- ✅ 진단 이미지 검증 → Task 2.4-2.5
- ✅ Unity 씬 재빌드 → Task 3
- ✅ `hormuz_run1 --force` 재시작 → Task 4.2
- ✅ 학습 검증 기준 (-5 ~ -9 AvgReward) → Task 4.5
- ✅ 롤백 절차 → 별도 섹션

**파라미터 일관성**: `COAST_THRESHOLD=5.0`, `LAND_LIFT=25.0`은 Task 1.2에서 정의하고 Task 2.4 검증 기준(±5m, +30m)과 일치.

**Placeholder 없음**: 모든 step에 실제 명령/코드/예상 출력 포함.
