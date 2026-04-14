# Hormuz Map System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 실제 GEBCO 위성 고도 데이터를 기반으로 호르무즈 해협 핵심 수로 40×20km Unity 씬을 자동으로 구성한다.

**Architecture:** Python 스크립트가 GEBCO GeoTIFF 데이터를 다운로드하고 Unity Terrain용 16-bit RAW 하이트맵으로 변환한다. Unity Editor 스크립트(`HormuzSceneBuilder`)가 Terrain, WaterPlane, SpawnPoints, GoalTrigger, BoundaryWalls를 씬에 자동 배치한다.

**Tech Stack:** Python 3.9+, requests, rasterio, numpy, Pillow / Unity 6, C# (URP), ML-Agents release_20

---

## 파일 구조

```
D:\Project\OverTheStrait\
├── tools/
│   ├── requirements.txt                          # Python 의존성
│   ├── generate_heightmap.py                     # GEBCO → 16-bit RAW 변환
│   └── test_generate_heightmap.py                # Python 단위 테스트
└── UnityProject/Assets/
    ├── Scenes/
    │   └── HormuzStage1.unity                    # 메인 씬 (Editor 스크립트가 생성)
    ├── Terrain/
    │   ├── heightmap_hormuz.raw                  # Python 스크립트 출력
    │   ├── heightmap_meta.txt                    # 해수면 Y 등 메타데이터
    │   └── HormuzTerrainData.asset               # Unity TerrainData (Editor 스크립트 생성)
    ├── Materials/
    │   └── WaterMaterial.mat                     # 물 머티리얼 (Editor 스크립트 생성)
    └── Scripts/
        ├── Environment/
        │   └── SpawnManager.cs                   # 스폰 포인트 관리 (런타임)
        └── Editor/
            └── HormuzSceneBuilder.cs             # 씬 자동 생성 도구
```

---

### Task 1: Python 도구 환경 설정

**Files:**
- Create: `tools/requirements.txt`

- [ ] **Step 1: requirements.txt 작성**

```
# tools/requirements.txt
requests>=2.31.0
rasterio>=1.3.0
numpy>=1.24.0
Pillow>=10.0.0
```

- [ ] **Step 2: 패키지 설치**

```bash
cd tools
pip install -r requirements.txt
```

Expected (마지막 줄):
```
Successfully installed ...rasterio-... numpy-... Pillow-...
```

> **Windows에서 rasterio 설치 실패 시:**
> ```bash
> conda install -c conda-forge rasterio
> ```

- [ ] **Step 3: 설치 검증**

```bash
python -c "import rasterio, numpy, requests, PIL; print('OK')"
```

Expected: `OK`

- [ ] **Step 4: 커밋**

```bash
cd ..
git add tools/requirements.txt
git commit -m "feat: add Python tools environment for heightmap generation"
```

---

### Task 2: generate_heightmap.py 작성

**Files:**
- Create: `tools/test_generate_heightmap.py`
- Create: `tools/generate_heightmap.py`

- [ ] **Step 1: 테스트 먼저 작성 (TDD)**

```python
# tools/test_generate_heightmap.py
import numpy as np
import os
import sys
sys.path.insert(0, os.path.dirname(__file__))

from generate_heightmap import normalize_and_resize


def test_output_dtype_is_uint16():
    """결과 배열은 uint16 이어야 한다."""
    data = np.array([[-100.0, 0.0], [50.0, 300.0]], dtype=np.float32)
    result = normalize_and_resize(data, (2, 2))
    assert result.dtype == np.uint16


def test_output_range_within_uint16():
    """결과값은 0~65535 범위 내이어야 한다."""
    data = np.array([[-100.0, 0.0], [50.0, 300.0]], dtype=np.float32)
    result = normalize_and_resize(data, (2, 2))
    assert result.min() >= 0
    assert result.max() <= 65535


def test_min_max_maps_to_full_range():
    """최솟값 → 0, 최댓값 → 65535 으로 정규화되어야 한다."""
    data = np.array([[0.0, 100.0]], dtype=np.float32)
    result = normalize_and_resize(data, (1, 2))
    assert result[0, 0] == 0
    assert result[0, 1] == 65535


def test_output_shape_matches_target():
    """출력 shape 이 target_size 와 일치해야 한다."""
    data = np.ones((10, 20), dtype=np.float32)
    result = normalize_and_resize(data, (5, 7))
    assert result.shape == (5, 7)
```

