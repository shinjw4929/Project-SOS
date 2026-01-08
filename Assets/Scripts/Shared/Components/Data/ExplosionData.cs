using Unity.Entities;

namespace Shared
{
    /// <summary>
    /// 자폭/폭발 기능이 있는 건물에 부착
    /// Inspector에서 폭발 범위와 데미지 설정 가능
    /// </summary>
    public struct ExplosionData : IComponentData
    {
        /// <summary>폭발 반경 (월드 단위)</summary>
        public float Radius;

        /// <summary>폭발 데미지 (아군/적군 모두 적용)</summary>
        public float Damage;

        /// <summary>폭발 지연 시간 (0이면 즉시 폭발)</summary>
        public float Delay;
    }
}
