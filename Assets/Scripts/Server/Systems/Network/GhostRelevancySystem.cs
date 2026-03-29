using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using Shared;

namespace Server
{
    /// <summary>
    /// Ghost Relevancy: 카메라 뷰포트 AABB 기반으로 적/다른 유저 유닛 Ghost 전송 여부 결정.
    /// Outer(HalfExtent x 1.3) 밖 -> irrelevant, Inner(HalfExtent x 1.15) 안 -> relevant.
    /// 자기 유닛(GhostOwner == Connection)은 항상 relevant 유지.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(UpdateConnectionPositionSystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [BurstCompile]
    public partial struct GhostRelevancySystem : ISystem
    {
        private const float OuterMultiplier = 1.3f;
        private const float InnerMultiplier = 1.15f;
        private static readonly float2 DefaultHalfExtent = new float2(30f, 20f);

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (!SystemAPI.TryGetSingletonRW<GhostRelevancy>(out var relevancy))
                return;

            relevancy.ValueRW.GhostRelevancyMode = GhostRelevancyMode.SetIsIrrelevant;
            var relevancySet = relevancy.ValueRO.GhostRelevancySet;

            var connections = new NativeList<ConnectionInfo>(4, Allocator.Temp);
            foreach (var (conPos, networkId, viewExtent) in
                     SystemAPI.Query<RefRO<GhostConnectionPosition>, RefRO<NetworkId>, RefRO<ConnectionViewExtent>>()
                         .WithAll<NetworkStreamInGame>())
            {
                float2 half = viewExtent.ValueRO.HalfExtent;
                if (half.x <= 0f || half.y <= 0f) half = DefaultHalfExtent;

                connections.Add(new ConnectionInfo
                {
                    NetworkIdValue = networkId.ValueRO.Value,
                    Position = conPos.ValueRO.Position,
                    OuterHalf = half * OuterMultiplier,
                    InnerHalf = half * InnerMultiplier,
                });
            }

            if (connections.Length == 0)
            {
                connections.Dispose();
                return;
            }

            // 적: ownerId = -1 (어떤 Connection과도 매칭 불가 -> 모든 Connection에 대해 필터링)
            foreach (var (ghost, transform) in
                     SystemAPI.Query<RefRO<GhostInstance>, RefRO<LocalTransform>>()
                         .WithAll<EnemyTag>())
            {
                UpdateRelevancy(ref relevancySet, in connections,
                    ghost.ValueRO.ghostId, transform.ValueRO.Position, -1);
            }

            // 유닛: GhostOwner.NetworkId로 자기 유닛 skip
            foreach (var (ghost, transform, owner) in
                     SystemAPI.Query<RefRO<GhostInstance>, RefRO<LocalTransform>, RefRO<GhostOwner>>()
                         .WithAll<UnitTag>())
            {
                UpdateRelevancy(ref relevancySet, in connections,
                    ghost.ValueRO.ghostId, transform.ValueRO.Position, owner.ValueRO.NetworkId);
            }

            connections.Dispose();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void UpdateRelevancy(
            ref NativeParallelHashMap<RelevantGhostForConnection, int> relevancySet,
            in NativeList<ConnectionInfo> connections,
            int ghostId, float3 pos, int ownerId)
        {
            for (int c = 0; c < connections.Length; c++)
            {
                var conn = connections[c];

                // 자기 소유 = 항상 relevant
                if (ownerId == conn.NetworkIdValue)
                    continue;

                var pair = new RelevantGhostForConnection(conn.NetworkIdValue, ghostId);

                float dx = math.abs(pos.x - conn.Position.x);
                float dz = math.abs(pos.z - conn.Position.z);

                bool currentlyIrrelevant = relevancySet.ContainsKey(pair);

                // AABB 밖: X 또는 Z 중 하나라도 Outer 초과 -> irrelevant
                if (!currentlyIrrelevant && (dx > conn.OuterHalf.x || dz > conn.OuterHalf.y))
                    relevancySet.TryAdd(pair, 1);
                // AABB 안: X와 Z 모두 Inner 이내 -> relevant 복원
                else if (currentlyIrrelevant && dx < conn.InnerHalf.x && dz < conn.InnerHalf.y)
                    relevancySet.Remove(pair);
            }
        }

        private struct ConnectionInfo
        {
            public int NetworkIdValue;
            public float3 Position;
            public float2 OuterHalf; // Outer AABB 반크기
            public float2 InnerHalf; // Inner AABB 반크기 (Hysteresis)
        }
    }
}
