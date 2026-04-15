// UnityProject/Assets/Tests/EditMode/ShipStatsSOTests.cs
using NUnit.Framework;
using UnityEngine;
using HormuzAI.Data;

namespace HormuzAI.Tests
{
    public class ShipStatsSOTests
    {
        ShipStatsSO _stats;

        [SetUp]
        public void SetUp()
        {
            _stats = ScriptableObject.CreateInstance<ShipStatsSO>();
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_stats);

        // ── GetDepthMult ────────────────────────────────────────────────

        [Test]
        public void GetDepthMult_ShallowRatio_ReturnsShallowMult()
        {
            Assert.AreEqual(_stats.shallowSpeedMult, _stats.GetDepthMult(0.1f), 0.001f);
        }

        [Test]
        public void GetDepthMult_NormalRatio_ReturnsOne()
        {
            Assert.AreEqual(1.0f, _stats.GetDepthMult(0.5f), 0.001f);
        }

        [Test]
        public void GetDepthMult_DeepRatio_ReturnsDeepMult()
        {
            Assert.AreEqual(_stats.deepSpeedMult, _stats.GetDepthMult(0.9f), 0.001f);
        }

        // ── GetWidthMult ────────────────────────────────────────────────

        [Test]
        public void GetWidthMult_NarrowRatio_ReturnsNarrowMult()
        {
            Assert.AreEqual(_stats.narrowTurnMult, _stats.GetWidthMult(0.3f), 0.001f);
        }

        [Test]
        public void GetWidthMult_WideRatio_ReturnsWideMult()
        {
            Assert.AreEqual(_stats.wideTurnMult, _stats.GetWidthMult(0.7f), 0.001f);
        }

        // ── GetHealthMult ───────────────────────────────────────────────

        [Test]
        public void GetHealthMult_CriticalHealth_ReturnsCriticalMult()
        {
            Assert.AreEqual(_stats.criticalSpeedMult, _stats.GetHealthMult(0.1f), 0.001f);
        }

        [Test]
        public void GetHealthMult_DamagedHealth_ReturnsDamagedMult()
        {
            Assert.AreEqual(_stats.damagedSpeedMult, _stats.GetHealthMult(0.4f), 0.001f);
        }

        [Test]
        public void GetHealthMult_FullHealth_ReturnsOne()
        {
            Assert.AreEqual(1.0f, _stats.GetHealthMult(1.0f), 0.001f);
        }

        // ── GetEffectiveSpeed ───────────────────────────────────────────

        [Test]
        public void GetEffectiveSpeed_DeepAndFullHealth_AppliesDeepMult()
        {
            float expected = _stats.maxSpeed * _stats.deepSpeedMult * 1.0f;
            Assert.AreEqual(expected, _stats.GetEffectiveSpeed(0.9f, 1.0f), 0.001f);
        }

        [Test]
        public void GetEffectiveSpeed_ShallowAndCritical_AppliesBothMults()
        {
            float expected = _stats.maxSpeed * _stats.shallowSpeedMult * _stats.criticalSpeedMult;
            Assert.AreEqual(expected, _stats.GetEffectiveSpeed(0.1f, 0.1f), 0.001f);
        }

        // ── GetEffectiveTurnRate ────────────────────────────────────────

        [Test]
        public void GetEffectiveTurnRate_NarrowChannel_AppliesNarrowMult()
        {
            float expected = _stats.turnRate * _stats.narrowTurnMult;
            Assert.AreEqual(expected, _stats.GetEffectiveTurnRate(0.2f), 0.001f);
        }
    }
}
