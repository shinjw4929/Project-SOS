using Unity.Entities;
using Unity.Mathematics;

public struct ProjectileFireInput : IComponentData
{
    public byte Fire;

    // F 누른 순간의 마우스 월드 위치
    public float3 TargetPosition;
}
