using NUnit.Framework;
using System.Reflection;
using UnityEngine;
using HormuzAI.Agent;
using HormuzAI.Data;
using HormuzAI.Environment;

namespace HormuzAI.Tests
{
    public class AgentPopulatorTests
    {
        GameObject _root;
        ShipStatsSO _stats;

        [SetUp]
        public void SetUp()
        {
            _root  = new GameObject("Root");
            _stats = ScriptableObject.CreateInstance<ShipStatsSO>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
            Object.DestroyImmediate(_stats);
        }

        AgentPopulator CreatePopulator(int count)
        {
            var smGO = new GameObject("SpawnManager");
            smGO.transform.SetParent(_root.transform);
            var sp = new GameObject("SP0");
            sp.transform.SetParent(smGO.transform);
            sp.transform.position = new Vector3(1000f, 10.68f, 39000f);
            var sm = smGO.AddComponent<SpawnManager>();

            var goalGO = new GameObject("Goal");
            goalGO.transform.SetParent(_root.transform);

            var gmGO = new GameObject("GM");
            gmGO.transform.SetParent(_root.transform);
            var gm = gmGO.AddComponent<GenerationManager>();

            var popGO = new GameObject("AgentPopulator");
            popGO.transform.SetParent(_root.transform);
            var pop = popGO.AddComponent<AgentPopulator>();

            var bind = BindingFlags.NonPublic | BindingFlags.Instance;
            var t    = typeof(AgentPopulator);
            t.GetField("agentCount",        bind).SetValue(pop, count);
            t.GetField("stats",             bind).SetValue(pop, _stats);
            t.GetField("spawnManager",      bind).SetValue(pop, sm);
            t.GetField("goal",              bind).SetValue(pop, goalGO.transform);
            t.GetField("generationManager", bind).SetValue(pop, gm);

            pop.SpawnAgents();
            return pop;
        }

        [Test]
        public void SpawnAgents_CreatesCorrectChildCount()
        {
            var pop = CreatePopulator(5);
            Assert.AreEqual(5, pop.transform.childCount);
        }

        [Test]
        public void SpawnAgents_EachChildHasShipAgent()
        {
            var pop = CreatePopulator(3);
            for (int i = 0; i < pop.transform.childCount; i++)
                Assert.IsNotNull(pop.transform.GetChild(i).GetComponent<ShipAgent>(),
                    $"Child {i} has no ShipAgent");
        }

        [Test]
        public void SpawnAgents_EachChildHasRigidbodyWithNoGravity()
        {
            var pop = CreatePopulator(3);
            for (int i = 0; i < pop.transform.childCount; i++)
            {
                var rb = pop.transform.GetChild(i).GetComponent<Rigidbody>();
                Assert.IsNotNull(rb, $"Child {i} has no Rigidbody");
                Assert.IsFalse(rb.useGravity, $"Child {i} useGravity should be false");
            }
        }
    }
}