- [ ] **Step 2: 테스트 실행 → 실패 확인**

```bash
cd tools
python -m pytest test_generate_heightmap.py -v
```

Expected: `ImportError: cannot import name 'normalize_and_resize'`

- [ ] **Step 3: generate_heightmap.py 작성**

```python
# tools/generate_heightmap.py
"""
Hormuz Strait Heightmap Generator

Usage:
    python generate_heightmap.py            # GEBCO 자동 다운로드
    python generate_heightmap.py input.tif  # 기존 GeoTIFF 사용
"""

import os
import sys
import zipfile
import requests
import numpy as np
import rasterio
from PIL import Image

# ── 설정 ─────────────────────────────────────────────────────
WEST, SOUTH, EAST, NORTH = 56.05, 26.35, 56.50, 26.75
HEIGHTMAP_SIZE = (1025, 1025)  # Unity heightmapResolution = 1025 → 정사각형

SCRIPT_DIR   = os.path.dirname(os.path.abspath(__file__))
PROJECT_ROOT = os.path.dirname(SCRIPT_DIR)
OUTPUT_RAW   = os.path.join(PROJECT_ROOT, "UnityProject", "Assets", "Terrain", "heightmap_hormuz.raw")
OUTPUT_META  = os.path.join(PROJECT_ROOT, "UnityProject", "Assets", "Terrain", "heightmap_meta.txt")
TMP_ZIP      = os.path.join(SCRIPT_DIR, "tmp_gebco.zip")
TMP_TIF      = os.path.join(SCRIPT_DIR, "tmp_hormuz.tif")

GEBCO_URL = (
    "https://download.gebco.net/api/download"
    f"?sw_lat={SOUTH}&sw_lng={WEST}&ne_lat={NORTH}&ne_lng={EAST}"
    "&format=geotiff&layer=2023"
)
# ─────────────────────────────────────────────────────────────


def normalize_and_resize(data: np.ndarray, target_size: tuple) -> np.ndarray:
    """
    float32 고도 배열을 정규화하고 target_size 로 리샘플링해 uint16 반환.

    Args:
        data: 2D float32 배열 (임의 고도 범위)
        target_size: (height, width) 출력 픽셀 수

    Returns:
        2D uint16 배열, 값 범위 0~65535
    """
    min_val = float(data.min())
    max_val = float(data.max())
    span = (max_val - min_val) or 1.0

    normalized = ((data - min_val) / span).astype(np.float32)  # 0.0 ~ 1.0

    # PIL mode='F' (32-bit float) 로 LANCZOS 리샘플링
    img = Image.fromarray(normalized, mode='F')
    img = img.resize((target_size[1], target_size[0]), Image.LANCZOS)
    resampled = np.array(img, dtype=np.float32)

    return (np.clip(resampled, 0.0, 1.0) * 65535).astype(np.uint16)


def download_gebco(output_zip: str) -> None:
    print(f"GEBCO 데이터 다운로드 중...")
    resp = requests.get(GEBCO_URL, stream=True, timeout=120)
    resp.raise_for_status()
    with open(output_zip, "wb") as f:
        for chunk in resp.iter_content(chunk_size=8192):
            f.write(chunk)
    print(f"다운로드 완료: {output_zip}")


def extract_tif(zip_path: str, output_tif: str) -> None:
    with zipfile.ZipFile(zip_path, "r") as z:
        tif_files = [n for n in z.namelist() if n.lower().endswith(".tif")]
        if not tif_files:
            raise FileNotFoundError("zip 안에 .tif 파일이 없습니다.")
        z.extract(tif_files[0], path=os.path.dirname(output_tif))
        extracted = os.path.join(os.path.dirname(output_tif), tif_files[0])
        if os.path.abspath(extracted) != os.path.abspath(output_tif):
            os.replace(extracted, output_tif)
    print(f"추출 완료: {output_tif}")


def convert_to_raw(input_tif: str, output_raw: str, output_meta: str) -> None:
    with rasterio.open(input_tif) as ds:
        data = ds.read(1).astype(np.float32)

    min_val, max_val = float(data.min()), float(data.max())
    print(f"고도 범위: {min_val:.1f}m ~ {max_val:.1f}m")

    heightmap = normalize_and_resize(data, HEIGHTMAP_SIZE)

    os.makedirs(os.path.dirname(output_raw), exist_ok=True)

    # Unity Import Raw: 16-bit big-endian (Mac byte order)
    heightmap.byteswap().tofile(output_raw)
    print(f"RAW 저장: {output_raw}")

    # 해수면 Y 계산 (Unity Terrain Height = 500)
    span = (max_val - min_val) or 1.0
    sea_level_y = (-min_val / span) * 500.0

    meta = (
        f"Width: {HEIGHTMAP_SIZE[1]}\n"
        f"Height: {HEIGHTMAP_SIZE[0]}\n"
        f"Bit Depth: 16\n"
        f"Byte Order: Mac (big-endian)\n"
        f"Terrain Height (Unity): 500\n"
        f"Sea Level Y (Unity): {sea_level_y:.2f}\n"
    )
    with open(output_meta, "w", encoding="utf-8") as f:
        f.write(meta)
    print(f"메타데이터 저장: {output_meta}")
    print(f"\nUnity Import Raw 설정:")
    print(f"  Depth: 16 bit | Width: {HEIGHTMAP_SIZE[1]} | Height: {HEIGHTMAP_SIZE[0]}")
    print(f"  Byte Order: Mac | Terrain Size: 40000 x 500 x 20000")
    print(f"  해수면 Y: {sea_level_y:.2f}m")


def main() -> None:
    input_tif = sys.argv[1] if len(sys.argv) > 1 else None

    if input_tif is None:
        download_gebco(TMP_ZIP)
        extract_tif(TMP_ZIP, TMP_TIF)
        input_tif = TMP_TIF

    convert_to_raw(input_tif, OUTPUT_RAW, OUTPUT_META)

    for f in [TMP_ZIP, TMP_TIF]:
        if os.path.exists(f):
            os.remove(f)

    print("\n완료! Unity에서 Hormuz > Build Scene 메뉴를 실행하세요.")


if __name__ == "__main__":
    main()
```

