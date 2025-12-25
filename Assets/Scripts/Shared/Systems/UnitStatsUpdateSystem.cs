using Unity.Burst;
using Unity.Entities;
using Unity.Collections;
using Unity.NetCode;
using Shared;

[UpdateInGroup(typeof(PredictedSimulationSystemGroup))]
[UpdateBefore(typeof(NetcodePlayerMovementSystem))]
[BurstCompile]
public partial struct UnitStatsUpdateSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GlobalUpgradeMultipliers>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var multipliers = SystemAPI.GetSingleton<GlobalUpgradeMultipliers>();
        var ecb = new EntityCommandBuffer(Allocator.Temp);

        // 1. 베이스 스탯 저장 (최초 1회만)
        foreach (var (stats, entity) in SystemAPI.Query<RefRO<UnitStats>>()
            .WithNone<BaseStats>()
            .WithEntityAccess())
        {
            ecb.AddComponent(entity, new BaseStats
            {
                baseMoveSpeed = stats.ValueRO.moveSpeed,
                baseAttackPower = stats.ValueRO.attackPower
            });
        }

        // 2. 배율 적용
        foreach (var (statsRW, baseStats, unitType) in SystemAPI.Query<
            RefRW<UnitStats>,
            RefRO<BaseStats>,
            RefRO<UnitType>>())
        {
            float moveMultiplier = 1.0f;
            float attackMultiplier = 1.0f;

            switch (unitType.ValueRO.type)
            {
                case UnitTypeEnum.Infantry:
                    moveMultiplier = multipliers.infantryMoveSpeedMultiplier;
                    attackMultiplier = multipliers.infantryAttackPowerMultiplier;
                    break;
                case UnitTypeEnum.Tank:
                    moveMultiplier = multipliers.tankMoveSpeedMultiplier;
                    attackMultiplier = multipliers.tankAttackPowerMultiplier;
                    break;
                case UnitTypeEnum.Scout:
                    moveMultiplier = multipliers.scoutMoveSpeedMultiplier;
                    attackMultiplier = multipliers.scoutAttackPowerMultiplier;
                    break;
                case UnitTypeEnum.Artillery:
                    moveMultiplier = multipliers.artilleryMoveSpeedMultiplier;
                    attackMultiplier = multipliers.artilleryAttackPowerMultiplier;
                    break;
            }

            statsRW.ValueRW.moveSpeed = baseStats.ValueRO.baseMoveSpeed * moveMultiplier;
            statsRW.ValueRW.attackPower = baseStats.ValueRO.baseAttackPower * attackMultiplier;
        }

        ecb.Playback(state.EntityManager);
        ecb.Dispose();
    }
}
