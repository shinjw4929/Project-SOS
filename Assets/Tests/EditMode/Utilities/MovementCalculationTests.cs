using NUnit.Framework;
using Shared;
using Unity.Mathematics;

namespace Tests.EditMode.Utilities
{
    /// <summary>
    /// MovementMath 유틸리티 단위 테스트
    /// 이동 시스템의 핵심 계산 로직 검증
    /// </summary>
    [TestFixture]
    public class MovementCalculationTests
    {
        #region CalculateSlowingDistance Tests

        [Test]
        public void CalculateSlowingDistance_NormalValues_ReturnsCorrectDistance()
        {
            // Arrange
            float maxSpeed = 10f;
            float deceleration = 20f;

            // Act
            float result = MovementMath.CalculateSlowingDistance(maxSpeed, deceleration);

            // Assert
            // v^2 / (2a) = 100 / 40 = 2.5
            Assert.AreEqual(2.5f, result, 0.001f);
        }

        [Test]
        public void CalculateSlowingDistance_HighSpeed_ReturnsLargerDistance()
        {
            // Arrange
            float maxSpeed = 20f;
            float deceleration = 20f;

            // Act
            float result = MovementMath.CalculateSlowingDistance(maxSpeed, deceleration);

            // Assert
            // v^2 / (2a) = 400 / 40 = 10
            Assert.AreEqual(10f, result, 0.001f);
        }

        [Test]
        public void CalculateSlowingDistance_ZeroDeceleration_ReturnsZero()
        {
            // Arrange
            float maxSpeed = 10f;
            float deceleration = 0f;

            // Act
            float result = MovementMath.CalculateSlowingDistance(maxSpeed, deceleration);

            // Assert
            Assert.AreEqual(0f, result);
        }

        [Test]
        public void CalculateSlowingDistance_NegativeDeceleration_ReturnsZero()
        {
            // Arrange
            float maxSpeed = 10f;
            float deceleration = -5f;

            // Act
            float result = MovementMath.CalculateSlowingDistance(maxSpeed, deceleration);

            // Assert
            Assert.AreEqual(0f, result);
        }

        #endregion

        #region CalculateTargetSpeed Tests

        [Test]
        public void CalculateTargetSpeed_FarFromDestination_ReturnsMaxSpeed()
        {
            // Arrange
            float distance = 100f;
            float maxSpeed = 10f;
            float slowingDistance = 5f;

            // Act
            float result = MovementMath.CalculateTargetSpeed(distance, maxSpeed, slowingDistance, hasNextWaypoint: false);

            // Assert
            Assert.AreEqual(maxSpeed, result);
        }

        [Test]
        public void CalculateTargetSpeed_InSlowingDistance_ReturnsReducedSpeed()
        {
            // Arrange
            float distance = 2.5f;  // 절반 거리
            float maxSpeed = 10f;
            float slowingDistance = 5f;

            // Act
            float result = MovementMath.CalculateTargetSpeed(distance, maxSpeed, slowingDistance, hasNextWaypoint: false);

            // Assert
            // 거리/감속거리 * 최대속도 = 2.5/5 * 10 = 5
            Assert.AreEqual(5f, result, 0.001f);
        }

        [Test]
        public void CalculateTargetSpeed_VeryClose_ReturnsMinSpeed()
        {
            // Arrange
            float distance = 0.1f;  // 아주 가까움
            float maxSpeed = 10f;
            float slowingDistance = 5f;

            // Act
            float result = MovementMath.CalculateTargetSpeed(distance, maxSpeed, slowingDistance, hasNextWaypoint: false);

            // Assert
            // 계산: 0.1/5 * 10 = 0.2, 하지만 MinSpeed(0.5f)로 클램프
            Assert.AreEqual(MovementMath.MinSpeed, result);
        }

        [Test]
        public void CalculateTargetSpeed_HasNextWaypoint_ReturnsMaxSpeed()
        {
            // Arrange
            float distance = 1f;  // 가까워도
            float maxSpeed = 10f;
            float slowingDistance = 5f;

            // Act
            float result = MovementMath.CalculateTargetSpeed(distance, maxSpeed, slowingDistance, hasNextWaypoint: true);

            // Assert
            // 다음 웨이포인트가 있으면 감속 안함
            Assert.AreEqual(maxSpeed, result);
        }

        [Test]
        public void CalculateTargetSpeed_ExactlyAtSlowingDistance_ReturnsMaxSpeed()
        {
            // Arrange
            float distance = 5f;
            float maxSpeed = 10f;
            float slowingDistance = 5f;

            // Act
            float result = MovementMath.CalculateTargetSpeed(distance, maxSpeed, slowingDistance, hasNextWaypoint: false);

            // Assert
            Assert.AreEqual(maxSpeed, result);
        }

