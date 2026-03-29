using Unity.Collections;
using Unity.NetCode;

namespace Shared
{
    public struct GoInGameRequestRpc : IRpcCommand
    {
        public FixedString128Bytes AuthToken;
    }
}
