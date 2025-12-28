using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Authoring
{
    /*
     * ProjectileFireInputAuthoring
     * - 역할:
     *   특정 GameObject(보통 플레이어/유닛 프리팹)에 "발사 입력 데이터(ProjectileFireInput)" 컴포넌트를
     *   베이킹 단계에서 붙여서, 런타임에 입력 시스템이 값을 써 넣을 수 있게 준비한다.
     *
     * - 왜 필요한가:
     *   ECS/NetCode 구조에서 "입력"은 보통 Ghost(또는 입력을 들고 있는 엔티티)에 저장되고,
     *   클라이언트 입력 시스템이 그 값을 갱신한 뒤 서버가 그 입력을 처리한다.
     *   즉, ProjectileFireInput이 엔티티에 존재해야 입력 시스템이 안전하게 Set/Get 할 수 있다.
     *
     * - 이 프로젝트에서의 사용 시나리오:
     *   (A) CommandTarget 기반 입력(예전 구조)
     *     - ProjectileFireInputSystem 같은 시스템이 CommandTarget.targetEntity에
     *       ProjectileFireInput을 써넣는 방식이면, 그 targetEntity(플레이어 고스트)에
     *       이 컴포넌트가 반드시 존재해야 한다.
     *
     *   (B) RPC 기반 입력(현재 사용 중인 구조)
     *     - 지금은 FireProjectileClientSystem이 RPC를 보내고 서버가 처리하므로
     *       ProjectileFireInput을 실제로 안 쓸 수도 있다.
     *     - 하지만 향후 "입력 컴포넌트 기반"으로 다시 갈 가능성이 있거나,
     *       기존 코드가 ProjectileFireInput 존재를 전제로 한다면 이 Authoring은 유지해도 된다.
     *
     * - 주의:
     *   여기서 GetEntity(TransformUsageFlags.Dynamic)를 쓰면
     *   이 컴포넌트가 붙는 엔티티는 이동/회전을 할 수 있는 동적 트랜스폼을 갖게 된다.
     *   만약 이 Authoring이 붙는 대상이 "입력만 들고 있는 엔티티"라서 트랜스폼이 필요 없다면
     *   TransformUsageFlags.None으로 바꾸는 것이 더 깔끔하다.
     */
    public class ProjectileFireInputAuthoring : MonoBehaviour
    {
        /*
         * Baker
         * - 역할:
         *   ProjectileFireInputAuthoring이 붙은 GameObject를 ECS 엔티티로 변환할 때,
         *   ProjectileFireInput 컴포넌트를 기본값으로 추가한다.
         */
        class Baker : Baker<ProjectileFireInputAuthoring>
        {
            public override void Bake(ProjectileFireInputAuthoring authoring)
            {
                // Authoring이 붙은 GameObject를 엔티티로 변환한다.
                // Dynamic을 사용하면 엔티티에 동적 트랜스폼이 포함될 수 있다.
                Entity entity = GetEntity(TransformUsageFlags.Dynamic);

                // 발사 입력 컴포넌트를 기본값으로 추가한다.
                // Fire는 이번 틱/프레임에 발사했는지 여부 같은 플래그로 쓰는 값이고,
                // TargetPosition은 마우스 월드 좌표 등 발사 목표 지점으로 쓰는 값이다.
                AddComponent(entity, new ProjectileFireInput
                {
                    Fire = 0,
                    TargetPosition = float3.zero
                });
            }
        }
    }
}
