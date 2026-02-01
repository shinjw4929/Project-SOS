// using Unity.Entities;
// using Unity.Mathematics;
// using UnityEngine;
//
// // Enemy 프리팹에서 접촉 데미지/간격/거리(닿음 판정)를 개발자가 조절할 수 있게 한다.
// public class ContactDamageAuthoring : MonoBehaviour
// {
//     public int damage = 1;
//     public float interval = 0.5f;
//     public float range = 1.5f;
//
//     private class ContactDamageBaker : Unity.Entities.Baker<ContactDamageAuthoring>
//     {
//         public override void Bake(ContactDamageAuthoring authoring)
//         {
//             var entity = GetEntity(TransformUsageFlags.Dynamic);
//
//             AddComponent(entity, new ContactDamage
//             {
//                 Damage = math.max(0, authoring.damage),
//                 Interval = math.max(0f, authoring.interval),
//                 Range = math.max(0f, authoring.range)
//             });
//         }
//     }
// }
