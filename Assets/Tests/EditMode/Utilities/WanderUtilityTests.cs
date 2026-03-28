using NUnit.Framework;
using Shared;
using Unity.Mathematics;

namespace Tests.EditMode.Utilities
{
    [TestFixture]
    public class WanderUtilityTests
    {
        #region CheckStuck Tests

        [Test]
        public void CheckStuck_IntervalNotMet_ReturnsFalse()
        {
            var currentPos = new float3(0, 0, 0);
            var lastCheckPos = new float3(0, 0, 0);

            bool performed = WanderUtility.CheckStuck(currentPos, lastCheckPos, 0f, 1f, out bool isStuck);

            Assert.IsFalse(performed);
            Assert.IsFalse(isStuck);
        }

        [Test]
        public void CheckStuck_IntervalMet_NotMoved_IsStuck()
        {
            var currentPos = new float3(5, 0, 5);
            var lastCheckPos = new float3(5, 0, 5.5f); // 0.5m만 이동 (< 2m 임계치)

            bool performed = WanderUtility.CheckStuck(currentPos, lastCheckPos, 0f, 3f, out bool isStuck);

            Assert.IsTrue(performed);
            Assert.IsTrue(isStuck);
        }

        [Test]
        public void CheckStuck_IntervalMet_MovedEnough_NotStuck()
        {
            var currentPos = new float3(5, 0, 5);
            var lastCheckPos = new float3(0, 0, 0); // 약 7m 이동

            bool performed = WanderUtility.CheckStuck(currentPos, lastCheckPos, 0f, 3f, out bool isStuck);

            Assert.IsTrue(performed);
            Assert.IsFalse(isStuck);
        }

        [Test]
        public void CheckStuck_ExactThreshold_NotStuck()
        {
            var currentPos = new float3(2, 0, 0);
            var lastCheckPos = new float3(0, 0, 0); // 정확히 2m

            bool performed = WanderUtility.CheckStuck(currentPos, lastCheckPos, 0f, 3f, out bool isStuck);

            Assert.IsTrue(performed);
            Assert.IsFalse(isStuck); // 2m == threshold → not stuck (< 사용)
        }

        [Test]
        public void CheckStuck_JustUnderThreshold_IsStuck()
        {
            var currentPos = new float3(1.99f, 0, 0);
            var lastCheckPos = new float3(0, 0, 0);

            bool performed = WanderUtility.CheckStuck(currentPos, lastCheckPos, 0f, 3f, out bool isStuck);

            Assert.IsTrue(performed);
            Assert.IsTrue(isStuck);
        }

        [Test]
        public void CheckStuck_ExactInterval_Performs()
        {
            var pos = new float3(0, 0, 0);

            // elapsedTime - lastCheckTime = 3.0 == StuckCheckInterval → 3.0 < 3.0 은 false → early return 안 함 → 체크 수행
            bool performed = WanderUtility.CheckStuck(pos, pos, 0f, 3.0f, out _);

            Assert.IsTrue(performed);
        }

        [Test]
        public void CheckStuck_JustOverInterval_Performs()
        {
            var pos = new float3(0, 0, 0);

            bool performed = WanderUtility.CheckStuck(pos, pos, 0f, 3.01f, out _);

            Assert.IsTrue(performed);
        }

        #endregion

        #region CalculateDormantWakeTime Tests

        [Test]
        public void CalculateDormantWakeTime_ReturnsWithinRange()
        {
            float elapsedTime = 10f;

            float wakeTime = WanderUtility.CalculateDormantWakeTime(42, elapsedTime);

            float duration = wakeTime - elapsedTime;
            Assert.GreaterOrEqual(duration, WanderUtility.DefaultDormantMinDuration);
            Assert.Less(duration, WanderUtility.DefaultDormantMaxDuration);
        }

        [Test]
        public void CalculateDormantWakeTime_DifferentEntities_DifferentTimes()
        {
            float elapsedTime = 10f;

            float wake1 = WanderUtility.CalculateDormantWakeTime(1, elapsedTime);
            float wake2 = WanderUtility.CalculateDormantWakeTime(2, elapsedTime);

            // 다른 entityIndex → 다른 시드 → (높은 확률로) 다른 결과
            Assert.AreNotEqual(wake1, wake2);
        }

        [Test]
        public void CalculateDormantWakeTime_Deterministic_SameInputSameOutput()
        {
            float wake1 = WanderUtility.CalculateDormantWakeTime(100, 50f);
            float wake2 = WanderUtility.CalculateDormantWakeTime(100, 50f);

            Assert.AreEqual(wake1, wake2);
        }

