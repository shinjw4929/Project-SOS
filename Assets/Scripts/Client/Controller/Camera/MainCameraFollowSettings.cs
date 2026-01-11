using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

// 1. 실제 시스템에서 사용할 순수 데이터 컴포넌트
public struct MainCameraSettingsData : IComponentData
{
    public float3 Offset;
    public float SmoothTime;
    public bool LockRotation;
}

// 2. 인스펙터 저작(Authoring)용 MonoBehaviour
public class MainCameraFollowSettings : MonoBehaviour
{
    public Vector3 offset = new Vector3(0f, 20f, -10f);
    public float smoothTime = 0.12f;
    public bool lockRotation = true;
}

// 3. Authoring 데이터를 Entity 데이터로 변환하는 베이커
public class MainCameraFollowSettingsBaker : Baker<MainCameraFollowSettings>
{
    public override void Bake(MainCameraFollowSettings authoring)
    {
        // 설정용 엔티티이므로 Transform 정보는 필요 없음
        var entity = GetEntity(TransformUsageFlags.None);

        AddComponent(entity, new MainCameraSettingsData
        {
            Offset = authoring.offset, // Vector3 -> float3 자동 변환
            SmoothTime = authoring.smoothTime,
            LockRotation = authoring.lockRotation
        });
    }
}