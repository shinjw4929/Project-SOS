// [주석처리] HeroHpTextPresentationSystem - 비활성화됨
/*
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using UnityEngine;

// 클라이언트에서만 UI를 갱신한다.
// GhostInstance가 있는 엔티티만 읽어서, SubScene/로컬 엔티티(초기값 100)를 잡는 문제를 막는다.
// 1순위: GhostOwnerIsLocal(내 Hero)
// 2순위: 소유자 설정이 아직 안 된 경우를 대비해서 Ghost Hero 하나를 표시(디버그용)
//       소유자 세팅이 끝나면 2순위는 지워도 된다.
[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
[UpdateInGroup(typeof(PresentationSystemGroup))]
public partial class HeroHpTextPresentationSystem : SystemBase
{
    private HeroHpTextManager ui;
    private bool loggedNoLocalOwner;

    protected override void OnUpdate()
    {
        if (ui == null)
        {
            ui = Object.FindAnyObjectByType<HeroHpTextManager>();
            if (ui == null)
                return;
        }

        // 1순위: 내 Hero(OwnerIsLocal) + Ghost 엔티티만
        {
            var q = SystemAPI.QueryBuilder()
                .WithAll<HeroTag, HeroHealth, GhostOwnerIsLocal, GhostInstance>()
                .Build();

            if (!q.IsEmptyIgnoreFilter)
            {
                var h = q.GetSingleton<HeroHealth>();
                ui.SetHp(math.max(0, h.Current), math.max(1, h.Max));
                return;
            }
        }

        // 여기로 왔다는 건 "내 Hero 소유자"가 아직 안 잡힌 상태일 가능성이 크다.
        if (!loggedNoLocalOwner)
        {
            loggedNoLocalOwner = true;
            Debug.LogWarning("[Client] GhostOwnerIsLocal Hero를 못 찾았습니다. 서버에서 Hero 생성 시 GhostOwner(NetworkId)를 세팅해야 각자 자기 HP만 표시됩니다.");
        }

        // 2순위(디버그): Ghost Hero 아무거나 하나 표시 (값 동기화가 되는지 확인용)
        {
            var q = SystemAPI.QueryBuilder()
                .WithAll<HeroTag, HeroHealth, GhostInstance>()
                .Build();

            if (q.IsEmptyIgnoreFilter)
                return;

            using var entities = q.ToEntityArray(Allocator.Temp);
            var h = EntityManager.GetComponentData<HeroHealth>(entities[0]);
            ui.SetHp(math.max(0, h.Current), math.max(1, h.Max));
        }
    }
}
*/
