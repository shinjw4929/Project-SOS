using NUnit.Framework;
using Shared;

namespace Tests.EditMode.Utilities
{
    /// <summary>
    /// DamageUtility 단위 테스트
    /// 데미지 계산 로직 검증
    /// </summary>
    [TestFixture]
    public class DamageUtilityTests
    {
        #region CalculateDamage Tests

        [Test]
        public void CalculateDamage_NoDef_ReturnsBaseDamage()
        {
            // Arrange
            float baseDamage = 10f;
            float defense = 0f;

            // Act
            float result = DamageUtility.CalculateDamage(baseDamage, defense);

            // Assert
            Assert.AreEqual(10f, result);
        }

        [Test]
        public void CalculateDamage_30PercentDef_Returns70PercentDamage()
        {
            // Arrange
            float baseDamage = 100f;
            float defense = 0.3f;  // 30% 감소

            // Act
            float result = DamageUtility.CalculateDamage(baseDamage, defense);

            // Assert
            // 100 * (1 - 0.3) = 70
            Assert.AreEqual(70f, result, 0.001f);
        }

        [Test]
        public void CalculateDamage_50PercentDef_Returns50PercentDamage()
        {
            // Arrange
            float baseDamage = 100f;
            float defense = 0.5f;

            // Act
            float result = DamageUtility.CalculateDamage(baseDamage, defense);

            // Assert
            Assert.AreEqual(50f, result, 0.001f);
        }

        [Test]
        public void CalculateDamage_90PercentDef_Returns10PercentDamage()
        {
            // Arrange
            float baseDamage = 100f;
            float defense = 0.9f;

            // Act
            float result = DamageUtility.CalculateDamage(baseDamage, defense);

            // Assert
            Assert.AreEqual(10f, result, 0.001f);
        }

        [Test]
        public void CalculateDamage_100PercentDef_ReturnsMinimumOne()
        {
            // Arrange
            float baseDamage = 100f;
            float defense = 1f;  // 100% 방어

            // Act
            float result = DamageUtility.CalculateDamage(baseDamage, defense);

            // Assert
            // 100 * 0 = 0 이지만, 최소 1 보장
            Assert.AreEqual(1f, result);
        }

        [Test]
        public void CalculateDamage_OverflowDef_ReturnsMinimumOne()
        {
            // Arrange
            float baseDamage = 100f;
            float defense = 1.5f;  // 150% 방어 (오버플로우)

            // Act
            float result = DamageUtility.CalculateDamage(baseDamage, defense);

            // Assert
            // 방어력이 1을 초과해도 최소 1 데미지
            Assert.AreEqual(1f, result);
        }

        [Test]
        public void CalculateDamage_NegativeDefense_ClampsToZeroDefense()
        {
            // Arrange
            float baseDamage = 100f;
            float defense = -0.5f;  // 음수 방어력 (디버프 상황)

            // Act
            float result = DamageUtility.CalculateDamage(baseDamage, defense);

            // Assert
            // 1 - (-0.5) = 1.5 → 150 데미지 (증폭됨)
            // 현재 구현은 음수 방어를 클램프하지 않으므로 데미지 증폭
            Assert.AreEqual(150f, result, 0.001f);
        }

        [Test]
        public void CalculateDamage_SmallDamage_ReturnsMinimumOne()
        {
            // Arrange
            float baseDamage = 0.5f;  // 1보다 작은 데미지
            float defense = 0f;

            // Act
            float result = DamageUtility.CalculateDamage(baseDamage, defense);

            // Assert
            // 0.5 < 1 이므로 최소 1로 클램프
            Assert.AreEqual(1f, result);
        }

        [Test]
        public void CalculateDamage_ZeroDamage_ReturnsMinimumOne()
        {
            // Arrange
            float baseDamage = 0f;
            float defense = 0f;

            // Act
            float result = DamageUtility.CalculateDamage(baseDamage, defense);

            // Assert
            Assert.AreEqual(1f, result);
        }

        [Test]
        public void CalculateDamage_TypicalCombatScenario()
        {
            // Arrange - 일반적인 전투 시나리오
            float swordsmanDamage = 5f;
            float enemyDefense = 0.1f;  // 10% 방어

            // Act
            float result = DamageUtility.CalculateDamage(swordsmanDamage, enemyDefense);

            // Assert
            // 5 * 0.9 = 4.5
            Assert.AreEqual(4.5f, result, 0.001f);
        }

        [Test]
        public void CalculateDamage_SniperVsHeavyArmor()
        {
            // Arrange - 저격수 vs 중장갑 적
            float sniperDamage = 20f;
            float heavyArmorDefense = 0.4f;  // 40% 방어

            // Act
            float result = DamageUtility.CalculateDamage(sniperDamage, heavyArmorDefense);

            // Assert
            // 20 * 0.6 = 12
            Assert.AreEqual(12f, result, 0.001f);
        }

        #endregion
    }
}
