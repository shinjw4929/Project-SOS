using Client;
using Shared;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.InputSystem;

[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
[UpdateInGroup(typeof(PresentationSystemGroup))]
public partial class CameraSystem : SystemBase
{
    private Transform _camTransform;

    // SmoothDamp 상태 변수
    private Vector3 _velocity;
    private Quaternion _lockedRotation;
    private bool _rotationInitialized;

    // 외부 모드 변경 감지용
    private CameraMode _prevMode;

    // 카메라 위치 서버 전송용
    private int _sendTimer;

    protected override void OnCreate()
    {
        RequireForUpdate<NetworkId>();
        RequireForUpdate<CameraSettings>();
        RequireForUpdate<CameraState>();
    }

    protected override void OnUpdate()
    {
        // 1. 카메라 참조 확인
        if (_camTransform == null)
        {
            var cam = Camera.main;
            if (cam == null) return;
            _camTransform = cam.transform;
        }

        // 포커스 있을 때 마우스 가두기 (포커스 복귀 시 재설정 필요)
        if (Application.isFocused && Cursor.lockState != CursorLockMode.Confined)
        {
            Cursor.lockState = CursorLockMode.Confined;
        }

        // 2. 싱글톤 데이터 가져오기
        var settings = SystemAPI.GetSingleton<CameraSettings>();
        ref var cameraState = ref SystemAPI.GetSingletonRW<CameraState>().ValueRW;

        // 3. 회전 고정 초기화
        if (settings.LockRotation && !_rotationInitialized)
        {
            _lockedRotation = _camTransform.rotation;
            _rotationInitialized = true;
        }

        // 4. 외부 모드 변경 감지 (미니맵 클릭 등)
        if (cameraState.CurrentMode != _prevMode)
        {
            if (cameraState.CurrentMode == CameraMode.EdgePan)
                _velocity = Vector3.zero;
            _prevMode = cameraState.CurrentMode;
        }

        // 5. T 키 토글 입력 처리
        if (Keyboard.current != null && Keyboard.current.tKey.wasPressedThisFrame)
        {
            if (cameraState.CurrentMode == CameraMode.EdgePan)
            {
                // EdgePan → HeroFollow
                cameraState.CurrentMode = CameraMode.HeroFollow;
                _prevMode = CameraMode.HeroFollow;
                if (TryFindLocalHero(out Entity hero))
                {
                    cameraState.TargetEntity = hero;
                }
            }
            else
            {
                // HeroFollow → EdgePan
                cameraState.CurrentMode = CameraMode.EdgePan;
                _prevMode = CameraMode.EdgePan;
                cameraState.TargetEntity = Entity.Null;
                _velocity = Vector3.zero; // 속도 초기화
            }
        }

        // 6. 모드에 따른 카메라 처리
        if (cameraState.CurrentMode == CameraMode.HeroFollow)
        {
            HandleHeroFollow(ref settings, ref cameraState);
        }
        else
        {
            bool blockEdgePan = SystemAPI.TryGetSingleton<UserSelectionInputState>(out var selectionState)
                && (selectionState.Phase == SelectionPhase.Pressing
                    || selectionState.Phase == SelectionPhase.Dragging);

            if (!blockEdgePan)
            {
                HandleEdgePan(ref settings);
            }
        }

        // 7. 회전 고정 적용
        if (settings.LockRotation)
        {
            _camTransform.rotation = _lockedRotation;
        }

        // 8. 뷰포트 반크기 계산 및 캐싱
        var viewHalfExtent = ComputeViewHalfExtent();
        cameraState.ViewHalfExtent = viewHalfExtent;

        // 9. 카메라 위치 + 뷰포트 반크기 서버 전송 (~20Hz)
        if (++_sendTimer >= 3)
        {
            _sendTimer = 0;
            var rpcEntity = EntityManager.CreateEntity();
            var camPos = _camTransform.position;
            EntityManager.AddComponentData(rpcEntity, new CameraPositionRpc
            {
                Position = new float3(camPos.x, 0f, camPos.z),
                ViewHalfExtent = viewHalfExtent
            });
            EntityManager.AddComponent<SendRpcCommandRequest>(rpcEntity);
        }
    }

    private void HandleHeroFollow(ref CameraSettings settings, ref CameraState cameraState)
    {
        // 타겟 엔티티 유효성 검사 및 검색
        if (cameraState.TargetEntity == Entity.Null || !SystemAPI.Exists(cameraState.TargetEntity))
        {
            if (!TryFindLocalHero(out Entity hero))
                return;
            cameraState.TargetEntity = hero;
        }

        // 타겟 위치 조회 및 이동
        var localTransform = SystemAPI.GetComponent<LocalTransform>(cameraState.TargetEntity);
        float3 heroPos = localTransform.Position;

        Vector3 desired = (Vector3)heroPos + (Vector3)settings.Offset;

        _camTransform.position = Vector3.SmoothDamp(
            _camTransform.position,
            desired,
            ref _velocity,
            settings.SmoothTime
        );
    }

    private void HandleEdgePan(ref CameraSettings settings)
    {
        if (Mouse.current == null) return;

        // 게임 창에 포커스가 없으면 무시
        if (!Application.isFocused) return;

        float2 mousePos = Mouse.current.position.ReadValue();
        float2 screenSize = new float2(Screen.width, Screen.height);

        // 마우스가 화면 밖이거나 유효하지 않은 위치면 무시
        // 모든 방향에서 EdgeThreshold만큼 여유 허용 (대칭 처리)
        if (mousePos.x < -settings.EdgeThreshold || mousePos.y < -settings.EdgeThreshold ||
            mousePos.x > screenSize.x + settings.EdgeThreshold ||
            mousePos.y > screenSize.y + settings.EdgeThreshold)
            return;

        float3 panDirection = float3.zero;

        // 좌측 가장자리
        if (mousePos.x < settings.EdgeThreshold)
            panDirection.x = -1f;
        // 우측 가장자리
        else if (mousePos.x > screenSize.x - settings.EdgeThreshold)
            panDirection.x = 1f;

        // 하단 가장자리
        if (mousePos.y < settings.EdgeThreshold)
            panDirection.z = -1f;
        // 상단 가장자리
        else if (mousePos.y > screenSize.y - settings.EdgeThreshold)
            panDirection.z = 1f;

        // 이동 방향이 있을 때만 처리
        if (math.lengthsq(panDirection) > 0)
        {
            // 대각선 이동 시 속도 일정하게 정규화
            panDirection = math.normalize(panDirection);

            float deltaTime = SystemAPI.Time.DeltaTime;
            Vector3 currentPos = _camTransform.position;

            // XZ 평면 이동, Y축은 유지
            currentPos.x += panDirection.x * settings.EdgePanSpeed * deltaTime;
            currentPos.z += panDirection.z * settings.EdgePanSpeed * deltaTime;

            // 경계 클램프
            currentPos.x = math.clamp(currentPos.x, settings.MapBoundsMin.x, settings.MapBoundsMax.x);
            currentPos.z = math.clamp(currentPos.z, settings.MapBoundsMin.y, settings.MapBoundsMax.y);

            _camTransform.position = currentPos;
        }
    }

    /// <summary>
    /// 뷰포트 4코너를 y=0 평면에 투영하여 카메라 XZ 기준 반크기(halfX, halfZ) 계산.
    /// </summary>
    private float2 ComputeViewHalfExtent()
    {
        var cam = Camera.main;
        if (cam == null) return new float2(30f, 20f);

        var camPos = cam.transform.position;
        float maxDx = 0f, maxDz = 0f;

        for (int i = 0; i < 4; i++)
        {
            float vx = (i & 1) == 0 ? 0f : 1f;
            float vy = (i & 2) == 0 ? 0f : 1f;
            var ray = cam.ViewportPointToRay(new Vector3(vx, vy, 0));

            if (ray.direction.y < -0.001f)
            {
                float t = -ray.origin.y / ray.direction.y;
                var groundPoint = ray.origin + ray.direction * t;
                float dx = Mathf.Abs(groundPoint.x - camPos.x);
                float dz = Mathf.Abs(groundPoint.z - camPos.z);
                if (dx > maxDx) maxDx = dx;
                if (dz > maxDz) maxDz = dz;
            }
        }

        return new float2(
            maxDx > 0f ? maxDx : 30f,
            maxDz > 0f ? maxDz : 20f);
    }

    private bool TryFindLocalHero(out Entity heroEntity)
    {
        heroEntity = Entity.Null;
        int myNetworkId = SystemAPI.GetSingleton<NetworkId>().Value;

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
