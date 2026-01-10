using Unity.Entities;

namespace Shared
{
    /// <summary>
    /// 데미지 이벤트 버퍼
    /// - MeleeAttackSystem, CombatDamageSystem 등에서 데미지를 버퍼에 추가
    /// - DamageApplySystem에서 버퍼의 데미지를 Health에 적용
    /// - Job 스케줄링 충돌 방지를 위한 지연 데미지 적용 패턴
    /// </summary>
    [InternalBufferCapacity(4)] // 한 프레임에 4개 이상의 공격을 받는 경우는 드묾
    public struct DamageEvent : IBufferElementData
    {
        public float Damage;
    }
}