- [ ] **Step 4: 테스트 재실행 → 통과 확인**

```bash
python -m pytest test_generate_heightmap.py -v
```

Expected:
```
PASSED test_output_dtype_is_uint16
PASSED test_output_range_within_uint16
PASSED test_min_max_maps_to_full_range
PASSED test_output_shape_matches_target
4 passed
```

- [ ] **Step 5: 커밋**

```bash
cd ..
git add tools/generate_heightmap.py tools/test_generate_heightmap.py
git commit -m "feat: add GEBCO heightmap generator with unit tests"
```

---

### Task 3: Heightmap 생성 실행

**Files:**
- Creates: `UnityProject/Assets/Terrain/heightmap_hormuz.raw`
- Creates: `UnityProject/Assets/Terrain/heightmap_meta.txt`

- [ ] **Step 1: 스크립트 실행**

```bash
cd tools
python generate_heightmap.py
```

Expected (마지막 6줄):
```
RAW 저장: ...\UnityProject\Assets\Terrain\heightmap_hormuz.raw
메타데이터 저장: ...\UnityProject\Assets\Terrain\heightmap_meta.txt

Unity Import Raw 설정:
  Depth: 16 bit | Width: 1025 | Height: 1025
  Byte Order: Mac | Terrain Size: 40000 x 500 x 20000
  해수면 Y: ???m
```

