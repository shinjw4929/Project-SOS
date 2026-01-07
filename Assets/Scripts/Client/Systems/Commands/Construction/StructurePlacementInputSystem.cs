using Unity.Entities;
using Unity.NetCode;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.InputSystem;
using Shared;
using Unity.Collections;

namespace Client
{
    [UpdateInGroup(typeof(GhostInputSystemGroup))]
    [UpdateAfter(typeof(UnitCommandInputSystem))]  // UnitCommandInputSystem 후에 실행 (None 덮어쓰기 방지)
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial class StructurePlacementInputSystem : SystemBase
    {
        private Camera _mainCamera;
        private int _groundMask;
        private BufferLookup<UnitCommand> _unitCommandLookup;

        protected override void OnCreate()
        {
            RequireForUpdate<NetworkStreamInGame>();
            RequireForUpdate<NetworkTime>();
            RequireForUpdate<UserState>();
            RequireForUpdate<GridSettings>();
            RequireForUpdate<SelectedEntityInfoState>();
            _groundMask = 1 << 3; // 3: Ground
            _unitCommandLookup = GetBufferLookup<UnitCommand>(false);
        }

        protected override void OnUpdate()
        {
            var userState = SystemAPI.GetSingleton<UserState>();
            if (userState.CurrentState != UserContext.Construction) return;

            _unitCommandLookup.Update(this);

            // 1. 카메라 캐싱 (매 프레임 FindObject 방지)
            if (_mainCamera == null)
            {
                _mainCamera = Camera.main;
                if (_mainCamera == null) return;
            }

            var mouse = Mouse.current;
            if (mouse == null) return;

            var gridSettings = SystemAPI.GetSingleton<GridSettings>();
            float2 mousePos = mouse.position.ReadValue();
            Ray ray = _mainCamera.ScreenPointToRay(new Vector3(mousePos.x, mousePos.y, 0));

            if (UnityEngine.Physics.Raycast(ray, out RaycastHit hit, 1000f, _groundMask))
            {
                int2 gridPos = GridUtility.WorldToGrid(hit.point, gridSettings);

                // RefRW로 접근하여 값 수정
                ref var previewState = ref SystemAPI.GetSingletonRW<StructurePreviewState>().ValueRW;

                // 값이 다를 때만 쓰기 (Cache Miss 방지 미세 최적화)
                if (!previewState.GridPosition.Equals(gridPos))
                    previewState.GridPosition = gridPos;

                // 좌클릭 처리: PlacementStatus에 따라 분기
                if (mouse.leftButton.wasPressedThisFrame)
                {
                    HandleLeftClick(ref previewState, gridPos, gridSettings);
                }
            }
        }

        private void HandleLeftClick(ref StructurePreviewState previewState, int2 gridPos, GridSettings gridSettings)
        {
            // Invalid 상태면 무시
            if (previewState.Status == PlacementStatus.Invalid)
                return;

            var ecb = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>()
                .CreateCommandBuffer(World.Unmanaged);

            // Builder entity의 GhostId 조회
            var selectionState = SystemAPI.GetSingleton<SelectedEntityInfoState>();
            Entity builderEntity = selectionState.PrimaryEntity;
            int builderGhostId = 0;

            if (builderEntity != Entity.Null && EntityManager.HasComponent<GhostInstance>(builderEntity))
            {
                builderGhostId = EntityManager.GetComponentData<GhostInstance>(builderEntity).ghostId;
            }

            switch (previewState.Status)
            {
                case PlacementStatus.ValidInRange:
                    // 즉시 건설: BuildRequestRpc 전송 (BuilderGhostId 포함)
                    SendBuildRequest(ecb, previewState.SelectedPrefabIndex, gridPos, builderGhostId);
                    ExitConstructionMode();
                    break;

                case PlacementStatus.ValidOutOfRange:
                    // 이동 후 건설: PendingBuildRequest 부착 + 이동 명령
                    IssueMoveAndBuildCommand(ecb, previewState, gridPos, gridSettings);
                    ExitConstructionMode();
                    break;
            }
        }

        private void SendBuildRequest(EntityCommandBuffer ecb, int structureIndex, int2 gridPos, int builderGhostId)
        {
            var rpcEntity = ecb.CreateEntity();
            ecb.AddComponent(rpcEntity, new BuildRequestRpc
            {
                StructureIndex = structureIndex,
                GridPosition = gridPos,
                BuilderGhostId = builderGhostId
            });
            ecb.AddComponent<SendRpcCommandRequest>(rpcEntity);
        }

        private void IssueMoveAndBuildCommand(EntityCommandBuffer ecb, StructurePreviewState previewState, int2 gridPos, GridSettings gridSettings)
        {
            var selectionState = SystemAPI.GetSingleton<SelectedEntityInfoState>();
            Entity builderEntity = selectionState.PrimaryEntity;

            if (builderEntity == Entity.Null)
                return;

            // Builder 태그 확인
            if (!EntityManager.HasComponent<BuilderTag>(builderEntity))
                return;

            // 건물 Footprint 조회
            if (!EntityManager.HasComponent<StructureFootprint>(previewState.SelectedPrefab))
                return;

            var footprint = EntityManager.GetComponentData<StructureFootprint>(previewState.SelectedPrefab);

            // 건물 중심 월드 좌표 계산
            float3 buildCenter = GridUtility.GridToWorld(
                gridPos.x,
                gridPos.y,
                footprint.Width,
                footprint.Length,
                gridSettings
            );

            // workRange 조회
            float workRange = 2f; // 기본값
            if (EntityManager.HasComponent<WorkRange>(builderEntity))
            {
                workRange = EntityManager.GetComponentData<WorkRange>(builderEntity).Value;
            }

            // 건물 반지름 조회 (ObstacleRadius)
            float structureRadius = 1.5f; // 기본값 (Wall:1.42, Barracks:2.13 등 고려)
            if (EntityManager.HasComponent<ObstacleRadius>(previewState.SelectedPrefab))
            {
                structureRadius = EntityManager.GetComponentData<ObstacleRadius>(previewState.SelectedPrefab).Radius;
            }

            // 유닛 반지름 조회 (ObstacleRadius)
            float unitRadius = 0.5f; // 기본값
            if (EntityManager.HasComponent<ObstacleRadius>(builderEntity))
            {
                unitRadius = EntityManager.GetComponentData<ObstacleRadius>(builderEntity).Radius;
            }

            // 유닛 현재 위치 조회
            float3 unitPos = float3.zero;
            if (EntityManager.HasComponent<Unity.Transforms.LocalTransform>(builderEntity))
            {
                unitPos = EntityManager.GetComponentData<Unity.Transforms.LocalTransform>(builderEntity).Position;
            }

            // 이동 목표 계산: 건물 중심 방향으로, 건물 표면 + 유닛 반지름 + 여유분 지점
            float3 moveTarget = CalculateMoveTarget(unitPos, buildCenter, structureRadius, unitRadius);

            // PendingBuildRequest 부착 (기존 있으면 덮어쓰기)
            var pendingRequest = new PendingBuildRequest
            {
                StructureIndex = previewState.SelectedPrefabIndex,
                GridPosition = gridPos,
                BuildSiteCenter = buildCenter,
                RequiredRange = workRange,
                Width = footprint.Width,
                Length = footprint.Length,
                StructureRadius = structureRadius
            };

            if (EntityManager.HasComponent<PendingBuildRequest>(builderEntity))
            {
                ecb.SetComponent(builderEntity, pendingRequest);
            }
            else
            {
                ecb.AddComponent(builderEntity, pendingRequest);
            }

            // UnitCommand 버퍼에 BuildKey 명령 추가
            // CommandProcessingSystem에서 MovementGoal 설정 및 경로 계산 트리거
            if (_unitCommandLookup.HasBuffer(builderEntity))
            {
                var networkTime = SystemAPI.GetSingleton<NetworkTime>();
                var inputBuffer = _unitCommandLookup[builderEntity];

                inputBuffer.AddCommandData(new UnitCommand
                {
                    Tick = networkTime.ServerTick,
                    CommandType = UnitCommandType.BuildKey,
                    GoalPosition = moveTarget,
                    TargetGhostId = 0
                });
            }
        }

        /// <summary>
        /// 이동 목표 계산: 건물 중심 방향으로, 건물 표면 + 유닛 반지름 + 여유분 지점
        /// 단순화: 중심점 거리 - 건물 반지름 = 표면까지 거리
        /// </summary>
        private float3 CalculateMoveTarget(float3 unitPos, float3 buildCenter, float structureRadius, float unitRadius)
        {
            // 유닛에서 건물 중심 방향 (XZ 평면)
            float2 unitXZ = new float2(unitPos.x, unitPos.z);
            float2 centerXZ = new float2(buildCenter.x, buildCenter.z);
            float2 toCenter = centerXZ - unitXZ;
            float distToCenter = math.length(toCenter);

            // 방향 정규화 (유닛이 건물 중심에 있으면 기본 방향 사용)
            float2 dir;
            if (distToCenter < 0.01f)
            {
                dir = new float2(1, 0); // 기본 방향
            }
            else
            {
                dir = toCenter / distToCenter;
            }

            // 이동 목표: 건물 중심에서 (건물 반지름 + 유닛 반지름 + 여유분) 만큼 떨어진 지점
            float stopDistance = structureRadius + unitRadius + 0.1f;
            float2 targetXZ = centerXZ - dir * stopDistance;

            return new float3(targetXZ.x, 0, targetXZ.y);
        }

        private void ExitConstructionMode()
        {
            var userStateRw = SystemAPI.GetSingletonRW<UserState>();
            userStateRw.ValueRW.CurrentState = UserContext.Command;
        }
    }
}
