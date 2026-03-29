using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Burst;
using Shared;

namespace Client
{
    /// <summary>
    /// MinimapBatchRpc 수신 -> MinimapDataState 싱글톤 갱신.
    /// 새 FrameId 감지 시 Pending 버퍼를 Resize, 배치 데이터를 복사.
    /// 전체 수신 완료 시 Data <-> PendingData 스왑 + 적/유닛 수 카운트.
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
                Data = new NativeList<float3>(256, Allocator.Persistent),
                PendingData = new NativeList<float3>(256, Allocator.Persistent),
                PendingFrameId = 0,
                ReceivedCount = 0,
                ExpectedTotalCount = 0,
                EnemyCount = 0,
                UnitCount = 0,
            });
#if UNITY_EDITOR
            state.EntityManager.SetName(entity, "Singleton_MinimapDataState");
#endif
        }

        public void OnDestroy(ref SystemState state)
        {
            if (SystemAPI.TryGetSingleton<MinimapDataState>(out var data))
            {
                if (data.Data.IsCreated) data.Data.Dispose();
                if (data.PendingData.IsCreated) data.PendingData.Dispose();
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

                if (batch.TotalCount == 0)
                {
                    s.Data.Clear();
                    s.PendingData.Clear();
                    s.PendingFrameId = batch.FrameId;
                    s.ReceivedCount = 0;
                    s.ExpectedTotalCount = 0;
                    s.EnemyCount = 0;
                    s.UnitCount = 0;
                    ecb.DestroyEntity(rpcEntity);
                    continue;
                }

                // 새 프레임 시작 감지
                if (batch.FrameId != s.PendingFrameId)
                {
                    s.PendingFrameId = batch.FrameId;
                    s.ReceivedCount = 0;
                    s.ExpectedTotalCount = batch.TotalCount;
                    s.PendingData.Resize(batch.TotalCount, NativeArrayOptions.ClearMemory);
                }

                // 배치 데이터 복사
                int start = batch.StartIndex;
                int count = batch.ValidCount;
                for (int i = 0; i < count; i++)
                {
                    int idx = start + i;
                    if (idx < s.PendingData.Length)
                    {
                        s.PendingData[idx] = batch.GetData(i);
                    }
                }

                s.ReceivedCount += count;

                if (s.ReceivedCount >= s.ExpectedTotalCount)
                {
                    (s.Data, s.PendingData) = (s.PendingData, s.Data);
                    s.ReceivedCount = 0;

                    // 적/유닛 수 카운트
                    int enemies = 0;
                    for (int i = 0; i < s.Data.Length; i++)
                    {
                        if ((int)s.Data[i].z == -1) enemies++;
                    }
                    s.EnemyCount = (ushort)enemies;
                    s.UnitCount = (ushort)(s.Data.Length - enemies);
                }

                ecb.DestroyEntity(rpcEntity);
            }
        }
    }
}
