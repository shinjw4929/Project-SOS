using Unity.Entities;
using Unity.Collections;
using Unity.NetCode;
using Unity.Transforms;
using Unity.Mathematics;
using Shared;

namespace Server
{
    /// <summary>
    /// 클라이언트의 건물 건설 요청 RPC를 처리하는 서버 시스템
    /// - BuildRequestRpc 수신 및 검증
    /// - 그리드 충돌 체크 (기존 건물과 겹치지 않는지)
    /// - 건물 엔티티 생성 및 네트워크 동기화
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial struct HandleBuildRequestSystem : ISystem
    {
        private bool _singletonWarningLogged;

        public void OnCreate(ref SystemState state)
        {
            _singletonWarningLogged = false;
        }

        public void OnUpdate(ref SystemState state)
        {
            // 필수 싱글톤 검증
            if (!ValidateSingletons(ref state))
            {
                return;
            }

            var buildingRefs = SystemAPI.GetSingleton<BuildingEntitiesReferences>();
            var gridSettings = SystemAPI.GetSingleton<GridSettings>();
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            // 모든 건설 요청 RPC 처리
            foreach (var (rpcReceive, rpcEntity) in SystemAPI.Query<RefRO<ReceiveRpcCommandRequest>>()
                .WithAll<BuildRequestRpc>()
                .WithEntityAccess())
            {
                ProcessBuildRequest(ref state, ref ecb, rpcReceive, rpcEntity, buildingRefs, gridSettings);
            }

            ecb.Playback(state.EntityManager);
            ecb.Dispose();
        }

        /// <summary>
        /// 필수 싱글톤이 존재하는지 검증
        /// 없으면 RPC를 소비하고 경고를 출력
        /// </summary>
        private bool ValidateSingletons(ref SystemState state)
        {
            if (SystemAPI.HasSingleton<BuildingEntitiesReferences>() &&
                SystemAPI.HasSingleton<GridSettings>())
            {
                return true;
            }

            if (!_singletonWarningLogged)
            {
                UnityEngine.Debug.LogWarning("[HandleBuildRequestSystem] GridSettings or BuildingEntitiesReferences singleton not found. Make sure EntitiesSubScene is properly set up.");
                _singletonWarningLogged = true;
            }

            // RPC만 소비해서 경고 제거
            var tempEcb = new EntityCommandBuffer(Allocator.Temp);
            foreach (var (_, rpcEntity) in SystemAPI.Query<RefRO<ReceiveRpcCommandRequest>>()
                .WithAll<BuildRequestRpc>()
                .WithEntityAccess())
            {
                tempEcb.DestroyEntity(rpcEntity);
            }
            tempEcb.Playback(state.EntityManager);
            tempEcb.Dispose();

            return false;
        }

        /// <summary>
        /// 개별 건설 요청 처리
        /// </summary>
        private void ProcessBuildRequest(
            ref SystemState state,
            ref EntityCommandBuffer ecb,
            RefRO<ReceiveRpcCommandRequest> rpcReceive,
            Entity rpcEntity,
            BuildingEntitiesReferences buildingRefs,
            GridSettings gridSettings)
        {
            var rpc = state.EntityManager.GetComponentData<BuildRequestRpc>(rpcEntity);

            // 프리팹 선택 및 메타데이터에서 크기 조회
            Entity buildingPrefab = SelectBuildingPrefab(rpc.buildingType, buildingRefs);
            if (buildingPrefab == Entity.Null)
            {
                UnityEngine.Debug.LogWarning($"[HandleBuildRequestSystem] Building prefab not found for type: {rpc.buildingType}");
                ecb.DestroyEntity(rpcEntity);
                return;
            }

            var metadata = state.EntityManager.GetComponentData<BuildingMetadata>(buildingPrefab);
            int width = metadata.width;
            int height = metadata.height;

            // GridCell 버퍼로 점유 상태 확인
            var gridEntity = SystemAPI.GetSingletonEntity<GridSettings>();
            if (!SystemAPI.HasBuffer<GridCell>(gridEntity))
            {
                ecb.DestroyEntity(rpcEntity);
                return;
            }

            var buffer = SystemAPI.GetBuffer<GridCell>(gridEntity);
            if (GridUtility.IsOccupied(buffer, rpc.gridX, rpc.gridY, width, height,
                gridSettings.gridWidth, gridSettings.gridHeight))
            {
                ecb.DestroyEntity(rpcEntity);
                return;
            }

            // 유닛 충돌 검증
            if (CheckUnitCollision(ref state, rpc.gridX, rpc.gridY, width, height, gridSettings))
            {
                ecb.DestroyEntity(rpcEntity);
                return;
            }

            // 건물 엔티티 생성 및 설정
            var networkId = SystemAPI.GetComponent<NetworkId>(rpcReceive.ValueRO.SourceConnection);
            CreateBuildingEntity(ref state, ref ecb, buildingPrefab, rpc, width, height, gridSettings, networkId.Value);

            ecb.DestroyEntity(rpcEntity);
        }

        /// <summary>
        /// 건물 타입에 따라 적절한 프리팹 선택
        /// </summary>
        private Entity SelectBuildingPrefab(BuildingTypeEnum type, BuildingEntitiesReferences refs)
        {
            return type switch
            {
                BuildingTypeEnum.Wall => refs.wallPrefabEntity,
                BuildingTypeEnum.Barracks => refs.barracksPrefabEntity,
                _ => Entity.Null
            };
        }

        /// <summary>
        /// 건물 엔티티 생성 및 컴포넌트 설정
        /// </summary>
        private void CreateBuildingEntity(
            ref SystemState state,
            ref EntityCommandBuffer ecb,
            Entity buildingPrefab,
            BuildRequestRpc rpc,
            int width,
            int height,
            GridSettings gridSettings,
            int ownerNetworkId)
        {
            Entity buildingEntity = ecb.Instantiate(buildingPrefab);

            // 그리드 좌표를 월드 좌표로 변환하여 위치 설정 (Y 오프셋 적용)
            float3 worldPos = GridUtility.GridToWorld(rpc.gridX, rpc.gridY, width, height, gridSettings);
            worldPos.y = GridUtility.GetBuildingYOffset(rpc.buildingType);
            SetBuildingTransform(ref state, ref ecb, buildingEntity, buildingPrefab, worldPos);

            // 건물 정보 설정
            var buildingData = new Building
            {
                buildingType = rpc.buildingType,
                ownerTeamId = ownerNetworkId,
                gridX = rpc.gridX,
                gridY = rpc.gridY
            };

            if (state.EntityManager.HasComponent<Building>(buildingPrefab))
            {
                ecb.SetComponent(buildingEntity, buildingData);
            }
            else
            {
                ecb.AddComponent(buildingEntity, buildingData);
            }
        }

        /// <summary>
        /// 건물의 Transform 설정 (프리팹의 스케일과 회전 유지)
        /// </summary>
        private void SetBuildingTransform(
            ref SystemState state,
            ref EntityCommandBuffer ecb,
            Entity buildingEntity,
            Entity buildingPrefab,
            float3 worldPos)
        {
            if (state.EntityManager.HasComponent<LocalTransform>(buildingPrefab))
            {
                var prefabTransform = state.EntityManager.GetComponentData<LocalTransform>(buildingPrefab);
                var newTransform = prefabTransform;
                newTransform.Position = worldPos;
                ecb.SetComponent(buildingEntity, newTransform);
            }
            else
            {
                ecb.AddComponent(buildingEntity, LocalTransform.FromPosition(worldPos));
            }
        }

        /// <summary>
        /// 유닛과의 충돌 검사 (건물 배치 영역 내에 유닛이 있는지 확인)
        /// </summary>
        private bool CheckUnitCollision(ref SystemState state, int gridX, int gridY, int width, int height, GridSettings gridSettings)
        {
            float3 buildingCenter = GridUtility.GridToWorld(gridX, gridY, width, height, gridSettings);
            float halfWidth = width * gridSettings.cellSize / 2f;
            float halfHeight = height * gridSettings.cellSize / 2f;

            foreach (var (transform, _) in SystemAPI.Query<RefRO<LocalTransform>, RefRO<UnitType>>())
            {
                float3 unitPos = transform.ValueRO.Position;

                // AABB 충돌 검사 (XZ 평면)
                if (unitPos.x >= buildingCenter.x - halfWidth && unitPos.x <= buildingCenter.x + halfWidth &&
                    unitPos.z >= buildingCenter.z - halfHeight && unitPos.z <= buildingCenter.z + halfHeight)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
