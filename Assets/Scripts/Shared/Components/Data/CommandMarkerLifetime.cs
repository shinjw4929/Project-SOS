using Unity.Entities;

namespace Shared
{
    public struct CommandMarkerLifetime : IComponentData
    {
        public float TotalTime;
        public float RemainingTime;
        public float InitialScale;
        public byte MarkerType; // 1=Move, 2=Gather, 3=Attack
    }
}
