using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Burst;
using Shared;

namespace Client
{
    /// <summary>
    /// MinimapBatchRpc 수신 → MinimapDataState 싱글톤 갱신.
    /// 새 FrameId 감지 시 Pending 버퍼를 Resize, 배치 데이터를 복사.
    /// 전체 수신 완료 시 EnemyPositions ↔ PendingPositions 스왑.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [BurstCompile]
    public partial struct MinimapDataReceiveSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NetworkStreamInGame>();

            var entity = state.EntityManager.CreateEntity();
            state.EntityManager.AddComponentData(entity, new MinimapDataState
            {
                EnemyPositions = new NativeList<float2>(256, Allocator.Persistent),
                PendingPositions = new NativeList<float2>(256, Allocator.Persistent),
                PendingFrameId = 0,
                ReceivedCount = 0,
                ExpectedTotalCount = 0,
            });
#if UNITY_EDITOR
            state.EntityManager.SetName(entity, "Singleton_MinimapDataState");
#endif
        }

        public void OnDestroy(ref SystemState state)
        {
            if (SystemAPI.TryGetSingleton<MinimapDataState>(out var data))
            {
                if (data.EnemyPositions.IsCreated) data.EnemyPositions.Dispose();
                if (data.PendingPositions.IsCreated) data.PendingPositions.Dispose();
            }
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (!SystemAPI.TryGetSingletonRW<MinimapDataState>(out var minimapState))
                return;

            var ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
                .CreateCommandBuffer(state.WorldUnmanaged);

            foreach (var (rpc, rpcEntity) in
                     SystemAPI.Query<RefRO<MinimapBatchRpc>>()
                         .WithAll<ReceiveRpcCommandRequest>()
                         .WithEntityAccess())
            {
                ref var s = ref minimapState.ValueRW;
                var batch = rpc.ValueRO;

                // TotalCount == 0: 적 없음 → 클리어
                if (batch.TotalCount == 0)
                {
                    s.EnemyPositions.Clear();
                    s.PendingPositions.Clear();
                    s.PendingFrameId = batch.FrameId;
                    s.ReceivedCount = 0;
                    s.ExpectedTotalCount = 0;
                    ecb.DestroyEntity(rpcEntity);
                    continue;
                }

                // 새 프레임 시작 감지
                if (batch.FrameId != s.PendingFrameId)
                {
                    s.PendingFrameId = batch.FrameId;
                    s.ReceivedCount = 0;
                    s.ExpectedTotalCount = batch.TotalCount;
                    s.PendingPositions.Resize(batch.TotalCount, NativeArrayOptions.ClearMemory);
                }

                // 배치 데이터 복사
                int start = batch.StartIndex;
                int count = batch.ValidCount;
                for (int i = 0; i < count; i++)
                {
                    int idx = start + i;
                    if (idx < s.PendingPositions.Length)
                    {
                        s.PendingPositions[idx] = batch.GetPosition(i);
                    }
                }

                s.ReceivedCount += count;

                if (s.ReceivedCount >= s.ExpectedTotalCount)
                {
                    (s.EnemyPositions, s.PendingPositions) = (s.PendingPositions, s.EnemyPositions);
                    s.ReceivedCount = 0;
                }

                ecb.DestroyEntity(rpcEntity);
            }
        }
    }
}
