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
    /// 서버에서 전체 적 위치를 수집하여 MinimapBatchRpc로 분산 브로드캐스트.
    /// 전체 전송 완료 후 적 위치를 재수집, 매 틱 2배치(64적)씩 전송하여 분산.
    /// 대역폭: 2400적 기준 ~20KB/s per connection.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [BurstCompile]
    public partial struct MinimapDataBroadcastSystem : ISystem
    {
        private const int BatchSize = 32;
        private const int BatchesPerTick = 2;

        private NativeList<float2> _enemyPositions;
        private int _currentIndex;
        private uint _frameId;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<EnemyTag>();
            _enemyPositions = new NativeList<float2>(256, Allocator.Persistent);
            _currentIndex = 0;
            _frameId = 0;
        }

        public void OnDestroy(ref SystemState state)
        {
            if (_enemyPositions.IsCreated) _enemyPositions.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            // 전체 전송 완료 → 새 프레임 수집
            if (_currentIndex >= _enemyPositions.Length)
            {
                CollectEnemyPositions(ref state);
                _currentIndex = 0;
                _frameId++;
            }

            var ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
                .CreateCommandBuffer(state.WorldUnmanaged);

            int totalCount = _enemyPositions.Length;

            // 적 0마리: TotalCount=0 RPC 1회 전송 → 클라이언트 미니맵 클리어
            if (totalCount == 0)
            {
                var rpcEntity = ecb.CreateEntity();
                ecb.AddComponent(rpcEntity, new MinimapBatchRpc
                {
                    FrameId = _frameId,
                    StartIndex = 0,
                    TotalCount = 0,
                    ValidCount = 0,
                });
                ecb.AddComponent<SendRpcCommandRequest>(rpcEntity);
                return;
            }

            // 이번 틱에 최대 BatchesPerTick개 배치 전송
            for (int b = 0; b < BatchesPerTick && _currentIndex < totalCount; b++)
            {
                int remaining = totalCount - _currentIndex;
                int validCount = math.min(remaining, BatchSize);

                var rpc = new MinimapBatchRpc
                {
                    FrameId = _frameId,
                    StartIndex = (ushort)_currentIndex,
                    TotalCount = (ushort)totalCount,
                    ValidCount = (byte)validCount,
                };

                for (int i = 0; i < validCount; i++)
                    rpc.SetPosition(i, _enemyPositions[_currentIndex + i]);

                var rpcEntity = ecb.CreateEntity();
                ecb.AddComponent(rpcEntity, rpc);
                ecb.AddComponent<SendRpcCommandRequest>(rpcEntity);

                _currentIndex += validCount;
            }
        }

        private void CollectEnemyPositions(ref SystemState state)
        {
            _enemyPositions.Clear();

            foreach (var transform in
                     SystemAPI.Query<RefRO<LocalTransform>>()
                         .WithAll<EnemyTag>())
            {
                _enemyPositions.Add(new float2(
                    transform.ValueRO.Position.x,
                    transform.ValueRO.Position.z));
            }
        }
    }
}
