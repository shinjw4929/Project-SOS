using Unity.Burst;

namespace Shared
{
    /// <summary>
    /// 데미지 계산 유틸리티
    /// </summary>
    [BurstCompile]
    public static class DamageUtility
    {
        /// <summary>
        /// 최종 데미지 계산 (Defense 적용)
        /// </summary>
        /// <param name="baseDamage">기본 공격력</param>
        /// <param name="defense">방어력 (0.0 ~ 1.0, 예: 0.3 = 30% 감소)</param>
        /// <returns>최종 데미지 (최소 1)</returns>
        [BurstCompile]
        public static float CalculateDamage(float baseDamage, float defense)
        {
            float damageMultiplier = 1f - defense;
            if (damageMultiplier < 0f) damageMultiplier = 0f;

            float finalDamage = baseDamage * damageMultiplier;
            if (finalDamage < 1) finalDamage = 1;

            return finalDamage;
        }
    }
}
