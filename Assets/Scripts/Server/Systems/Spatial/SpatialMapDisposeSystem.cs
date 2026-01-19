using Unity.Entities;
using Shared;

namespace Server
{
    /// <summary>
    /// 공간 분할 맵 해제 시스템
    /// <para>- LateSimulationSystemGroup에서 실행 (모든 시스템 사용 후)</para>
    /// <para>- CompleteDependency 후 맵 Dispose</para>
    /// </summary>
    [UpdateInGroup(typeof(LateSimulationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial struct SpatialMapDisposeSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<SpatialMaps>();
        }

        public void OnDestroy(ref SystemState state)
        {
            // 월드 종료 시 맵 정리
            CleanupMaps(ref state);
        }

        public void OnUpdate(ref SystemState state)
        {
            // 모든 Job이 완료될 때까지 대기
            state.CompleteDependency();

            // 싱글톤에서 맵 참조 가져오기
            if (!SystemAPI.TryGetSingleton<SpatialMaps>(out var maps) || !maps.IsValid)
                return;

            // 맵 해제
            if (maps.TargetingMap.IsCreated)
                maps.TargetingMap.Dispose();

            if (maps.MovementMap.IsCreated)
                maps.MovementMap.Dispose();

            // 싱글톤 무효화
            SystemAPI.SetSingleton(new SpatialMaps { IsValid = false });
        }

        private void CleanupMaps(ref SystemState state)
        {
            if (!SystemAPI.TryGetSingleton<SpatialMaps>(out var maps))
                return;

            if (maps.TargetingMap.IsCreated)
                maps.TargetingMap.Dispose();

            if (maps.MovementMap.IsCreated)
                maps.MovementMap.Dispose();
        }
    }
}
