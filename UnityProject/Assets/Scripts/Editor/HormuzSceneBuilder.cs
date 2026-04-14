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
