using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Shared
{
    /// <summary>
    /// 전투 공통 유틸리티 (Burst 호환 static 메서드)
    /// - MeleeAttackSystem, RangedAttackSystem의 공통 로직 추출
    /// </summary>
    [BurstCompile]
    public static class CombatUtility
    {
        // 투사체 높이 오프셋
        const float ProjectileHeightOffset = 1f;
        // 투사체 이동 속도
        const float ProjectileSpeed = 30f;

        /// <summary>
        /// 유효 거리 계산: max(0, 직선거리 - 타겟 반지름)
        /// 큰 오브젝트(Wall 등)는 중심이 아닌 표면까지의 거리로 사거리 판정
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [BurstCompile]
        public static float CalculateEffectiveDistance(float rawDistance, float targetRadius)
        {
            return math.max(0f, rawDistance - targetRadius);
        }

        /// <summary>
        /// 쿨다운 감소: max(0, remaining - dt)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [BurstCompile]
        public static float TickCooldown(float remainingTime, float deltaTime)
        {
            return math.max(0f, remainingTime - deltaTime);
        }

        /// <summary>
        /// 타겟 방향 수평 회전 (y=0 후 LookRotationSafe)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [BurstCompile]
        public static void RotateTowardTarget(in float3 myPos, in float3 targetPos, ref quaternion rotation)
        {
            float3 direction = targetPos - myPos;
            direction.y = 0;

            if (math.lengthsq(direction) > 0.001f)
            {
                rotation = quaternion.LookRotationSafe(math.normalize(direction), math.up());
            }
        }

        /// <summary>
        /// 쿨다운 리셋값 계산: attackSpeed > 0 ? 1/attackSpeed : 1
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [BurstCompile]
        public static float ResetCooldown(float attackSpeed)
        {
            return attackSpeed > 0f ? 1.0f / attackSpeed : 1.0f;
        }

        /// <summary>
        /// ObstacleRadius 포함 유효 거리 계산 (직선거리 - 타겟 반지름)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float GetEffectiveDistance(
            float3 myPos, float3 targetPos,
            Entity targetEntity,
            in ComponentLookup<ObstacleRadius> radiusLookup)
        {
            float rawDist = math.distance(myPos, targetPos);
            float targetRadius = radiusLookup.TryGetComponent(targetEntity, out var obs)
                ? obs.Radius : 0f;
            return CalculateEffectiveDistance(rawDist, targetRadius);
        }

        /// <summary>
        /// 타겟 생존 여부 확인 (Transform 존재 + Health 존재 + HP > 0)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsTargetAlive(
            Entity targetEntity,
            in ComponentLookup<LocalTransform> transformLookup,
            in ComponentLookup<Health> healthLookup,
            out LocalTransform targetTransform)
        {
            targetTransform = default;
            if (!transformLookup.TryGetComponent(targetEntity, out targetTransform))
                return false;
            if (!healthLookup.TryGetComponent(targetEntity, out var health))
                return false;
            return health.CurrentValue > 0;
        }

        /// <summary>
        /// Defense 조회 + 데미지 계산 + DamageEvent 버퍼 추가
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ApplyDamage(
            ref EntityCommandBuffer.ParallelWriter ecb,
            int sortKey,
            Entity targetEntity,
            Entity attackerEntity,
            float attackPower,
            in ComponentLookup<Defense> defenseLookup)
        {
            float defenseValue = defenseLookup.TryGetComponent(targetEntity, out var defense)
                ? defense.Value : 0f;
            float finalDamage = DamageUtility.CalculateDamage(attackPower, defenseValue);
            ecb.AppendToBuffer(sortKey, targetEntity, new DamageEvent { Damage = finalDamage, Attacker = attackerEntity });
        }

        /// <summary>
        /// 시각 전용 투사체 생성 (VisualOnlyTag, 필중)
        /// </summary>
        [BurstCompile]
        public static void SpawnVisualProjectile(
            ref EntityCommandBuffer.ParallelWriter ecb,
            int sortKey,
            in Entity prefab,
            in float3 myPos,
            in float3 targetPos)
        {
            float3 startPos = myPos + new float3(0, ProjectileHeightOffset, 0);
            float3 endPos = targetPos + new float3(0, ProjectileHeightOffset, 0);
            float3 projectileDir = math.normalize(endPos - startPos);
            float projectileDistance = math.distance(startPos, endPos);

            Entity projectile = ecb.Instantiate(sortKey, prefab);

            quaternion rotation = quaternion.LookRotationSafe(projectileDir, math.up());
            ecb.SetComponent(sortKey, projectile, LocalTransform.FromPositionRotationScale(startPos, rotation, 1f));

            ecb.SetComponent(sortKey, projectile, new ProjectileMove
            {
                Direction = projectileDir,
                Speed = ProjectileSpeed,
                RemainingDistance = projectileDistance
            });

            ecb.AddComponent(sortKey, projectile, new VisualOnlyTag());
        }
    }
}
