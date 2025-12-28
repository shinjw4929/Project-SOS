using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Collections;
using Shared;

namespace Server
{
    /// <summary>
    /// 유닛이 건물 내부에 갇혔을 때 외부로 밀어내는 시스템
    /// - 건물과 유닛이 겹치면 가장 가까운 외곽으로 밀어냄
    /// - 서버에서만 실행 (결과는 Ghost 동기화로 클라이언트에 전파)
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(HandleBuildRequestSystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial struct StructurePushOutSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GridSettings>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var gridSettings = SystemAPI.GetSingleton<GridSettings>();
            float cellSize = gridSettings.CellSize;
            float pushMargin = 0.1f; // 밀어낸 후 여유 거리

            // 모든 건물의 AABB 정보 수집
            var structures = new NativeList<StructureAABB>(Allocator.Temp);

            foreach (var (gridPos, footprint, transform) in
                SystemAPI.Query<RefRO<GridPosition>, RefRO<StructureFootprint>, RefRO<LocalTransform>>()
                    .WithAll<StructureTag>())
            {
                float3 center = transform.ValueRO.Position;
                float halfWidth = footprint.ValueRO.Width * cellSize * 0.5f;
                float halfLength = footprint.ValueRO.Length * cellSize * 0.5f;

                structures.Add(new StructureAABB
                {
                    Center = new float2(center.x, center.z),
                    HalfExtents = new float2(halfWidth, halfLength)
                });
            }

            // 모든 유닛에 대해 건물 겹침 체크 및 밀어내기
            foreach (var (transform, _) in
                SystemAPI.Query<RefRW<LocalTransform>, RefRO<UnitTag>>())
            {
                float3 unitPos = transform.ValueRO.Position;
                float2 unitPos2D = new float2(unitPos.x, unitPos.z);

                for (int i = 0; i < structures.Length; i++)
                {
                    var structure = structures[i];

                    // AABB 내부에 있는지 체크
                    if (IsInsideAABB(unitPos2D, structure.Center, structure.HalfExtents))
                    {
                        // 가장 가까운 모서리로 밀어내기
                        float2 pushedPos = PushOutOfAABB(unitPos2D, structure.Center, structure.HalfExtents, pushMargin);
                        transform.ValueRW.Position = new float3(pushedPos.x, unitPos.y, pushedPos.y);
                        break; // 하나의 건물에서 밀려나면 다음 유닛으로
                    }
                }
            }

            structures.Dispose();
        }

        private bool IsInsideAABB(float2 point, float2 center, float2 halfExtents)
        {
            float2 diff = math.abs(point - center);
            return diff.x < halfExtents.x && diff.y < halfExtents.y;
        }

        private float2 PushOutOfAABB(float2 point, float2 center, float2 halfExtents, float margin)
        {
            float2 diff = point - center;

            // 각 축에서 가장 가까운 모서리까지의 거리 계산
            float distToRight = halfExtents.x - diff.x;
            float distToLeft = halfExtents.x + diff.x;
            float distToTop = halfExtents.y - diff.y;
            float distToBottom = halfExtents.y + diff.y;

            // 가장 가까운 모서리 방향으로 밀어내기
            float minDist = math.min(math.min(distToRight, distToLeft), math.min(distToTop, distToBottom));

            if (minDist == distToRight)
            {
                return new float2(center.x + halfExtents.x + margin, point.y);
            }
            else if (minDist == distToLeft)
            {
                return new float2(center.x - halfExtents.x - margin, point.y);
            }
            else if (minDist == distToTop)
            {
                return new float2(point.x, center.y + halfExtents.y + margin);
            }
            else // distToBottom
            {
                return new float2(point.x, center.y - halfExtents.y - margin);
            }
        }

        private struct StructureAABB
        {
            public float2 Center;
            public float2 HalfExtents;
        }
    }
}
