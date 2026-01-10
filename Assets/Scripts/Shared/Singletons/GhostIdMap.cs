using Unity.Entities;
using Unity.Collections;

namespace Shared
{
    // 다른 시스템(CommandProcessingSystem)에서 참조할 데이터 컨테이너
    public struct GhostIdMap : IComponentData
    {
        public NativeParallelHashMap<int, Entity> Map;
    }
}