using Unity.NetCode;
using Unity.Mathematics;

namespace Shared
{
    public struct RTSCommand : ICommandData
    {
        public NetworkTick Tick { get; set; }

        public RTSCommandType commandType;
        public float3 targetPosition;
    }

    public enum RTSCommandType : byte
    {
        None = 0,
        Move = 1,
    }
}
