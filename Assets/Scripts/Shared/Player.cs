using Unity.Entities;
using Unity.NetCode; // [필수] 이 네임스페이스가 있어야 합니다.

namespace Shared
{
    public struct Player : IComponentData
    {
        // [GhostField]: 이 값이 서버에서 바뀌면 클라이언트한테도 전송하라는 뜻입니다.
        [GhostField] 
        public int TeamId; 
    }
}