        #endregion

        #region CalculateNewSpeed Tests

        [Test]
        public void CalculateNewSpeed_Acceleration_IncreasesSpeed()
        {
            // Arrange
            float currentSpeed = 5f;
            float targetSpeed = 10f;
            float acceleration = 20f;
            float deceleration = 30f;
            float deltaTime = 0.1f;

            // Act
            float result = MovementMath.CalculateNewSpeed(currentSpeed, targetSpeed, acceleration, deceleration, deltaTime);

            // Assert
            // 가속: 5 + (20 * 0.1) = 7
            Assert.AreEqual(7f, result, 0.001f);
        }

        [Test]
        public void CalculateNewSpeed_Deceleration_DecreasesSpeed()
        {
            // Arrange
            float currentSpeed = 10f;
            float targetSpeed = 5f;
            float acceleration = 20f;
            float deceleration = 30f;
            float deltaTime = 0.1f;

            // Act
            float result = MovementMath.CalculateNewSpeed(currentSpeed, targetSpeed, acceleration, deceleration, deltaTime);

            // Assert
            // 감속: 10 - (30 * 0.1) = 7
            Assert.AreEqual(7f, result, 0.001f);
        }

        [Test]
        public void CalculateNewSpeed_NoOvershoot_ClampsToTarget()
        {
            // Arrange
            float currentSpeed = 9f;
            float targetSpeed = 10f;
            float acceleration = 20f;  // 0.1초에 2만큼 증가 가능
            float deceleration = 30f;
            float deltaTime = 0.1f;

            // Act
            float result = MovementMath.CalculateNewSpeed(currentSpeed, targetSpeed, acceleration, deceleration, deltaTime);

            // Assert
            // 목표가 10이고 가속으로 11이 될 수 있지만, 차이(1)만큼만 증가
            Assert.AreEqual(10f, result, 0.001f);
        }

        [Test]
        public void CalculateNewSpeed_NeverNegative_ClampsToZero()
        {
            // Arrange
            float currentSpeed = 1f;
            float targetSpeed = 0f;
            float acceleration = 20f;
            float deceleration = 30f;  // 0.1초에 3만큼 감소 가능
            float deltaTime = 0.1f;

            // Act
            float result = MovementMath.CalculateNewSpeed(currentSpeed, targetSpeed, acceleration, deceleration, deltaTime);

            // Assert
            // 1 - 3 = -2가 되어야 하지만, 0으로 클램프
            Assert.AreEqual(0f, result);
        }

        [Test]
        public void CalculateNewSpeed_SameSpeed_NoChange()
        {
            // Arrange
            float currentSpeed = 10f;
            float targetSpeed = 10f;
            float acceleration = 20f;
            float deceleration = 30f;
            float deltaTime = 0.1f;

            // Act
            float result = MovementMath.CalculateNewSpeed(currentSpeed, targetSpeed, acceleration, deceleration, deltaTime);

            // Assert
            Assert.AreEqual(10f, result);
        }

        #endregion

        #region IsAtArrivalThreshold Tests

        [Test]
        public void IsAtArrivalThreshold_WithinRange_ReturnsTrue()
        {
            // Arrange
            float distance = 0.2f;
            float arrivalThreshold = 0.3f;

            // Act
            bool result = MovementMath.IsAtArrivalThreshold(distance, arrivalThreshold, hasNextWaypoint: false);

            // Assert
            Assert.IsTrue(result);
        }

        [Test]
        public void IsAtArrivalThreshold_OutsideRange_ReturnsFalse()
        {
            // Arrange
            float distance = 0.5f;
            float arrivalThreshold = 0.3f;

            // Act
            bool result = MovementMath.IsAtArrivalThreshold(distance, arrivalThreshold, hasNextWaypoint: false);

            // Assert
            Assert.IsFalse(result);
        }

        [Test]
        public void IsAtArrivalThreshold_HasNextWaypoint_ReturnsFalse()
        {
            // Arrange
            float distance = 0.1f;  // 아주 가까워도
            float arrivalThreshold = 0.3f;

            // Act
            bool result = MovementMath.IsAtArrivalThreshold(distance, arrivalThreshold, hasNextWaypoint: true);

            // Assert
            // 다음 웨이포인트가 있으면 도착 아님
            Assert.IsFalse(result);
        }

