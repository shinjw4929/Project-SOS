using Unity.NetCode;
using Unity.Entities;
using UnityEngine;

public class NetcodePlayerInputAuthoring : MonoBehaviour
{
    public class Baker : Baker<NetcodePlayerInputAuthoring>
    {
        public override void Bake(NetcodePlayerInputAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new NetcodePlayerInput());
        }
    }
}
