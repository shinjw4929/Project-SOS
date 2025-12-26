using Unity.Entities;
using Unity.NetCode;

namespace Shared
{
    // 1. 건설 중 태그 (이게 붙어있으면 기능 정지 + 체력 서서히 참)
    // * 건설이 완료되면 이 컴포넌트를 제거(Remove) 합니다.
    [GhostComponent]
    public struct UnderConstructionTag : IComponentData
    {
        [GhostField(Quantization = 100)] public float Progress; // 0.0 ~ 1.0 (진행도)
        [GhostField] public float TotalBuildTime;               // 총 소요 시간
        //[GhostField] public Entity BuilderEntity;               // (선택) 건설하고 있는 일꾼
    }

    // 2. 기능 정지 태그 (EMP, 전력 부족 등)
    // * IEnableableComponent를 써서 껐다 켰다 하면 효율적
    [GhostComponent]
    public struct IsDisabledTag : IComponentData, IEnableableComponent { }
}