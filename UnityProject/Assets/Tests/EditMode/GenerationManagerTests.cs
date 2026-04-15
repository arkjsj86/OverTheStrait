using NUnit.Framework;
using System.IO;
using System.Reflection;
using UnityEngine;
using HormuzAI.Environment;

namespace HormuzAI.Tests
{
    public class GenerationManagerTests
    {
        GameObject _go;
        string _tmpCsv;

        [SetUp]
        public void SetUp()
        {
            _go     = new GameObject("GM");
            _tmpCsv = Path.Combine(Path.GetTempPath(), $"test_gen_{System.Guid.NewGuid()}.csv");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_go);
            if (File.Exists(_tmpCsv)) File.Delete(_tmpCsv);
        }

        GenerationManager CreateGM(int agentsPerGen, string csvPath = null)
        {
            var gm   = _go.AddComponent<GenerationManager>();
            var bind = BindingFlags.NonPublic | BindingFlags.Instance;
            var t    = typeof(GenerationManager);
            t.GetField("agentsPerGeneration", bind).SetValue(gm, agentsPerGen);
            t.GetField("_csvPath",            bind).SetValue(gm, csvPath ?? _tmpCsv);
            t.GetField("_currentGeneration",  bind).SetValue(gm, 1);
            // InitGeneration 호출로 집계 변수 초기화
            t.GetMethod("InitGeneration", bind).Invoke(gm, null);
            return gm;
        }

        [Test]
        public void ReportEpisodeEnd_CountsGoalReached()
        {
            var gm = CreateGM(4);
            gm.ReportEpisodeEnd(true,  1f);
            gm.ReportEpisodeEnd(false, -0.5f);
            gm.ReportEpisodeEnd(false, -0.1f);

            var bind = BindingFlags.NonPublic | BindingFlags.Instance;
            int goalCount = (int)typeof(GenerationManager)
                .GetField("_goalReachedCount", bind).GetValue(gm);
            Assert.AreEqual(1, goalCount);
        }

        [Test]
        public void ReportEpisodeEnd_TracksBestReward()
        {
            var gm = CreateGM(4);
            gm.ReportEpisodeEnd(false, -0.5f);
            gm.ReportEpisodeEnd(true,   1f);
            gm.ReportEpisodeEnd(false, -0.1f);

            var bind = BindingFlags.NonPublic | BindingFlags.Instance;
            float best = (float)typeof(GenerationManager)
                .GetField("_bestReward", bind).GetValue(gm);
            Assert.AreEqual(1f, best, 0.0001f);
        }

        [Test]
        public void EndGeneration_WritesCSVRow()
        {
            var gm = CreateGM(2, _tmpCsv);
            gm.ReportEpisodeEnd(true,  1f);
            gm.ReportEpisodeEnd(false, -0.5f);
            // agentsPerGeneration=2 이므로 EndGeneration 자동 호출됨

            Assert.IsTrue(File.Exists(_tmpCsv), "CSV 파일이 생성되지 않음");
            var lines = File.ReadAllLines(_tmpCsv);
            Assert.GreaterOrEqual(lines.Length, 2, "헤더 + 데이터 행 최소 2줄 필요");
            StringAssert.Contains("1,", lines[1], "세대 번호 1이 포함되어야 함");
        }

        [Test]
        public void EndGeneration_IncrementsGenerationNumber()
        {
            var gm = CreateGM(2, _tmpCsv);
            gm.ReportEpisodeEnd(true,  1f);
            gm.ReportEpisodeEnd(false, -0.5f);

            var bind = BindingFlags.NonPublic | BindingFlags.Instance;
            int gen = (int)typeof(GenerationManager)
                .GetField("_currentGeneration", bind).GetValue(gm);
            Assert.AreEqual(2, gen);
        }
    }
}
