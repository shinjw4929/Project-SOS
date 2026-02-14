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
    /// Ghost Relevancy: 카메라 뷰포트 AABB 기반으로 적 Ghost 전송 여부 결정.
    /// Outer(HalfExtent × 1.3) 밖 → irrelevant, Inner(HalfExtent × 1.15) 안 → relevant.
    /// HalfExtent는 CameraPositionRpc로 클라이언트에서 수신한 실제 뷰포트 지면 투영 반크기.
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

            foreach (var (ghost, transform) in
                     SystemAPI.Query<RefRO<GhostInstance>, RefRO<LocalTransform>>()
                         .WithAll<EnemyTag>())
            {
                int ghostId = ghost.ValueRO.ghostId;
                float3 enemyPos = transform.ValueRO.Position;

                for (int c = 0; c < connections.Length; c++)
                {
                    var conn = connections[c];
                    var pair = new RelevantGhostForConnection(conn.NetworkIdValue, ghostId);

                    float dx = math.abs(enemyPos.x - conn.Position.x);
                    float dz = math.abs(enemyPos.z - conn.Position.z);

                    bool currentlyIrrelevant = relevancySet.ContainsKey(pair);

                    // AABB 밖: X 또는 Z 중 하나라도 Outer 초과 → irrelevant
                    if (!currentlyIrrelevant && (dx > conn.OuterHalf.x || dz > conn.OuterHalf.y))
                        relevancySet.TryAdd(pair, 1);
                    // AABB 안: X와 Z 모두 Inner 이내 → relevant 복원
                    else if (currentlyIrrelevant && dx < conn.InnerHalf.x && dz < conn.InnerHalf.y)
                        relevancySet.Remove(pair);
                }
            }

            connections.Dispose();
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
