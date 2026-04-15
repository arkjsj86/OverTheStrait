using NUnit.Framework;
using System.Reflection;
using UnityEngine;
using HormuzAI.Agent;
using HormuzAI.Data;
using HormuzAI.Environment;

namespace HormuzAI.Tests
{
    public class ShipAgentSetRefsTests
    {
        GameObject _agentGO;
        GameObject _smGO;
        GameObject _goalGO;
        GameObject _gmGO;

        [SetUp]
        public void SetUp()
        {
            _agentGO = new GameObject("Agent");
            _agentGO.AddComponent<Rigidbody>();

            _smGO = new GameObject("SpawnManager");
            new GameObject("SP0").transform.SetParent(_smGO.transform);

            _goalGO = new GameObject("Goal");
            _gmGO   = new GameObject("GM");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_agentGO);
            Object.DestroyImmediate(_smGO);
            Object.DestroyImmediate(_goalGO);
            Object.DestroyImmediate(_gmGO);
        }

        static object GetField(object obj, string name)
        {
            var flags = BindingFlags.NonPublic | BindingFlags.Instance;
            return obj.GetType().GetField(name, flags)?.GetValue(obj);
        }

        [Test]
        public void SetRefs_AssignsSpawnManager()
        {
            var agent = _agentGO.AddComponent<ShipAgent>();
            var sm    = _smGO.AddComponent<SpawnManager>();
            var stats = ScriptableObject.CreateInstance<ShipStatsSO>();
            var gm    = _gmGO.AddComponent<GenerationManager>();

            agent.SetRefs(sm, _goalGO.transform, stats, gm);

            Assert.AreEqual(sm, GetField(agent, "spawnManager"));
            Object.DestroyImmediate(stats);
        }

        [Test]
        public void SetRefs_AssignsGoal()
        {
            var agent = _agentGO.AddComponent<ShipAgent>();
            var sm    = _smGO.AddComponent<SpawnManager>();
            var stats = ScriptableObject.CreateInstance<ShipStatsSO>();
            var gm    = _gmGO.AddComponent<GenerationManager>();

            agent.SetRefs(sm, _goalGO.transform, stats, gm);

            Assert.AreEqual(_goalGO.transform, GetField(agent, "goal"));
            Object.DestroyImmediate(stats);
        }

        [Test]
        public void SetRefs_AssignsGenerationManager()
        {
            var agent = _agentGO.AddComponent<ShipAgent>();
            var sm    = _smGO.AddComponent<SpawnManager>();
            var stats = ScriptableObject.CreateInstance<ShipStatsSO>();
            var gm    = _gmGO.AddComponent<GenerationManager>();

            agent.SetRefs(sm, _goalGO.transform, stats, gm);

            Assert.AreEqual(gm, GetField(agent, "generationManager"));
            Object.DestroyImmediate(stats);
        }
    }
}