> **GEBCO 다운로드 실패 시 (API 변경 등):**
> 1. https://download.gebco.net 접속
> 2. Bounding Box: S=26.35 N=26.75 W=56.05 E=56.50 설정
> 3. Format: GeoTIFF, Layer: 2023 Grid 선택 후 다운로드
> 4. `python generate_heightmap.py 다운로드한파일.tif` 로 실행

- [ ] **Step 2: 출력 파일 크기 확인**

`heightmap_hormuz.raw` 파일 크기 = 1025 × 1025 × 2 bytes = **약 2.1MB**

- [ ] **Step 3: 커밋**

```bash
cd ..
git add UnityProject/Assets/Terrain/heightmap_hormuz.raw
git add UnityProject/Assets/Terrain/heightmap_meta.txt
git commit -m "feat: add generated Hormuz Strait heightmap from GEBCO data"
```

---

### Task 4: SpawnManager.cs 작성

**Files:**
- Create: `UnityProject/Assets/Scripts/Environment/SpawnManager.cs`

- [ ] **Step 1: SpawnManager.cs 작성**

```csharp
// UnityProject/Assets/Scripts/Environment/SpawnManager.cs
using UnityEngine;

namespace HormuzAI.Environment
{
    /// <summary>
    /// SpawnPoints 오브젝트의 자식 Transform 을 스폰 위치로 관리한다.
    /// Awake 에서 자식을 자동 수집하므로 Inspector 할당 불필요.
    /// 멀티 에이전트 레이싱을 위해 인덱스 순환을 지원한다.
    /// </summary>
    public class SpawnManager : MonoBehaviour
    {
        private Transform[] _spawnPoints;

        private void Awake()
        {
            _spawnPoints = new Transform[transform.childCount];
            for (int i = 0; i < transform.childCount; i++)
                _spawnPoints[i] = transform.GetChild(i);
        }

        /// <summary>index 번째 스폰 포인트를 반환한다 (범위 초과 시 순환).</summary>
        public Transform GetSpawnPoint(int index = 0)
        {
            if (_spawnPoints == null || _spawnPoints.Length == 0)
                return transform;
            return _spawnPoints[index % _spawnPoints.Length];
        }

        /// <summary>등록된 스폰 포인트 수.</summary>
        public int SpawnCount => _spawnPoints?.Length ?? 0;
    }
}
```

- [ ] **Step 2: Unity 컴파일 확인**

Unity 에디터 포커스 이동 → Console 창 오류 없음 확인.

- [ ] **Step 3: 커밋**

```bash
git add UnityProject/Assets/Scripts/Environment/SpawnManager.cs
git commit -m "feat: add SpawnManager for multi-agent spawn point management"
```

---

### Task 5: HormuzSceneBuilder.cs 작성

**Files:**
- Create: `UnityProject/Assets/Scripts/Editor/HormuzSceneBuilder.cs`

- [ ] **Step 1: HormuzSceneBuilder.cs 작성**

