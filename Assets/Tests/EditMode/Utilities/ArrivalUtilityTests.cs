using NUnit.Framework;
using Shared;
using Unity.Mathematics;

namespace Tests.EditMode.Utilities
{
    [TestFixture]
    public class ArrivalUtilityTests
    {
        #region CalculateApproachPoint Tests

        [Test]
        public void CalculateApproachPoint_NormalDirection_ReturnsCorrectPoint()
        {
            var from = new float3(0, 0, 0);
            var target = new float3(10, 0, 0);
            float standoff = 2f;

            float3 result = ArrivalUtility.CalculateApproachPoint(from, target, standoff);

            Assert.AreEqual(8f, result.x, 0.001f);
            Assert.AreEqual(0f, result.z, 0.001f);
        }

        [Test]
        public void CalculateApproachPoint_SamePosition_ReturnsTarget()
        {
            var pos = new float3(5, 0, 5);

            float3 result = ArrivalUtility.CalculateApproachPoint(pos, pos, 2f);

            Assert.AreEqual(pos.x, result.x, 0.001f);
            Assert.AreEqual(pos.z, result.z, 0.001f);
        }

        [Test]
        public void CalculateApproachPoint_DiagonalDirection_ReturnsCorrectPoint()
        {
            var from = new float3(0, 0, 0);
            var target = new float3(10, 0, 10);
            float standoff = math.sqrt(2f); // 대각선 방향 단위벡터 * standoff

            float3 result = ArrivalUtility.CalculateApproachPoint(from, target, standoff);

            // 방향: (1,0,1)/sqrt(2), standoff만큼 뒤로
            float expected = 10f - 1f; // 10 - standoff/sqrt(2) * sqrt(2) = 10 - standoff
            Assert.AreEqual(expected, result.x, 0.01f);
            Assert.AreEqual(expected, result.z, 0.01f);
        }

        [Test]
        public void CalculateApproachPoint_NegativeDirection_ReturnsCorrectPoint()
        {
            var from = new float3(10, 0, 0);
            var target = new float3(0, 0, 0);
            float standoff = 3f;

            float3 result = ArrivalUtility.CalculateApproachPoint(from, target, standoff);

            // target(0,0,0)에서 from 방향으로 3만큼 → (3, 0, 0)
            Assert.AreEqual(3f, result.x, 0.001f);
        }

        [Test]
        public void CalculateApproachPoint_ZeroStandoff_ReturnsTarget()
        {
            var from = new float3(0, 0, 0);
            var target = new float3(10, 0, 0);

            float3 result = ArrivalUtility.CalculateApproachPoint(from, target, 0f);

            Assert.AreEqual(target.x, result.x, 0.001f);
        }

        #endregion

        #region GetInteractionArrivalDistance Tests

        [Test]
        public void GetInteractionArrivalDistance_ReturnsSum()
        {
            float result = ArrivalUtility.GetInteractionArrivalDistance(2f, 3f);

            Assert.AreEqual(5f, result, 0.001f);
        }

        [Test]
        public void GetInteractionArrivalDistance_ZeroRadius_ReturnsRange()
        {
            float result = ArrivalUtility.GetInteractionArrivalDistance(0f, 1.5f);

            Assert.AreEqual(1.5f, result, 0.001f);
        }

        [Test]
        public void GetInteractionArrivalDistance_ZeroRange_ReturnsRadius()
        {
            float result = ArrivalUtility.GetInteractionArrivalDistance(2.5f, 0f);

            Assert.AreEqual(2.5f, result, 0.001f);
        }

        #endregion

        #region GetSafeArrivalRadius Tests

        [Test]
        public void GetSafeArrivalRadius_DefaultMargin_ReturnsCorrectValue()
        {
            float interactionRange = 2f;

            float result = ArrivalUtility.GetSafeArrivalRadius(interactionRange);

            // (2 - 0.1) * 0.5 = 0.95
            Assert.AreEqual(0.95f, result, 0.001f);
        }

        [Test]
        public void GetSafeArrivalRadius_CustomMargin_ReturnsCorrectValue()
        {
            float result = ArrivalUtility.GetSafeArrivalRadius(3f, 0.5f);

            // (3 - 0.5) * 0.5 = 1.25
            Assert.AreEqual(1.25f, result, 0.001f);
        }

        [Test]
        public void GetSafeArrivalRadius_SmallRange_ReturnsSmallRadius()
        {
            float result = ArrivalUtility.GetSafeArrivalRadius(0.2f);

            // (0.2 - 0.1) * 0.5 = 0.05
            Assert.AreEqual(0.05f, result, 0.001f);
        }

        #endregion

        #region IsWithinInteractionRange Tests

        [Test]
        public void IsWithinInteractionRange_InsideRange_ReturnsTrue()
        {
            var unit = new float3(0, 0, 0);
            var target = new float3(3, 0, 0);

            bool result = ArrivalUtility.IsWithinInteractionRange(unit, target, 5f);

            Assert.IsTrue(result);
        }

        [Test]
        public void IsWithinInteractionRange_ExactlyAtRange_ReturnsTrue()
        {
            var unit = new float3(0, 0, 0);
            var target = new float3(5, 0, 0);

            bool result = ArrivalUtility.IsWithinInteractionRange(unit, target, 5f);

            Assert.IsTrue(result);
        }

        [Test]
        public void IsWithinInteractionRange_OutsideRange_ReturnsFalse()
        {
            var unit = new float3(0, 0, 0);
            var target = new float3(10, 0, 0);

            bool result = ArrivalUtility.IsWithinInteractionRange(unit, target, 5f);

            Assert.IsFalse(result);
        }

        [Test]
        public void IsWithinInteractionRange_3DDistance_IncludesYAxis()
        {
            var unit = new float3(0, 0, 0);
            var target = new float3(3, 4, 0); // 3D 거리 = 5

            bool result = ArrivalUtility.IsWithinInteractionRange(unit, target, 4.9f);

            Assert.IsFalse(result);
        }

        #endregion

        #region IsWithinInteractionRangeXZ Tests

        [Test]
        public void IsWithinInteractionRangeXZ_IgnoresYAxis()
        {
            var unit = new float3(0, 0, 0);
            var target = new float3(3, 100, 0); // Y 차이 크지만 XZ 거리는 3

            bool result = ArrivalUtility.IsWithinInteractionRangeXZ(unit, target, 5f);

            Assert.IsTrue(result);
        }

        [Test]
        public void IsWithinInteractionRangeXZ_OutsideXZRange_ReturnsFalse()
        {
            var unit = new float3(0, 0, 0);
            var target = new float3(4, 0, 4); // XZ 거리 = sqrt(32) ≈ 5.66

            bool result = ArrivalUtility.IsWithinInteractionRangeXZ(unit, target, 5f);

            Assert.IsFalse(result);
        }

        [Test]
        public void IsWithinInteractionRangeXZ_SamePosition_ReturnsTrue()
        {
            var pos = new float3(5, 3, 5);

            bool result = ArrivalUtility.IsWithinInteractionRangeXZ(pos, pos, 0f);

            Assert.IsTrue(result);
        }

        #endregion
    }
}
