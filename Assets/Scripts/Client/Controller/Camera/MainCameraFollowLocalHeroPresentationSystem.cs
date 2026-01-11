using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
[UpdateInGroup(typeof(PresentationSystemGroup))]
public partial class MainCameraFollowLocalHeroPresentationSystem : SystemBase
{
    private Transform _camTransform;
    
    // 타겟 엔티티 캐싱
    private Entity _targetEntity;
    
    // SmoothDamp 상태 변수
    private Vector3 _velocity;
    private Quaternion _lockedRotation;
    private bool _rotationInitialized;

    protected override void OnCreate()
    {
        // NetworkId와 설정 데이터가 모두 존재해야 시스템 가동
        RequireForUpdate<NetworkId>();
        RequireForUpdate<MainCameraSettingsData>();
    }

    protected override void OnUpdate()
    {
        // 1. 카메라 참조 확인 (Managed Object)
        if (_camTransform == null)
        {
            var cam = Camera.main;
            if (cam == null) return;
            _camTransform = cam.transform;
        }

        // 2. 설정 데이터 가져오기 (싱글톤 접근, 매우 빠름)
        var settings = SystemAPI.GetSingleton<MainCameraSettingsData>();

        // 3. 회전 고정 초기화 로직
        if (settings.LockRotation && !_rotationInitialized)
        {
            _lockedRotation = _camTransform.rotation;
            _rotationInitialized = true;
        }

        // 4. 타겟 엔티티 유효성 검사 및 검색
        if (_targetEntity == Entity.Null || !SystemAPI.Exists(_targetEntity))
        {
            if (!TryFindLocalHero(out _targetEntity))
                return; 
        }

        // 5. 타겟 위치 조회 및 이동
        // LocalTransform 컴포넌트에 직접 접근
        var localTransform = SystemAPI.GetComponent<LocalTransform>(_targetEntity);
        float3 heroPos = localTransform.Position;

        // float3 -> Vector3 형변환
        Vector3 desired = (Vector3)heroPos + (Vector3)settings.Offset;

        _camTransform.position = Vector3.SmoothDamp(
            _camTransform.position,
            desired,
            ref _velocity,
            settings.SmoothTime
        );

        if (settings.LockRotation)
            _camTransform.rotation = _lockedRotation;
    }

    private bool TryFindLocalHero(out Entity heroEntity)
    {
        heroEntity = Entity.Null;
        int myNetworkId = SystemAPI.GetSingleton<NetworkId>().Value;

        // 내 캐릭터 찾기
        foreach (var (lt, owner, entity) in SystemAPI
                     .Query<RefRO<LocalTransform>, RefRO<GhostOwner>>()
                     .WithAll<HeroTag>()
                     .WithEntityAccess())
        {
            if (owner.ValueRO.NetworkId == myNetworkId)
            {
                heroEntity = entity;
                return true;
            }
        }
        return false;
    }
}