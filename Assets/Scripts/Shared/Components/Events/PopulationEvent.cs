using Unity.Entities;

namespace Shared
{
    /// <summary>
    /// 인구수 변경 이벤트 (사망 시 감소 처리용)
    /// </summary>
    public struct PopulationEvent : IBufferElementData
    {
        public int Delta; // 음수: 감소
    }
}
