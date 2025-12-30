using System.Collections.Generic;
using TMPro;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

[UpdateInGroup(typeof(PresentationSystemGroup))]
[UpdateAfter(typeof(TransformSystemGroup))] // LocalToWorld 갱신 이후
public partial class EnemyHpTextPresentationSystem : SystemBase
{
    private readonly Dictionary<Entity, TextMeshPro> _map = new();

    protected override void OnDestroy()
    {
        foreach (var kv in _map)
        {
            if (kv.Value != null)
                Object.Destroy(kv.Value.gameObject);
        }
        _map.Clear();
    }

    protected override void OnUpdate()
    {
        // 서버 월드 같은 곳에서는 UI 만들 필요 없음
        var flags = World.Flags;
        if ((flags & WorldFlags.GameServer) != 0)
            return;

        var bridge = EnemyHpUIBridge.Instance;
        if (bridge == null || bridge.text3dPrefab == null)
            return;

        var cam = Camera.main;
        if (cam == null)
            return;

        var em = EntityManager;

        EntityQuery q = em.CreateEntityQuery(
            ComponentType.ReadOnly<LocalToWorld>(),
            ComponentType.ReadOnly<EnemyHealthData>()
        );

        using NativeArray<Entity> entities = q.ToEntityArray(Allocator.Temp);

        var alive = new HashSet<Entity>();

        for (int i = 0; i < entities.Length; i++)
        {
            Entity e = entities[i];
            alive.Add(e);

            if (!_map.TryGetValue(e, out var tmp) || tmp == null)
            {
                tmp = Object.Instantiate(bridge.text3dPrefab);
                _map[e] = tmp;
            }

            // 위치/체력
            LocalToWorld ltw = em.GetComponentData<LocalToWorld>(e);
            EnemyHealthData hp = em.GetComponentData<EnemyHealthData>(e);

            Vector3 pos = (Vector3)ltw.Position + Vector3.up * bridge.heightOffset;
            tmp.transform.position = pos;

            // **UI처럼 정면 고정**: 카메라 회전과 완전히 동일
            tmp.transform.rotation = cam.transform.rotation;

            // 크기
            tmp.transform.localScale = Vector3.one * bridge.uniformScale;

            // 텍스트
            tmp.text = $"{hp.Current}/{hp.Max}";
        }

        // 사라진 엔티티 정리
        var toRemove = new List<Entity>();
        foreach (var kv in _map)
        {
            if (!alive.Contains(kv.Key))
            {
                if (kv.Value != null) Object.Destroy(kv.Value.gameObject);
                toRemove.Add(kv.Key);
            }
        }
        for (int i = 0; i < toRemove.Count; i++)
            _map.Remove(toRemove[i]);
    }
}