        [Test]
        public void CalculateDormantWakeTime_AlwaysGreaterThanElapsedTime()
        {
            for (int i = 0; i < 100; i++)
            {
                float elapsed = i * 10f;
                float wakeTime = WanderUtility.CalculateDormantWakeTime(i, elapsed);
                Assert.Greater(wakeTime, elapsed);
            }
        }

        #endregion

        #region GenerateWanderDestination Tests

        [Test]
        public void GenerateWanderDestination_WithinMapBounds()
        {
            var gridSettings = new GridSettings
            {
                GridOrigin = new float2(0, 0),
                GridSize = new int2(100, 100),
                CellSize = 1f
            };

            var pos = new float3(50f, 0f, 50f);
            WanderUtility.GenerateWanderDestination(1, 100, 10f, in pos, gridSettings, out float3 result);

            // 맵 범위: (0,0) ~ (100,100), 내부 마진 5 → (5,5) ~ (95,95)
            Assert.GreaterOrEqual(result.x, 5f);
            Assert.Less(result.x, 95f);
            Assert.GreaterOrEqual(result.z, 5f);
            Assert.Less(result.z, 95f);
        }

        [Test]
        public void GenerateWanderDestination_PreservesY()
        {
            var gridSettings = new GridSettings
            {
                GridOrigin = new float2(0, 0),
                GridSize = new int2(100, 100),
                CellSize = 1f
            };
            float expectedY = 5.5f;
            var pos = new float3(50f, expectedY, 50f);

            WanderUtility.GenerateWanderDestination(1, 100, 10f, in pos, gridSettings, out float3 result);

            Assert.AreEqual(expectedY, result.y, 0.001f);
        }

        [Test]
        public void GenerateWanderDestination_Deterministic()
        {
            var gridSettings = new GridSettings
            {
                GridOrigin = new float2(0, 0),
                GridSize = new int2(100, 100),
                CellSize = 1f
            };

            var pos = new float3(50f, 0f, 50f);
            WanderUtility.GenerateWanderDestination(42, 200, 15f, in pos, gridSettings, out float3 result1);
            WanderUtility.GenerateWanderDestination(42, 200, 15f, in pos, gridSettings, out float3 result2);

            Assert.AreEqual(result1.x, result2.x, 0.001f);
            Assert.AreEqual(result1.z, result2.z, 0.001f);
        }

        [Test]
        public void GenerateWanderDestination_DifferentSeeds_DifferentResults()
        {
            var gridSettings = new GridSettings
            {
                GridOrigin = new float2(0, 0),
                GridSize = new int2(100, 100),
                CellSize = 1f
            };

            var pos = new float3(50f, 0f, 50f);
            WanderUtility.GenerateWanderDestination(1, 100, 10f, in pos, gridSettings, out float3 result1);
            WanderUtility.GenerateWanderDestination(2, 100, 10f, in pos, gridSettings, out float3 result2);

            // 다른 entityIndex → (높은 확률로) 다른 위치
            bool different = math.abs(result1.x - result2.x) > 0.001f ||
                             math.abs(result1.z - result2.z) > 0.001f;
            Assert.IsTrue(different);
        }

        [Test]
        public void GenerateWanderDestination_WithOffset_WithinBounds()
        {
            var gridSettings = new GridSettings
            {
                GridOrigin = new float2(-50, -50),
                GridSize = new int2(200, 200),
                CellSize = 1.0f
            };

            // 맵 범위: (-50,-50) ~ (-50+200*1.0, -50+200*1.0) = (-50,-50) ~ (150,150)
            for (int i = 0; i < 50; i++)
            {
                var pos = new float3(50f, 0f, 50f);
                WanderUtility.GenerateWanderDestination(i, (uint)i * 7, i * 2f, in pos, gridSettings, out float3 result);

                Assert.GreaterOrEqual(result.x, -45f); // -50 + 5
                Assert.Less(result.x, 145f);            // 150 - 5
                Assert.GreaterOrEqual(result.z, -45f);
                Assert.Less(result.z, 145f);
            }
        }

        #endregion

        #region Constants Tests

        [Test]
        public void Constants_HaveExpectedValues()
        {
            Assert.AreEqual(3.0f, WanderUtility.DefaultStuckCheckInterval);
            Assert.AreEqual(2.0f, WanderUtility.DefaultStuckThreshold);
            Assert.AreEqual(5.0f, WanderUtility.DefaultDormantMinDuration);
            Assert.AreEqual(8.0f, WanderUtility.DefaultDormantMaxDuration);
        }

        #endregion
    }
}
