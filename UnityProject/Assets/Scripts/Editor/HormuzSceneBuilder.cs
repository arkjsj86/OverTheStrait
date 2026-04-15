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
        private const string TERRAIN_MAT      = "Assets/Terrain/TerrainMaterial.asset";
        private const string WATER_MAT_ASSET  = "Assets/Materials/WaterMaterial.mat";
        private const string HEIGHTMAP_RAW    = "Terrain/heightmap_hormuz.raw";   // Assets/ 상대
        private const string HEIGHTMAP_META   = "Terrain/heightmap_meta.txt";

        // ── Terrain 규격 ────────────────────────────────────
        private const float TERRAIN_W  = 56000f;   // 56 km (X, 페르시아만→오만만 전체 수로)
        private const float TERRAIN_H  = 2000f;    // 고도 범위 (Y, 시각적 과장 ×4)
        private const float TERRAIN_D  = 40000f;   // 40 km (Z)
        private const int   HM_RES     = 1025;     // heightmapResolution (2ⁿ + 1)
        // ───────────────────────────────────────────────────

        [MenuItem("Hormuz/Clear Scene")]
        public static void ClearScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, SCENE_PATH);
            AssetDatabase.Refresh();
            Debug.Log("[HormuzSceneBuilder] 씬 초기화 완료.");
        }

        [MenuItem("Hormuz/Build Scene")]
        public static void BuildScene()
        {
            EnsureDirectories();
            SetupTagsAndLayers();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root  = new GameObject("HormuzScene");

            float seaY = ReadSeaLevel();
            CreateTerrain(root);
            CreateWaterPlane(root, seaY);
            CreateSpawnPoints(root, seaY);
            CreateGoalTrigger(root, seaY);
            CreateBoundaryWalls(root);
            CreateOverviewCamera(root);

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
            ApplyTerrainMaterial(terrainGO);
        }

        /// <summary>Terrain 컴포넌트에 모래색 material을 직접 지정한다.</summary>
        private static void ApplyTerrainMaterial(GameObject terrainGO)
        {
            if (AssetDatabase.LoadAssetAtPath<Material>(TERRAIN_MAT) != null)
                AssetDatabase.DeleteAsset(TERRAIN_MAT);

            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"))
            {
                color = new Color(0.80f, 0.68f, 0.42f)   // 모래색
            };
            AssetDatabase.CreateAsset(mat, TERRAIN_MAT);
            AssetDatabase.SaveAssets();

            terrainGO.GetComponent<Terrain>().materialTemplate = mat;
            Debug.Log("[HormuzSceneBuilder] 지형 모래색 material 적용 완료.");
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

        private static void CreateWaterPlane(GameObject parent, float seaY)
        {
            var water = GameObject.CreatePrimitive(PrimitiveType.Plane);
            water.name = "WaterPlane";
            water.transform.SetParent(parent.transform);
            water.transform.localScale = new Vector3(TERRAIN_W / 10f, 1f, TERRAIN_D / 10f);
            water.transform.position   = new Vector3(TERRAIN_W / 2f, seaY, TERRAIN_D / 2f);
            water.layer = LayerMask.NameToLayer("Water");
            Object.DestroyImmediate(water.GetComponent<Collider>());

            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"))
            {
                color = new Color(0.0f, 0.55f, 0.85f, 1f)  // 밝은 청록색
            };
            if (File.Exists(Path.Combine(Application.dataPath, "..", WATER_MAT_ASSET)))
                AssetDatabase.DeleteAsset(WATER_MAT_ASSET);
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

        private static void CreateSpawnPoints(GameObject parent, float seaY)
        {
            var spawnRoot = new GameObject("SpawnPoints");
            spawnRoot.transform.SetParent(parent.transform);
            spawnRoot.AddComponent<SpawnManager>();

            var sp0 = new GameObject("SpawnPoint_0");
            sp0.transform.SetParent(spawnRoot.transform);
            sp0.transform.position = new Vector3(1000f, seaY, 39000f);
        }

        // ── GoalTrigger ────────────────────────────────────

        private static void CreateGoalTrigger(GameObject parent, float seaY)
        {
            var goal = new GameObject("GoalTrigger");
            goal.tag = "Goal";
            goal.transform.SetParent(parent.transform);
            goal.transform.position = new Vector3(55025f, seaY, 19025f);

            var col = goal.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.size = new Vector3(200f, 100f, 15000f);
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

        // ── OverviewCamera ─────────────────────────────────────────────────

        private static void CreateOverviewCamera(GameObject parent)
        {
            var camGO = new GameObject("OverviewCamera");
            camGO.transform.SetParent(parent.transform);

            var cam = camGO.AddComponent<Camera>();
            cam.orthographic     = true;
            cam.orthographicSize = 22000f;   // 세로 44km — 지형 40km 커버
            cam.farClipPlane     = 15000f;
            cam.backgroundColor  = new Color(0.02f, 0.05f, 0.10f);  // 거의 검정 — 씬 경계 밖
            cam.clearFlags       = CameraClearFlags.SolidColor;
            camGO.tag = "MainCamera";

            // 지형 정중앙 상공, 수직 하향
            // X: 56000/2=28000  Y: 12000  Z: 40000/2=20000
            camGO.transform.SetPositionAndRotation(
                new Vector3(TERRAIN_W / 2f, 12000f, TERRAIN_D / 2f),
                Quaternion.Euler(90f, 0f, 0f));

            Debug.Log("[HormuzSceneBuilder] OverviewCamera 생성 완료.");
        }
    }
}
