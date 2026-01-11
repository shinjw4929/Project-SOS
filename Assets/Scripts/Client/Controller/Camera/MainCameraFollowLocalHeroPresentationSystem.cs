using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;
using Shared;

// 클라이언트에서만 메인 카메라를 갱신한다.
[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
[UpdateInGroup(typeof(PresentationSystemGroup))]
public partial class MainCameraFollowLocalHeroPresentationSystem : SystemBase
{
    private Transform camTransform;
    private MainCameraFollowSettings settings;

    private Vector3 velocity;
    private Quaternion lockedRotation;
    private bool rotationInitialized;

    protected override void OnUpdate()
    {
        // 메인 카메라 Transform 캐싱
        if (camTransform == null)
        {
            var cam = Camera.main;
            if (cam == null)
                return;

            camTransform = cam.transform;
        }

        // 설정 컴포넌트 캐싱
        if (settings == null)
        {
            settings = Object.FindAnyObjectByType<MainCameraFollowSettings>();
            if (settings == null)
                return;
        }

        // 회전 고정 옵션 사용 시 최초 1회 현재 회전을 저장한다.
        if (settings.lockRotation && !rotationInitialized)
        {
            lockedRotation = camTransform.rotation;
            rotationInitialized = true;
        }

        // 내 클라이언트 NetworkId 가져오기(연결 엔티티 1개라고 가정)
        int myNetworkId = -1;
        foreach (var networkId in SystemAPI.Query<RefRO<NetworkId>>().WithAll<NetworkStreamConnection>())
        {
            myNetworkId = networkId.ValueRO.Value;
            break;
        }

        if (myNetworkId < 0)
            return;

        // 내 소유(Owner)인 Hero 엔티티를 찾는다.
        bool found = false;
        float3 heroPos = default;

        foreach (var (lt, owner) in SystemAPI
                     .Query<RefRO<LocalTransform>, RefRO<GhostOwner>>()
                     .WithAll<HeroTag>())
        {
            if (owner.ValueRO.NetworkId != myNetworkId)
                continue;

            heroPos = lt.ValueRO.Position;
            found = true;
            break;
        }

        if (!found)
            return;

        // 목표 카메라 위치 = 히어로 위치 + 오프셋
        Vector3 desired = (Vector3)heroPos + settings.offset;

        // 부드럽게 따라가기
        camTransform.position = Vector3.SmoothDamp(
            camTransform.position,
            desired,
            ref velocity,
            settings.smoothTime
        );

        // 회전 고정
        if (settings.lockRotation)
            camTransform.rotation = lockedRotation;
    }
}
