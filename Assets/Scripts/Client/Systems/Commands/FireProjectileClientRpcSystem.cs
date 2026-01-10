using Unity.Entities;
using Unity.NetCode;
using UnityEngine;
using UnityEngine.InputSystem;
using Shared; // CombatStatus, Team 사용을 위해

namespace Client
{
    /// <summary>
    /// [비활성화됨] 스페이스바 수동 발사 시스템
    /// - 원거리 공격이 RangedAttackSystem으로 자동화됨
    /// - 필요 시 [DisableAutoCreation] 제거하여 재활성화 가능
    /// </summary>
    [DisableAutoCreation]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(GhostInputSystemGroup))]
    public partial struct FireProjectileClientRpcSystem : ISystem
    {
        private double _lastFireTime;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NetworkId>();
            state.RequireForUpdate<UserState>();
            state.RequireForUpdate<SelectedEntityInfoState>();
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var userState = SystemAPI.GetSingleton<UserState>();
            if (userState.CurrentState != UserContext.Command) return;

            // 1. 입력 체크
            // (임시)마우스 입력 제거
            //bool isMouseFire = Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
            bool isSpaceFire = Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame;
            if (!isSpaceFire) return;

            // 2. 연결 엔티티 확인
            if (!SystemAPI.TryGetSingletonEntity<NetworkId>(out Entity connectionEntity)) return;

            // 3. 선택된 대표 엔티티 가져오기
            var selectedEntityInfoState = SystemAPI.GetSingleton<SelectedEntityInfoState>();
            if (selectedEntityInfoState.PrimaryEntity == Entity.Null) return;
            if (!selectedEntityInfoState.IsOwnedSelection) return;

            Entity shooter = selectedEntityInfoState.PrimaryEntity;

            // 4. 전투 능력 확인
            if (!SystemAPI.HasComponent<CombatStats>(shooter)) return;

            // 5. 쿨타임 계산
            var combatStatus = SystemAPI.GetComponent<CombatStats>(shooter);
            float cooldown = (combatStatus.AttackSpeed > 0) ? (1.0f / combatStatus.AttackSpeed) : 999f;

            double currentTime = SystemAPI.Time.ElapsedTime;
            if (currentTime - _lastFireTime < cooldown) return;

            // 4. 발사 처리
            _lastFireTime = currentTime;

            var ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
                .CreateCommandBuffer(state.WorldUnmanaged);

            var rpcEntity = ecb.CreateEntity();
            ecb.AddComponent(rpcEntity, default(FireProjectileRpc));
            ecb.AddComponent(rpcEntity, new SendRpcCommandRequest { TargetConnection = connectionEntity });
        }
    }
}