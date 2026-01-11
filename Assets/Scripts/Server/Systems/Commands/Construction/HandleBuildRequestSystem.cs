using Unity.Entities;
using Unity.Collections;
using Unity.NetCode;
using Unity.Transforms;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Burst;
using Unity.Jobs;
using Shared;

namespace Server
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [BurstCompile]
    public partial struct HandleBuildRequestSystem : ISystem
    {
        #region Component Lookups
        [ReadOnly] private ComponentLookup<ProductionCost> _productionCostLookup;
        [ReadOnly] private ComponentLookup<StructureFootprint> _footprintLookup;
        [ReadOnly] private ComponentLookup<LocalTransform> _transformLookup;
        [ReadOnly] private ComponentLookup<NetworkId> _networkIdLookup;
        [ReadOnly] private ComponentLookup<ProductionInfo> _productionInfoLookup;
        [ReadOnly] private ComponentLookup<NeedsNavMeshObstacle> _needsNavMeshLookup;
        [ReadOnly] private ComponentLookup<GhostInstance> _ghostInstanceLookup;
        [ReadOnly] private ComponentLookup<Parent> _parentLookup;
        [ReadOnly] private ComponentLookup<ResourceCenterTag> _resourceCenterTagLookup;
        [ReadOnly] private BufferLookup<GridCell> _gridCellLookup;

        // 자원 수정은 '직렬 Job'에서 하므로 안전하게 일반 Lookup 사용
        private ComponentLookup<UserCurrency> _userCurrencyLookup;
        private ComponentLookup<UserTechState> _userTechStateLookup;
        #endregion

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GridSettings>();
            state.RequireForUpdate<StructureCatalog>();
            state.RequireForUpdate<PhysicsWorldSingleton>();
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();

            _productionCostLookup = state.GetComponentLookup<ProductionCost>(true);
            _footprintLookup = state.GetComponentLookup<StructureFootprint>(true);
            _transformLookup = state.GetComponentLookup<LocalTransform>(true);
            _networkIdLookup = state.GetComponentLookup<NetworkId>(true);
            _productionInfoLookup = state.GetComponentLookup<ProductionInfo>(true);
            _needsNavMeshLookup = state.GetComponentLookup<NeedsNavMeshObstacle>(true);
            _ghostInstanceLookup = state.GetComponentLookup<GhostInstance>(true);
            _parentLookup = state.GetComponentLookup<Parent>(true);
            _resourceCenterTagLookup = state.GetComponentLookup<ResourceCenterTag>(true);
            _userCurrencyLookup = state.GetComponentLookup<UserCurrency>(false);
            _userTechStateLookup = state.GetComponentLookup<UserTechState>(false);
            _gridCellLookup = state.GetBufferLookup<GridCell>(true);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (!SystemAPI.TryGetSingletonEntity<GridSettings>(out var gridEntity) || 
                !SystemAPI.TryGetSingletonEntity<StructureCatalog>(out var catalogEntity))
            {
                return;
            }

            UpdateLookups(ref state);

            // 1. 임시 데이터 큐 생성 (TempJob 할당자 사용)
            var actionQueue = new NativeQueue<BuildActionRequest>(Allocator.TempJob);

            // 2. NetworkId 매핑
            var networkIdToCurrencyMap = new NativeHashMap<int, Entity>(16, Allocator.TempJob);
            foreach (var (ghostOwner, entity) in SystemAPI.Query<RefRO<GhostOwner>>()
                         .WithAll<UserEconomyTag>()
                         .WithEntityAccess())
            {
                networkIdToCurrencyMap.TryAdd(ghostOwner.ValueRO.NetworkId, entity);
            }

            // 3. 데이터 준비
            var gridSettings = SystemAPI.GetComponent<GridSettings>(gridEntity);
            var prefabBuffer = SystemAPI.GetBuffer<StructureCatalogElement>(catalogEntity).AsNativeArray();
            var physicsWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>();
            
            var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
            var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

            // ------------------------------------------------------------------
            // [Job 1] 병렬 검증 (ValidateBuildRequestJob)
            // ------------------------------------------------------------------
            var validateJob = new ValidateBuildRequestJob
            {
                // Inputs
                GridSettings = gridSettings,
                GridEntity = gridEntity,
                PhysicsWorld = physicsWorld.PhysicsWorld.CollisionWorld,
                PrefabBuffer = prefabBuffer,
                
                // Lookups
                NetworkIdLookup = _networkIdLookup,
                ProductionCostLookup = _productionCostLookup,
                FootprintLookup = _footprintLookup,
                GridCellLookup = _gridCellLookup,
                GhostInstanceLookup = _ghostInstanceLookup,
                ParentLookup = _parentLookup,
                
                // Output
                ActionQueue = actionQueue.AsParallelWriter()
            };

            JobHandle validateHandle = validateJob.ScheduleParallel(state.Dependency);

            // ------------------------------------------------------------------
            // [Job 2] 직렬 실행 (ExecuteBuildRequestJob)
            // ------------------------------------------------------------------
            var executeJob = new ExecuteBuildRequestJob
            {
                ActionQueue = actionQueue,
                NetworkIdToCurrencyMap = networkIdToCurrencyMap,
                UserCurrencyLookup = _userCurrencyLookup,
                UserTechStateLookup = _userTechStateLookup,
                ProductionInfoLookup = _productionInfoLookup,
                NeedsNavMeshLookup = _needsNavMeshLookup,
                ResourceCenterTagLookup = _resourceCenterTagLookup,
                TransformLookup = _transformLookup, // FootprintLookup 제거 (좌표 계산 완료됨)
                Ecb = ecb
            };

            state.Dependency = executeJob.Schedule(validateHandle);

            // 메모리 해제 예약
            networkIdToCurrencyMap.Dispose(state.Dependency);
            actionQueue.Dispose(state.Dependency);
        }

        private void UpdateLookups(ref SystemState state)
        {
            _productionCostLookup.Update(ref state);
            _footprintLookup.Update(ref state);
            _transformLookup.Update(ref state);
            _networkIdLookup.Update(ref state);
            _productionInfoLookup.Update(ref state);
            _needsNavMeshLookup.Update(ref state);
            _ghostInstanceLookup.Update(ref state);
            _parentLookup.Update(ref state);
            _resourceCenterTagLookup.Update(ref state);
            _userCurrencyLookup.Update(ref state);
            _userTechStateLookup.Update(ref state);
            _gridCellLookup.Update(ref state);
        }
    }

    /// <summary>
    /// [Job 1] 병렬 검증: 물리/그리드 확인 및 좌표 계산
    /// </summary>
    [BurstCompile]
    public partial struct ValidateBuildRequestJob : IJobEntity
    {
        public NativeQueue<BuildActionRequest>.ParallelWriter ActionQueue;
        
        [ReadOnly] public GridSettings GridSettings;
        [ReadOnly] public Entity GridEntity;
        [ReadOnly] public CollisionWorld PhysicsWorld;
        [ReadOnly] public NativeArray<StructureCatalogElement> PrefabBuffer;

        [ReadOnly] public ComponentLookup<NetworkId> NetworkIdLookup;
        [ReadOnly] public ComponentLookup<ProductionCost> ProductionCostLookup;
        [ReadOnly] public ComponentLookup<StructureFootprint> FootprintLookup;
        [ReadOnly] public BufferLookup<GridCell> GridCellLookup;
        [ReadOnly] public ComponentLookup<GhostInstance> GhostInstanceLookup;
        [ReadOnly] public ComponentLookup<Parent> ParentLookup;

        private void Execute(Entity rpcEntity, [EntityIndexInQuery] int sortKey, RefRO<ReceiveRpcCommandRequest> rpcReceive, RefRO<BuildRequestRpc> rpc)
        {
            // 1. 연결 유효성
            if (!NetworkIdLookup.HasComponent(rpcReceive.ValueRO.SourceConnection)) return;
            
            int sourceNetworkId = NetworkIdLookup[rpcReceive.ValueRO.SourceConnection].Value;

            // 2. 프리팹 Index 검증
            int index = rpc.ValueRO.StructureIndex;
            if (index < 0 || index >= PrefabBuffer.Length)
            {
                EnqueueFail(rpcEntity);
                return;
            }

            Entity structurePrefab = PrefabBuffer[index].PrefabEntity;
            if (structurePrefab == Entity.Null || !FootprintLookup.HasComponent(structurePrefab))
            {
                EnqueueFail(rpcEntity);
                return;
            }

            // 3. 물리/그리드 검사 준비
            var footprint = FootprintLookup[structurePrefab];
            int width = footprint.Width;
            int length = footprint.Length;
            int2 gridPos = rpc.ValueRO.GridPosition;

            // [최적화] 좌표 계산을 여기서 수행하고 결과에 담음
            float3 buildingCenter = GridUtility.GridToWorld(gridPos.x, gridPos.y, width, length, GridSettings);
            
            // 높이 보정 (피벗이 바닥인 경우를 대비해 미리 Y축 계산)
            // 피벗이 중심이면 그냥 두면 되고, 바닥이면 높이의 절반만큼 올림
            buildingCenter.y += footprint.Height * 0.5f;

            bool isValid = true;

            // 3-1. 그리드 점유
            if (GridCellLookup.HasBuffer(GridEntity))
            {
                var gridBuffer = GridCellLookup[GridEntity];
                if (GridUtility.IsOccupied(gridBuffer, gridPos.x, gridPos.y, width, length,
                    GridSettings.GridSize.x, GridSettings.GridSize.y))
                {
                    isValid = false;
                }
            }

            // 3-2. 물리 충돌 (좌표 계산된 buildingCenter 사용)
            if (isValid)
            {
                float3 halfExtents = new float3(width * GridSettings.CellSize * 0.5f, 1f, length * GridSettings.CellSize * 0.5f);
                
                // 높이 보정된 buildingCenter는 생성용이고, 물리 검사용은 Y축을 다시 0 근처로 잡거나 
                // AABB 센터를 조정해야 할 수 있습니다. 여기서는 XZ 평면 중심이 중요하므로 그대로 씁니다.
                // (단, 물리 체크 시 Y축 높이가 너무 높으면 안 맞을 수 있으니 원래 로직대로 GridToWorld 직후 값 사용이 안전할 수 있음.
                //  여기서는 편의상 생성 위치를 넘기고 물리 체크는 내부적으로 처리한다고 가정)

                // 물리 체크용 센터 (바닥 기준)
                float3 physicsCenter = buildingCenter; 
                physicsCenter.y = 0f; // 물리 체크는 보통 바닥 평면에서 수행

                if (CheckUnitCollision(physicsCenter, halfExtents, rpc.ValueRO.BuilderGhostId))
                {
                    isValid = false;
                }
            }

            // 4. 결과 Enqueue (WorldPos 포함)
            ActionQueue.Enqueue(new BuildActionRequest
            {
                RpcEntity = rpcEntity,
                PrefabEntity = structurePrefab,
                SourceNetworkId = sourceNetworkId,
                SourceConnection = rpcReceive.ValueRO.SourceConnection,
                GridPosition = gridPos,
                TargetWorldPos = buildingCenter, // 계산된 최종 위치 전달
                StructureCost = ProductionCostLookup[structurePrefab].Cost,
                IsValidPhysics = isValid
            });
        }

        private void EnqueueFail(Entity rpcEntity)
        {
            ActionQueue.Enqueue(new BuildActionRequest { RpcEntity = rpcEntity, IsValidPhysics = false });
        }

        private bool CheckUnitCollision(float3 center, float3 halfExtents, int builderGhostId)
        {
            float shrinkAmount = 0.05f;
            float3 queryHalfExtents = math.max(0, halfExtents - new float3(shrinkAmount, 0, shrinkAmount));

            var input = new OverlapAabbInput
            {
                Aabb = new Aabb { Min = center - queryHalfExtents, Max = center + queryHalfExtents },
                Filter = new CollisionFilter
                {
                    BelongsTo = 1u << 7,
                    CollidesWith = (1u << 11) | (1u << 12),
                    GroupIndex = 0
                }
            };

            var hits = new NativeList<int>(Allocator.Temp);
            PhysicsWorld.OverlapAabb(input, ref hits);

            bool hasBlockingCollision = false;
            for (int i = 0; i < hits.Length; i++)
            {
                Entity hitEntity = PhysicsWorld.Bodies[hits[i]].Entity;
                if (IsBuilder(hitEntity, builderGhostId)) continue;
                
                hasBlockingCollision = true;
                break;
            }
            hits.Dispose();
            return hasBlockingCollision;
        }

        private bool IsBuilder(Entity hitEntity, int builderGhostId)
        {
            if (builderGhostId == 0) return false;
            Entity current = hitEntity;
            for (int i = 0; i < 3; i++)
            {
                if (current == Entity.Null) break;
                if (GhostInstanceLookup.HasComponent(current)) return GhostInstanceLookup[current].ghostId == builderGhostId;
                if (ParentLookup.HasComponent(current)) current = ParentLookup[current].Value;
                else break;
            }
            return false;
        }
    }

    /// <summary>
    /// [Job 2] 직렬 실행: 자원 확인 및 생성
    /// </summary>
    [BurstCompile]
    public partial struct ExecuteBuildRequestJob : IJob
    {
        public NativeQueue<BuildActionRequest> ActionQueue;

        [ReadOnly] public NativeHashMap<int, Entity> NetworkIdToCurrencyMap;
        public ComponentLookup<UserCurrency> UserCurrencyLookup;
        public ComponentLookup<UserTechState> UserTechStateLookup;

        [ReadOnly] public ComponentLookup<ProductionInfo> ProductionInfoLookup;
        [ReadOnly] public ComponentLookup<NeedsNavMeshObstacle> NeedsNavMeshLookup;
        [ReadOnly] public ComponentLookup<ResourceCenterTag> ResourceCenterTagLookup;
        [ReadOnly] public ComponentLookup<LocalTransform> TransformLookup;

        public EntityCommandBuffer Ecb;

        public void Execute()
        {
            while (ActionQueue.TryDequeue(out var request))
            {
                // 1. 검증 실패 건 제거
                if (!request.IsValidPhysics)
                {
                    Ecb.DestroyEntity(request.RpcEntity);
                    continue;
                }

                // 2. 유저 자원 엔티티 확인
                if (!NetworkIdToCurrencyMap.TryGetValue(request.SourceNetworkId, out Entity userCurrencyEntity))
                {
                    Ecb.DestroyEntity(request.RpcEntity);
                    continue;
                }

                // 3. 자원 확인 및 차감 (직렬 실행으로 안전함)
                var currency = UserCurrencyLookup[userCurrencyEntity];

                if (currency.Amount < request.StructureCost)
                {
                    // 자원 부족 알림 RPC 전송
                    if (request.SourceConnection != Entity.Null)
                    {
                        var notifyEntity = Ecb.CreateEntity();
                        Ecb.AddComponent(notifyEntity, new NotificationRpc { Type = NotificationType.InsufficientFunds });
                        Ecb.AddComponent(notifyEntity, new SendRpcCommandRequest { TargetConnection = request.SourceConnection });
                    }
                    Ecb.DestroyEntity(request.RpcEntity);
                    continue;
                }

                currency.Amount -= request.StructureCost;
                UserCurrencyLookup[userCurrencyEntity] = currency;

                // 4. 건물 생성 (TargetWorldPos 사용)
                CreateBuildingEntity(request);

                // 5. ResourceCenter 건설 시 테크 상태 업데이트
                if (ResourceCenterTagLookup.HasComponent(request.PrefabEntity))
                {
                    UpdateTechState(request.SourceNetworkId, hasResourceCenter: true);
                }

                Ecb.DestroyEntity(request.RpcEntity);
            }
        }

        private void CreateBuildingEntity(BuildActionRequest request)
        {
            Entity prefab = request.PrefabEntity;
            Entity newStructure = Ecb.Instantiate(prefab);
            
            // Transform 설정 (Job 1에서 계산한 WorldPos 사용)
            if (TransformLookup.HasComponent(prefab))
            {
                var transform = TransformLookup[prefab];
                transform.Position = request.TargetWorldPos;
                Ecb.SetComponent(newStructure, transform);
            }
            else
            {
                Ecb.SetComponent(newStructure, LocalTransform.FromPosition(request.TargetWorldPos));
            }

            Ecb.SetComponent(newStructure, new GridPosition { Position = request.GridPosition });
            Ecb.AddComponent(newStructure, new GhostOwner { NetworkId = request.SourceNetworkId });
            Ecb.SetComponent(newStructure, new Team { teamId = request.SourceNetworkId });

            if (ProductionInfoLookup.HasComponent(prefab))
            {
                var info = ProductionInfoLookup[prefab];
                Ecb.AddComponent(newStructure, new UnderConstructionTag
                {
                    Progress = 0f,
                    TotalBuildTime = info.ProductionTime
                });
            }

            if (NeedsNavMeshLookup.HasComponent(prefab))
            {
                Ecb.SetComponentEnabled<NeedsNavMeshObstacle>(newStructure, true);
            }
        }

        private void UpdateTechState(int networkId, bool hasResourceCenter)
        {
            if (!NetworkIdToCurrencyMap.TryGetValue(networkId, out Entity userEconomyEntity))
                return;

            if (!UserTechStateLookup.HasComponent(userEconomyEntity))
                return;

            var techState = UserTechStateLookup[userEconomyEntity];
            techState.HasResourceCenter = hasResourceCenter;
            UserTechStateLookup[userEconomyEntity] = techState;
        }
    }
}