        [Test]
        public void IsAtArrivalThreshold_ExactlyAtThreshold_ReturnsFalse()
        {
            // Arrange
            float distance = 0.3f;
            float arrivalThreshold = 0.3f;

            // Act
            bool result = MovementMath.IsAtArrivalThreshold(distance, arrivalThreshold, hasNextWaypoint: false);

            // Assert
            // distance < threshold 이므로 같으면 false
            Assert.IsFalse(result);
        }

        #endregion

        #region ShouldSnapToStop Tests

        [Test]
        public void ShouldSnapToStop_VeryCloseAndSlow_ReturnsTrue()
        {
            // Arrange
            float distance = 0.01f;
            float currentSpeed = 0.1f;

            // Act
            bool result = MovementMath.ShouldSnapToStop(distance, currentSpeed, hasNextWaypoint: false);

            // Assert
            Assert.IsTrue(result);
        }

        [Test]
        public void ShouldSnapToStop_CloseButFast_ReturnsFalse()
        {
            // Arrange
            float distance = 0.01f;
            float currentSpeed = 1f;  // 빠름

            // Act
            bool result = MovementMath.ShouldSnapToStop(distance, currentSpeed, hasNextWaypoint: false);

            // Assert
            Assert.IsFalse(result);
        }

        [Test]
        public void ShouldSnapToStop_HasNextWaypoint_ReturnsFalse()
        {
            // Arrange
            float distance = 0.01f;
            float currentSpeed = 0.1f;

            // Act
            bool result = MovementMath.ShouldSnapToStop(distance, currentSpeed, hasNextWaypoint: true);

            // Assert
            Assert.IsFalse(result);
        }

        #endregion

        #region ShouldSwitchWaypoint Tests

        [Test]
        public void ShouldSwitchWaypoint_CloseWithNext_ReturnsTrue()
        {
            // Arrange
            float distance = 0.3f;  // CornerRadius(0.5f) 보다 작음

            // Act
            bool result = MovementMath.ShouldSwitchWaypoint(distance, hasNextWaypoint: true);

            // Assert
            Assert.IsTrue(result);
        }

        [Test]
        public void ShouldSwitchWaypoint_FarWithNext_ReturnsFalse()
        {
            // Arrange
            float distance = 1f;  // CornerRadius(0.5f) 보다 큼

            // Act
            bool result = MovementMath.ShouldSwitchWaypoint(distance, hasNextWaypoint: true);

            // Assert
            Assert.IsFalse(result);
        }

        [Test]
        public void ShouldSwitchWaypoint_CloseNoNext_ReturnsFalse()
        {
            // Arrange
            float distance = 0.3f;

            // Act
            bool result = MovementMath.ShouldSwitchWaypoint(distance, hasNextWaypoint: false);

            // Assert
            Assert.IsFalse(result);
        }

        #endregion

        #region Integration Scenarios

        [Test]
        public void FullMovementScenario_AccelerateAndDecelerate()
        {
            // Arrange
            float maxSpeed = 10f;
            float acceleration = 20f;
            float deceleration = 30f;
            float deltaTime = 0.1f;

            float slowingDistance = MovementMath.CalculateSlowingDistance(maxSpeed, deceleration);

            // Act & Assert - 가속 구간
            float distance = 20f;  // 멀리 있음
            float targetSpeed = MovementMath.CalculateTargetSpeed(distance, maxSpeed, slowingDistance, false);
            Assert.AreEqual(maxSpeed, targetSpeed, "먼 거리에서는 최대 속도");

            float currentSpeed = 0f;
            currentSpeed = MovementMath.CalculateNewSpeed(currentSpeed, targetSpeed, acceleration, deceleration, deltaTime);
            Assert.AreEqual(2f, currentSpeed, 0.001f, "첫 프레임 가속");

            currentSpeed = MovementMath.CalculateNewSpeed(currentSpeed, targetSpeed, acceleration, deceleration, deltaTime);
            Assert.AreEqual(4f, currentSpeed, 0.001f, "두번째 프레임 가속");

            // Act & Assert - 감속 구간
            currentSpeed = 10f;  // 최대 속도 도달
            distance = slowingDistance / 2;  // 감속 구간 중간
            targetSpeed = MovementMath.CalculateTargetSpeed(distance, maxSpeed, slowingDistance, false);
            Assert.Less(targetSpeed, maxSpeed, "감속 구간에서는 목표 속도 감소");

            currentSpeed = MovementMath.CalculateNewSpeed(currentSpeed, targetSpeed, acceleration, deceleration, deltaTime);
            Assert.Less(currentSpeed, 10f, "감속 적용됨");
        }

        #endregion
    }
}