```csharp
// UnityProject/Assets/Scripts/Editor/HormuzSceneBuilder.cs
using System.IO;
using System.Globalization;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using HormuzAI.Environment;

namespace HormuzAI.Editor
{
    /// <summary>
    /// 호르무즈 해협 Stage 1 씬을 자동으로 생성하는 Editor 도구.
    /// 메뉴: Hormuz > Build Scene
    /// </summary>
    public static class HormuzSceneBuilder
    {
        // ── 경로 상수 ──────────────────────────────────────
        private const string SCENE_PATH       = "Assets/Scenes/HormuzStage1.unity";
        private const string TERRAIN_ASSET    = "Assets/Terrain/HormuzTerrainData.asset";
        private const string WATER_MAT_ASSET  = "Assets/Materials/WaterMaterial.mat";
        private const string HEIGHTMAP_RAW    = "Terrain/heightmap_hormuz.raw";   // Assets/ 상대
        private const string HEIGHTMAP_META   = "Terrain/heightmap_meta.txt";

        // ── Terrain 규격 ────────────────────────────────────
        private const float TERRAIN_W  = 40000f;   // 40 km (X)
        private const float TERRAIN_H  = 500f;     // 고도 범위 (Y)
        private const float TERRAIN_D  = 20000f;   // 20 km (Z)
        private const int   HM_RES     = 1025;     // heightmapResolution (2ⁿ + 1)
        // ───────────────────────────────────────────────────

        [MenuItem("Hormuz/Build Scene")]
        public static void BuildScene()
        {
            EnsureDirectories();
            SetupTagsAndLayers();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root  = new GameObject("HormuzScene");

            CreateTerrain(root);
            CreateWaterPlane(root);
            CreateSpawnPoints(root);
            CreateGoalTrigger(root);
            CreateBoundaryWalls(root);

            EditorSceneManager.SaveScene(scene, SCENE_PATH);
            AssetDatabase.Refresh();
            Debug.Log("[HormuzSceneBuilder] HormuzStage1 씬 생성 완료.");
        }

        // ── 초기화 ─────────────────────────────────────────

        private static void EnsureDirectories()
        {
            string dataPath = Application.dataPath;
            foreach (var rel in new[] { "Scenes", "Terrain", "Materials", "Scripts/Environment", "Scripts/Editor" })
                Directory.CreateDirectory(Path.Combine(dataPath, rel));
        }

        private static void SetupTagsAndLayers()
        {
            EnsureTag("Goal");
            EnsureTag("Boundary");
            EnsureLayer("Water");
            EnsureLayer("Terrain");
        }

        private static void EnsureTag(string tagName)
        {
            var so = new SerializedObject(
                AssetDatabase.LoadMainAssetAtPath("ProjectSettings/TagManager.asset"));
            var tagsProp = so.FindProperty("tags");
            for (int i = 0; i < tagsProp.arraySize; i++)
                if (tagsProp.GetArrayElementAtIndex(i).stringValue == tagName) return;
            int idx = tagsProp.arraySize;
            tagsProp.InsertArrayElementAtIndex(idx);
            tagsProp.GetArrayElementAtIndex(idx).stringValue = tagName;
            so.ApplyModifiedProperties();
        }

        private static void EnsureLayer(string layerName)
        {
            var so = new SerializedObject(
                AssetDatabase.LoadMainAssetAtPath("ProjectSettings/TagManager.asset"));
            var layersProp = so.FindProperty("layers");
            for (int i = 8; i < layersProp.arraySize; i++)
            {
                var prop = layersProp.GetArrayElementAtIndex(i);
                if (prop.stringValue == layerName) return;
                if (!string.IsNullOrEmpty(prop.stringValue)) continue;
                prop.stringValue = layerName;
                so.ApplyModifiedProperties();
                return;
            }
            Debug.LogWarning($"[HormuzSceneBuilder] Layer 슬롯 부족: '{layerName}' 추가 실패.");
        }

        // ── Terrain ────────────────────────────────────────

        private static void CreateTerrain(GameObject parent)
        {
            var td = new TerrainData
            {
                heightmapResolution = HM_RES,
                size = new Vector3(TERRAIN_W, TERRAIN_H, TERRAIN_D)
            };
            td.SetDetailResolution(512, 16);

            if (File.Exists(Path.Combine(Application.dataPath, "..", TERRAIN_ASSET)))
                AssetDatabase.DeleteAsset(TERRAIN_ASSET);
            AssetDatabase.CreateAsset(td, TERRAIN_ASSET);

            var terrainGO = Terrain.CreateTerrainGameObject(td);
            terrainGO.name = "Terrain";
            terrainGO.transform.SetParent(parent.transform);
            terrainGO.transform.position = Vector3.zero;
            terrainGO.layer = LayerMask.NameToLayer("Terrain");

            ImportHeightmap(td);
        }

        private static void ImportHeightmap(TerrainData td)
        {
            string rawPath = Path.Combine(Application.dataPath, HEIGHTMAP_RAW);
            if (!File.Exists(rawPath))
            {
                Debug.LogWarning("[HormuzSceneBuilder] 하이트맵 없음 — tools/generate_heightmap.py 를 먼저 실행하세요.");
                return;
            }

            byte[] bytes = File.ReadAllBytes(rawPath);
            int res = td.heightmapResolution;
            float[,] heights = new float[res, res];

            for (int y = 0; y < res; y++)
                for (int x = 0; x < res; x++)
                {
                    int idx = (y * res + x) * 2;
                    if (idx + 1 >= bytes.Length) continue;
                    ushort raw = (ushort)((bytes[idx] << 8) | bytes[idx + 1]); // big-endian
                    heights[y, x] = raw / 65535f;
                }

            td.SetHeights(0, 0, heights);
            Debug.Log("[HormuzSceneBuilder] 하이트맵 임포트 완료.");
        }

        // ── WaterPlane ─────────────────────────────────────

        private static void CreateWaterPlane(GameObject parent)
        {
            float seaY = ReadSeaLevel();

            var water = GameObject.CreatePrimitive(PrimitiveType.Plane);
            water.name = "WaterPlane";
            water.transform.SetParent(parent.transform);
            water.transform.localScale = new Vector3(TERRAIN_W / 10f, 1f, TERRAIN_D / 10f);
            water.transform.position   = new Vector3(TERRAIN_W / 2f, seaY, TERRAIN_D / 2f);
            water.layer = LayerMask.NameToLayer("Water");
            Object.DestroyImmediate(water.GetComponent<Collider>());

            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"))
            {
                color = new Color(0.08f, 0.37f, 0.68f, 1f)
            };
            AssetDatabase.CreateAsset(mat, WATER_MAT_ASSET);
            water.GetComponent<Renderer>().sharedMaterial = mat;
        }

        private static float ReadSeaLevel()
        {
            string metaPath = Path.Combine(Application.dataPath, HEIGHTMAP_META);
            if (!File.Exists(metaPath)) return 125f; // 기본값: Terrain Height의 25%

            foreach (var line in File.ReadAllLines(metaPath))
            {
                if (!line.StartsWith("Sea Level Y")) continue;
                var parts = line.Split(':');
                if (parts.Length == 2 &&
                    float.TryParse(parts[1].Trim(), NumberStyles.Float,
                                   CultureInfo.InvariantCulture, out float y))
                    return y;
            }
            return 125f;
        }

        // ── SpawnPoints ────────────────────────────────────

        private static void CreateSpawnPoints(GameObject parent)
        {
            float seaY = ReadSeaLevel();

            var spawnRoot = new GameObject("SpawnPoints");
            spawnRoot.transform.SetParent(parent.transform);
            spawnRoot.AddComponent<SpawnManager>();

            // SpawnPoint_0: 서쪽 입구, 수로 중앙
            var sp0 = new GameObject("SpawnPoint_0");
            sp0.transform.SetParent(spawnRoot.transform);
            sp0.transform.position = new Vector3(1000f, seaY + 1f, TERRAIN_D / 2f);
        }

        // ── GoalTrigger ────────────────────────────────────

        private static void CreateGoalTrigger(GameObject parent)
        {
            float seaY = ReadSeaLevel();

            var goal = new GameObject("GoalTrigger");
            goal.tag = "Goal";
            goal.transform.SetParent(parent.transform);
            goal.transform.position = new Vector3(TERRAIN_W - 1000f, seaY + 10f, TERRAIN_D / 2f);

            var col = goal.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.size = new Vector3(200f, 100f, TERRAIN_D * 0.6f);
        }

        // ── BoundaryWalls ──────────────────────────────────

        private static void CreateBoundaryWalls(GameObject parent)
        {
            var walls = new GameObject("BoundaryWalls");
            walls.transform.SetParent(parent.transform);

            AddWall(walls, "Wall_North",
                new Vector3(TERRAIN_W / 2f, TERRAIN_H / 2f, TERRAIN_D + 100f),
                new Vector3(TERRAIN_W + 400f, TERRAIN_H, 200f));

            AddWall(walls, "Wall_South",
                new Vector3(TERRAIN_W / 2f, TERRAIN_H / 2f, -100f),
                new Vector3(TERRAIN_W + 400f, TERRAIN_H, 200f));

            AddWall(walls, "Wall_East",
                new Vector3(TERRAIN_W + 100f, TERRAIN_H / 2f, TERRAIN_D / 2f),
                new Vector3(200f, TERRAIN_H, TERRAIN_D + 400f));

            AddWall(walls, "Wall_West",
                new Vector3(-100f, TERRAIN_H / 2f, TERRAIN_D / 2f),
                new Vector3(200f, TERRAIN_H, TERRAIN_D + 400f));
        }

        private static void AddWall(GameObject parent, string wallName, Vector3 pos, Vector3 size)
        {
            var wall = new GameObject(wallName);
            wall.tag = "Boundary";
            wall.transform.SetParent(parent.transform);
            wall.transform.position = pos;
            wall.AddComponent<BoxCollider>().size = size;
        }
    }
}
```

