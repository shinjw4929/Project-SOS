using Unity.Entities;
using Unity.NetCode;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.InputSystem;
using Shared;

namespace Client
{
    [UpdateInGroup(typeof(GhostInputSystemGroup))]
    [UpdateAfter(typeof(SelectionInputSystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial class StructurePlacementInputSystem : SystemBase
    {
        private Camera _mainCamera;
        private int _groundMask;

        protected override void OnCreate()
        {
            RequireForUpdate<NetworkStreamInGame>();
            RequireForUpdate<UserState>();
            RequireForUpdate<GridSettings>();
            RequireForUpdate<CurrentSelectionState>();
            _groundMask = 1 << 3; // 3: Ground
        }

        protected override void OnUpdate()
        {
            var userState = SystemAPI.GetSingleton<UserState>();
            if (userState.CurrentState != UserContext.Construction) return;

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

            switch (previewState.Status)
            {
                case PlacementStatus.ValidInRange:
                    // 즉시 건설: BuildRequestRpc 전송
                    SendBuildRequest(ecb, previewState.SelectedPrefabIndex, gridPos);
                    ExitConstructionMode();
                    break;

                case PlacementStatus.ValidOutOfRange:
                    // 이동 후 건설: PendingBuildRequest 부착 + 이동 명령
                    IssueMoveAndBuildCommand(ecb, previewState, gridPos, gridSettings);
                    ExitConstructionMode();
                    break;
            }
        }

        private void SendBuildRequest(EntityCommandBuffer ecb, int structureIndex, int2 gridPos)
        {
            var rpcEntity = ecb.CreateEntity();
            ecb.AddComponent(rpcEntity, new BuildRequestRpc
            {
                StructureIndex = structureIndex,
                GridPosition = gridPos
            });
            ecb.AddComponent<SendRpcCommandRequest>(rpcEntity);
        }

        private void IssueMoveAndBuildCommand(EntityCommandBuffer ecb, StructurePreviewState previewState, int2 gridPos, GridSettings gridSettings)
        {
            var selectionState = SystemAPI.GetSingleton<CurrentSelectionState>();
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

            // BuildRange 조회
            float buildRange = 5f; // 기본값
            if (EntityManager.HasComponent<BuildRange>(builderEntity))
            {
                buildRange = EntityManager.GetComponentData<BuildRange>(builderEntity).Value;
            }

            // 유닛 현재 위치 조회
            float3 unitPos = float3.zero;
            if (EntityManager.HasComponent<Unity.Transforms.LocalTransform>(builderEntity))
            {
                unitPos = EntityManager.GetComponentData<Unity.Transforms.LocalTransform>(builderEntity).Position;
            }

            // 이동 목표 계산: 건물 AABB 가장자리에서 사거리 내 지점
            float3 moveTarget = CalculateMoveTarget(unitPos, buildCenter, footprint, gridSettings, buildRange);

            // PendingBuildRequest 부착 (기존 있으면 덮어쓰기)
            var pendingRequest = new PendingBuildRequest
            {
                StructureIndex = previewState.SelectedPrefabIndex,
                GridPosition = gridPos,
                BuildSiteCenter = buildCenter,
                RequiredRange = buildRange,
                Width = footprint.Width,
                Length = footprint.Length
            };

            if (EntityManager.HasComponent<PendingBuildRequest>(builderEntity))
            {
                ecb.SetComponent(builderEntity, pendingRequest);
            }
            else
            {
                ecb.AddComponent(builderEntity, pendingRequest);
            }

            // UnitState 변경: MovingToBuild
            if (EntityManager.HasComponent<UnitState>(builderEntity))
            {
                ecb.SetComponent(builderEntity, new UnitState
                {
                    CurrentState = UnitContext.MovingToBuild
                });
            }

            // 이동 명령 발행: RTSInputState 업데이트
            if (EntityManager.HasComponent<RTSInputState>(builderEntity))
            {
                ecb.SetComponent(builderEntity, new RTSInputState
                {
                    TargetPosition = moveTarget,
                    HasTarget = true
                });
            }

            // MoveTarget 설정 (이동 시스템에서 사용)
            if (EntityManager.HasComponent<MoveTarget>(builderEntity))
            {
                ecb.SetComponent(builderEntity, new MoveTarget
                {
                    position = moveTarget,
                    isValid = true
                });
            }
        }

        /// <summary>
        /// 이동 목표 계산: 건물 AABB 가장자리에서 약간 떨어진 지점 (사거리 내)
        /// </summary>
        private float3 CalculateMoveTarget(float3 unitPos, float3 buildCenter, StructureFootprint footprint, GridSettings gridSettings, float buildRange)
        {
            // 건물 AABB 계산
            float halfWidth = footprint.Width * gridSettings.CellSize * 0.5f;
            float halfLength = footprint.Length * gridSettings.CellSize * 0.5f;

            float3 aabbMin = new float3(buildCenter.x - halfWidth, 0, buildCenter.z - halfLength);
            float3 aabbMax = new float3(buildCenter.x + halfWidth, 0, buildCenter.z + halfLength);

            // AABB 최근접점 계산 (XZ 평면)
            float closestX = math.clamp(unitPos.x, aabbMin.x, aabbMax.x);
            float closestZ = math.clamp(unitPos.z, aabbMin.z, aabbMax.z);
            float3 closestPoint = new float3(closestX, 0, closestZ);

            // 유닛에서 최근접점까지의 방향
            float3 dirToUnit = unitPos - closestPoint;
            float distToEdge = math.length(new float2(dirToUnit.x, dirToUnit.z));

            // 방향 정규화 (유닛이 건물 위에 있으면 기본 방향 사용)
            float3 normalizedDir;
            if (distToEdge < 0.01f)
            {
                // 유닛이 건물 위에 있으면 건물 중심에서 멀어지는 방향
                float3 awayFromCenter = unitPos - buildCenter;
                float awayDist = math.length(new float2(awayFromCenter.x, awayFromCenter.z));
                if (awayDist < 0.01f)
                {
                    normalizedDir = new float3(1, 0, 0); // 기본 방향
                }
                else
                {
                    normalizedDir = math.normalize(new float3(awayFromCenter.x, 0, awayFromCenter.z));
                }
            }
            else
            {
                normalizedDir = math.normalize(new float3(dirToUnit.x, 0, dirToUnit.z));
            }

            // 이동 목표: AABB 가장자리에서 (1f) 만큼 떨어진 지점
            // 사거리보다 약간 안쪽으로 이동하여 확실히 사거리 내에 도착
            float targetDistance =  1f;
            float3 moveTarget = closestPoint + normalizedDir * targetDistance;

            return moveTarget;
        }

        private void ExitConstructionMode()
        {
            var userStateRw = SystemAPI.GetSingletonRW<UserState>();
            userStateRw.ValueRW.CurrentState = UserContext.Command;
        }
    }
}
