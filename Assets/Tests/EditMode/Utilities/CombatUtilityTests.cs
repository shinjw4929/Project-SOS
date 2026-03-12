using NUnit.Framework;
using Shared;
using Unity.Mathematics;

namespace Tests.EditMode.Utilities
{
    [TestFixture]
    public class CombatUtilityTests
    {
        #region CalculateEffectiveDistance Tests

        [Test]
        public void CalculateEffectiveDistance_NoRadius_ReturnsRawDistance()
        {
            float result = CombatUtility.CalculateEffectiveDistance(10f, 0f);

            Assert.AreEqual(10f, result, 0.001f);
        }

        [Test]
        public void CalculateEffectiveDistance_WithRadius_SubtractsRadius()
        {
            float result = CombatUtility.CalculateEffectiveDistance(10f, 3f);

            Assert.AreEqual(7f, result, 0.001f);
        }

        [Test]
        public void CalculateEffectiveDistance_RadiusExceedsDistance_ReturnsZero()
        {
            float result = CombatUtility.CalculateEffectiveDistance(2f, 5f);

            Assert.AreEqual(0f, result, 0.001f);
        }

        [Test]
        public void CalculateEffectiveDistance_EqualValues_ReturnsZero()
        {
            float result = CombatUtility.CalculateEffectiveDistance(5f, 5f);

            Assert.AreEqual(0f, result, 0.001f);
        }

        [Test]
        public void CalculateEffectiveDistance_LargeWall_SurfaceDistance()
        {
            // Wall 반지름 4, 거리 6 → 표면까지 2
            float result = CombatUtility.CalculateEffectiveDistance(6f, 4f);

            Assert.AreEqual(2f, result, 0.001f);
        }

        #endregion

        #region TickCooldown Tests

        [Test]
        public void TickCooldown_NormalTick_ReducesTime()
        {
            float result = CombatUtility.TickCooldown(1.0f, 0.016f);

            Assert.AreEqual(0.984f, result, 0.001f);
        }

        [Test]
        public void TickCooldown_ExceedRemaining_ReturnsZero()
        {
            float result = CombatUtility.TickCooldown(0.01f, 0.016f);

            Assert.AreEqual(0f, result, 0.001f);
        }

        [Test]
        public void TickCooldown_ZeroRemaining_StaysZero()
        {
            float result = CombatUtility.TickCooldown(0f, 0.016f);

            Assert.AreEqual(0f, result, 0.001f);
        }

        [Test]
        public void TickCooldown_ZeroDeltaTime_NoChange()
        {
            float result = CombatUtility.TickCooldown(1.0f, 0f);

            Assert.AreEqual(1.0f, result, 0.001f);
        }

        [Test]
        public void TickCooldown_ExactMatch_ReturnsZero()
        {
            float result = CombatUtility.TickCooldown(0.5f, 0.5f);

            Assert.AreEqual(0f, result, 0.001f);
        }

        #endregion

        #region RotateTowardTarget Tests

        [Test]
        public void RotateTowardTarget_ForwardDirection_RotatesToForward()
        {
            var myPos = new float3(0, 0, 0);
            var targetPos = new float3(0, 0, 10);
            quaternion rotation = quaternion.identity;

            CombatUtility.RotateTowardTarget(myPos, targetPos, ref rotation);

            // forward 방향 → z축 양의 방향
            float3 forward = math.forward(rotation);
            Assert.AreEqual(0f, forward.x, 0.01f);
            Assert.AreEqual(1f, forward.z, 0.01f);
        }

        [Test]
        public void RotateTowardTarget_RightDirection_RotatesToRight()
        {
            var myPos = new float3(0, 0, 0);
            var targetPos = new float3(10, 0, 0);
            quaternion rotation = quaternion.identity;

            CombatUtility.RotateTowardTarget(myPos, targetPos, ref rotation);

            float3 forward = math.forward(rotation);
            Assert.AreEqual(1f, forward.x, 0.01f);
            Assert.AreEqual(0f, forward.z, 0.01f);
        }

        [Test]
        public void RotateTowardTarget_IgnoresYDifference()
        {
            var myPos = new float3(0, 0, 0);
            var targetPos = new float3(10, 50, 0); // Y 차이 크지만 무시해야 함
            quaternion rotation = quaternion.identity;

            CombatUtility.RotateTowardTarget(myPos, targetPos, ref rotation);

            float3 forward = math.forward(rotation);
            Assert.AreEqual(1f, forward.x, 0.01f);
            Assert.AreEqual(0f, forward.y, 0.01f);
        }

        [Test]
        public void RotateTowardTarget_SamePosition_NoRotationChange()
        {
            var pos = new float3(5, 0, 5);
            quaternion original = quaternion.Euler(0, 0.5f, 0);
            quaternion rotation = original;

            CombatUtility.RotateTowardTarget(pos, pos, ref rotation);

            Assert.AreEqual(original.value.x, rotation.value.x, 0.001f);
            Assert.AreEqual(original.value.y, rotation.value.y, 0.001f);
            Assert.AreEqual(original.value.z, rotation.value.z, 0.001f);
            Assert.AreEqual(original.value.w, rotation.value.w, 0.001f);
        }

        #endregion

        #region ResetCooldown Tests

        [Test]
        public void ResetCooldown_NormalSpeed_ReturnsInverse()
        {
            float result = CombatUtility.ResetCooldown(2f);

            Assert.AreEqual(0.5f, result, 0.001f);
        }

        [Test]
        public void ResetCooldown_SpeedOne_ReturnsOne()
        {
            float result = CombatUtility.ResetCooldown(1f);

            Assert.AreEqual(1f, result, 0.001f);
        }

        [Test]
        public void ResetCooldown_ZeroSpeed_ReturnsFallback()
        {
            float result = CombatUtility.ResetCooldown(0f);

            Assert.AreEqual(1f, result, 0.001f);
        }

        [Test]
        public void ResetCooldown_NegativeSpeed_ReturnsFallback()
        {
            float result = CombatUtility.ResetCooldown(-1f);

            Assert.AreEqual(1f, result, 0.001f);
        }

        [Test]
        public void ResetCooldown_HighSpeed_ReturnsSmallCooldown()
        {
            float result = CombatUtility.ResetCooldown(10f);

            Assert.AreEqual(0.1f, result, 0.001f);
        }

        [Test]
        public void ResetCooldown_FractionalSpeed_ReturnsCorrectInverse()
        {
            float result = CombatUtility.ResetCooldown(0.5f);

            Assert.AreEqual(2f, result, 0.001f);
        }

        #endregion
    }
}
