// using System.Collections.Generic;
// using Shared;
// using TMPro;
// using Unity.Collections;
// using Unity.Entities;
// using Unity.Rendering;
// using Unity.Transforms;
// using UnityEngine;
//
// [UpdateInGroup(typeof(PresentationSystemGroup))]
// [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.Editor)]
// public partial class EnemyHpTextPresentationSystem : SystemBase
// {
//     private readonly Dictionary<Entity, TextMeshPro> _map = new();
//
//     private EntityQuery _enemyQuery;
//     private Transform _camTransform;
//     private readonly HashSet<Entity> _alive = new();
//     private readonly List<Entity> _toRemove = new();
//
//     private struct CachedHpData
//     {
//         public float CurrentValue;
//         public float MaxValue;
//     }
//
//     private readonly Dictionary<Entity, CachedHpData> _hpCache = new();
//
//     protected override void OnCreate()
//     {
//         _enemyQuery = EntityManager.CreateEntityQuery(new EntityQueryDesc
//         {
//             All = new[]
//             {
//                 ComponentType.ReadOnly<LocalToWorld>(),
//                 ComponentType.ReadOnly<Health>(),
//                 ComponentType.ReadOnly<EnemyTag>()
//             },
//             None = new[] { ComponentType.ReadOnly<DisableRendering>() }
//         });
//     }
//
//     protected override void OnDestroy()
//     {
//         foreach (var kv in _map)
//         {
//             if (kv.Value)
//                 Object.Destroy(kv.Value.gameObject);
//         }
//         _map.Clear();
//         _hpCache.Clear();
//     }
//
//     protected override void OnUpdate()
//     {
//         var bridge = EnemyHpUIBridge.Instance;
//         if (!bridge || !bridge.text3dPrefab)
//             return;
//
//         if (_camTransform == null)
//         {
//             var cam = Camera.main;
//             if (cam == null) return;
//             _camTransform = cam.transform;
//         }
//
//         var em = EntityManager;
//         var camRotation = _camTransform.rotation;
//
//         using NativeArray<Entity> entities = _enemyQuery.ToEntityArray(Allocator.Temp);
//
//         _alive.Clear();
//
//         for (int i = 0; i < entities.Length; i++)
//         {
//             Entity e = entities[i];
//             _alive.Add(e);
//
//             // 1. UI 객체 관리 (없으면 생성)
//             if (!_map.TryGetValue(e, out var tmp) || !tmp)
//             {
//                 tmp = Object.Instantiate(bridge.text3dPrefab);
//                 _map[e] = tmp;
//             }
//
//             // 2. 데이터 가져오기
//             LocalToWorld ltw = em.GetComponentData<LocalToWorld>(e);
//             Health hp = em.GetComponentData<Health>(e);
//             bool isFlying = em.HasComponent<FlyingTag>(e);
//
//             // 3. Flying/Ground 별 높이, 스케일
//             float height = isFlying ? bridge.flyingHeightOffset : bridge.heightOffset;
//             float scale = isFlying ? bridge.flyingScale : bridge.uniformScale;
//
//             // 4. Transform 적용
//             tmp.transform.position = (Vector3)ltw.Position + Vector3.up * height;
//             tmp.transform.rotation = camRotation;
//             tmp.transform.localScale = Vector3.one * scale;
//
//             // 5. HP dirty check → 변경 시에만 텍스트 갱신
//             bool needsUpdate = true;
//             if (_hpCache.TryGetValue(e, out var cached))
//             {
//                 if (cached.CurrentValue == hp.CurrentValue && cached.MaxValue == hp.MaxValue)
//                     needsUpdate = false;
//             }
//
//             if (needsUpdate)
//             {
//                 tmp.SetText("{0:0}/{1:0}", hp.CurrentValue, hp.MaxValue);
//                 _hpCache[e] = new CachedHpData
//                 {
//                     CurrentValue = hp.CurrentValue,
//                     MaxValue = hp.MaxValue
//                 };
//             }
//         }
//
//         // 6. 사라진 엔티티의 UI 제거
//         _toRemove.Clear();
//         foreach (var kv in _map)
//         {
//             if (!_alive.Contains(kv.Key))
//             {
//                 if (kv.Value) Object.Destroy(kv.Value.gameObject);
//                 _toRemove.Add(kv.Key);
//             }
//         }
//
//         for (int i = 0; i < _toRemove.Count; i++)
//         {
//             _map.Remove(_toRemove[i]);
//             _hpCache.Remove(_toRemove[i]);
//         }
//     }
// }
