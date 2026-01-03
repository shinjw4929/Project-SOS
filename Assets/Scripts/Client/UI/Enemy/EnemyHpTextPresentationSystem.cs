using System.Collections.Generic;
using Shared;
using TMPro;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;

[UpdateInGroup(typeof(PresentationSystemGroup))]
[UpdateAfter(typeof(TransformSystemGroup))]
[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.Editor)]
public partial class EnemyHpTextPresentationSystem : SystemBase
{
    private readonly Dictionary<Entity, TextMeshPro> _map = new();

    protected override void OnDestroy()
    {
        foreach (var kv in _map)
        {
            if (kv.Value)
                Object.Destroy(kv.Value.gameObject);
        }
        _map.Clear();
    }

    protected override void OnUpdate()
    {
        var bridge = EnemyHpUIBridge.Instance;
        if (!bridge || !bridge.text3dPrefab)
            return;

        var cam = Camera.main;
        if (!cam)
            return;

        var em = EntityManager;

        EntityQuery q = em.CreateEntityQuery(
            ComponentType.ReadOnly<LocalToWorld>(),
            ComponentType.ReadOnly<Health>(),
            ComponentType.ReadOnly<EnemyTag>()
        );

        using NativeArray<Entity> entities = q.ToEntityArray(Allocator.Temp);

        var alive = new HashSet<Entity>();

        for (int i = 0; i < entities.Length; i++)
        {
            Entity e = entities[i];
            alive.Add(e);

            // 1. UI 객체 관리 (없으면 생성)
            if (!_map.TryGetValue(e, out var tmp) || !tmp)
            {
                tmp = Object.Instantiate(bridge.text3dPrefab);
                _map[e] = tmp;
            }

            // 2. 데이터 가져오기
            LocalToWorld ltw = em.GetComponentData<LocalToWorld>(e);
            Health hp = em.GetComponentData<Health>(e);

            // 3. 위치 설정 (머리 위 오프셋 적용)
            Vector3 pos = (Vector3)ltw.Position + Vector3.up * bridge.heightOffset;
            tmp.transform.position = pos;

            // 4. 회전 설정 (카메라와 동일하게 -> 빌보드 효과)
            tmp.transform.rotation = cam.transform.rotation;

            // 5. 크기 설정
            tmp.transform.localScale = Vector3.one * bridge.uniformScale;

            // 6. 텍스트 갱신
            tmp.text = $"{hp.CurrentValue}/{hp.MaxValue}";
        }

        // 7. 사라진 엔티티의 UI 제거
        var toRemove = new List<Entity>();
        foreach (var kv in _map)
        {
            if (!alive.Contains(kv.Key))
            {
                if (kv.Value) Object.Destroy(kv.Value.gameObject);
                toRemove.Add(kv.Key);
            }
        }

        for (int i = 0; i < toRemove.Count; i++)
            _map.Remove(toRemove[i]);
    }
}
