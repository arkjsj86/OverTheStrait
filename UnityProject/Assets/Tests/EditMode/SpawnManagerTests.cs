using NUnit.Framework;
using UnityEngine;
using HormuzAI.Environment;

public class SpawnManagerTests
{
    GameObject _root;

    [SetUp]
    public void SetUp() => _root = new GameObject("Root");

    [TearDown]
    public void TearDown() => Object.DestroyImmediate(_root);

    SpawnManager CreateSpawnManager(Vector3 center, float radius = 500f)
    {
        var child = new GameObject("SP0");
        child.transform.SetParent(_root.transform);
        child.transform.position = center;

        var sm = _root.AddComponent<SpawnManager>();
        typeof(SpawnManager)
            .GetField("spawnRadius",
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance)
            .SetValue(sm, radius);
        return sm;
    }

    [Test]
    public void GetRandomSpawnPosition_ReturnsWithinRadius()
    {
        var center = new Vector3(1000f, 10.68f, 39000f);
        var sm = CreateSpawnManager(center, 500f);

        for (int i = 0; i < 50; i++)
        {
            Vector3 pos = sm.GetRandomSpawnPosition();
            float xzDist = Vector2.Distance(
                new Vector2(pos.x, pos.z),
                new Vector2(center.x, center.z));
            Assert.LessOrEqual(xzDist, 500f + 0.01f,
                $"Iteration {i}: 반경 초과 ({xzDist:F2}m)");
        }
    }

    [Test]
    public void GetRandomSpawnPosition_PreservesYCoordinate()
    {
        var center = new Vector3(1000f, 10.68f, 39000f);
        var sm = CreateSpawnManager(center);

        for (int i = 0; i < 10; i++)
        {
            Vector3 pos = sm.GetRandomSpawnPosition();
            Assert.AreEqual(center.y, pos.y, 0.0001f,
                $"Iteration {i}: Y 좌표 변경됨");
        }
    }

    [Test]
    public void GetRandomSpawnPosition_FallsBackToTransformWhenNoChildren()
    {
        _root.transform.position = new Vector3(500f, 5f, 1000f);
        var sm = _root.AddComponent<SpawnManager>();
        typeof(SpawnManager)
            .GetField("spawnRadius",
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance)
            .SetValue(sm, 100f);

        Vector3 pos = sm.GetRandomSpawnPosition();
        float xzDist = Vector2.Distance(
            new Vector2(pos.x, pos.z),
            new Vector2(500f, 1000f));
        Assert.LessOrEqual(xzDist, 100f + 0.01f);
    }
}
