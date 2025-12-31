using System.Collections.Generic;
using TMPro;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

[UpdateInGroup(typeof(PresentationSystemGroup))]
[UpdateAfter(typeof(TransformSystemGroup))] // LocalToWorld ���� ����
public partial class EnemyHpTextPresentationSystem : SystemBase
{
    private readonly Dictionary<Entity, TextMeshPro> _map = new();

    protected override void OnDestroy()
    {
        foreach (var kv in _map)
        {
            if (kv.Value) // Unity Object는 implicit bool 사용
                Object.Destroy(kv.Value.gameObject);
        }
        _map.Clear();
    }

    protected override void OnUpdate()
    {
        // ���� ���� ���� �������� UI ���� �ʿ� ����
        var flags = World.Flags;
        if ((flags & WorldFlags.GameServer) != 0)
            return;

        var bridge = EnemyHpUIBridge.Instance;
        if (!bridge || !bridge.text3dPrefab) // Unity Object는 implicit bool 사용
            return;

        var cam = Camera.main;
        if (!cam) // Unity Object는 implicit bool 사용
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

            if (!_map.TryGetValue(e, out var tmp) || !tmp) // Unity Object는 implicit bool 사용
            {
                tmp = Object.Instantiate(bridge.text3dPrefab);
                _map[e] = tmp;
            }

            // ��ġ/ü��
            LocalToWorld ltw = em.GetComponentData<LocalToWorld>(e);
            EnemyHealthData hp = em.GetComponentData<EnemyHealthData>(e);

            Vector3 pos = (Vector3)ltw.Position + Vector3.up * bridge.heightOffset;
            tmp.transform.position = pos;

            // **UIó�� ���� ����**: ī�޶� ȸ���� ������ ����
            tmp.transform.rotation = cam.transform.rotation;

            // ũ��
            tmp.transform.localScale = Vector3.one * bridge.uniformScale;

            // �ؽ�Ʈ
            tmp.text = $"{hp.Current}/{hp.Max}";
        }

        // ����� ��ƼƼ ����
        var toRemove = new List<Entity>();
        foreach (var kv in _map)
        {
            if (!alive.Contains(kv.Key))
            {
                if (kv.Value) Object.Destroy(kv.Value.gameObject); // Unity Object는 implicit bool 사용
                toRemove.Add(kv.Key);
            }
        }
        for (int i = 0; i < toRemove.Count; i++)
            _map.Remove(toRemove[i]);
    }
}