- [ ] **Step 2: Unity 에디터에서 컴파일 확인**

Unity 에디터 포커스 → Console 오류 없음 확인.  
메뉴바에 **Hormuz** 메뉴 항목 나타나는지 확인.

- [ ] **Step 3: 커밋**

```bash
git add UnityProject/Assets/Scripts/Editor/HormuzSceneBuilder.cs
git commit -m "feat: add HormuzSceneBuilder editor tool for automated scene creation"
```

---

### Task 6: 씬 빌드 및 검증

**Files:**
- Creates: `UnityProject/Assets/Scenes/HormuzStage1.unity`
- Creates: `UnityProject/Assets/Terrain/HormuzTerrainData.asset`
- Creates: `UnityProject/Assets/Materials/WaterMaterial.mat`

- [ ] **Step 1: Build Scene 실행**

Unity 메뉴바 → **Hormuz > Build Scene** 클릭.

Expected Console 출력:
```
[HormuzSceneBuilder] 하이트맵 임포트 완료.
[HormuzSceneBuilder] HormuzStage1 씬 생성 완료.
```

- [ ] **Step 2: Hierarchy 구조 확인**

```
HormuzScene
├── Terrain
├── WaterPlane
├── SpawnPoints
│   └── SpawnPoint_0
├── GoalTrigger
└── BoundaryWalls
    ├── Wall_North
    ├── Wall_South
    ├── Wall_East
    └── Wall_West
```

- [ ] **Step 3: Scene View 시각 검증**

다음 항목을 Scene View에서 육안 확인:
- Terrain에 지형 기복이 있고, 수로 부분이 육지보다 낮음
- WaterPlane(파란 평면)이 수로 위에 위치함
- SpawnPoint_0 이 서쪽(X≈1000) 수로 중앙(Z≈10000)에 있음
- GoalTrigger 가 동쪽(X≈39000)에 있음

- [ ] **Step 4: 커밋**

```bash
git add UnityProject/Assets/Scenes/HormuzStage1.unity
git add UnityProject/Assets/Terrain/HormuzTerrainData.asset
git add UnityProject/Assets/Materials/WaterMaterial.mat
git commit -m "feat: build HormuzStage1 scene with real-world Hormuz Strait terrain"
```
