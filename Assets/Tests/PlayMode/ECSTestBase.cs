using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Tests.PlayMode
{
    /// <summary>
    /// ECS 시스템 테스트를 위한 기반 클래스
    /// World 생성/정리 및 공용 유틸리티 제공
    /// </summary>
    public abstract class ECSTestBase
    {
        protected World MockWorld;
        protected EntityManager MockEntityManager;

        [SetUp]
        public virtual void SetUp()
        {
            MockWorld = new World("TestWorld");
            MockEntityManager = MockWorld.EntityManager;
        }

        [TearDown]
        public virtual void TearDown()
        {
            if (MockWorld != null && MockWorld.IsCreated)
            {
                MockWorld.Dispose();
            }
        }

        /// <summary>
        /// 다음 프레임의 델타타임 설정 (이동 테스트에 필수)
        /// </summary>
        protected void SetNextDeltaTime(float deltaTime)
        {
            var worldTime = MockWorld.Time;
            MockWorld.SetTime(new Unity.Core.TimeData(
                elapsedTime: worldTime.ElapsedTime + deltaTime,
                deltaTime: deltaTime
            ));
        }

        /// <summary>
        /// 기본 위치를 가진 엔티티 생성
        /// </summary>
        protected Entity CreateEntityWithPosition(float3 position)
        {
            var entity = MockEntityManager.CreateEntity();
            MockEntityManager.AddComponentData(entity, LocalTransform.FromPosition(position));
            return entity;
        }

        // TODO: 이동 유닛 생성 헬퍼
        // protected Entity CreateMovingUnit(float3 position, float3 target, float maxSpeed = 10f)
        // {
        //     var entity = m_Manager.CreateEntity();
        //     m_Manager.AddComponentData(entity, LocalTransform.FromPosition(position));
        //     m_Manager.AddComponentData(entity, new PhysicsVelocity());
        //     m_Manager.AddComponentData(entity, new MovementWaypoints { Current = target, HasNext = false, ArrivalRadius = 0.3f });
        //     m_Manager.AddComponentData(entity, new MovementDynamics { MaxSpeed = maxSpeed, Acceleration = 20f, Deceleration = 30f, RotationSpeed = 10f });
        //     m_Manager.AddComponentData(entity, new ObstacleRadius { Radius = 0.5f });
        //     return entity;
        // }

        // TODO: Health를 가진 엔티티 생성 헬퍼
        // protected Entity CreateEntityWithHealth(float maxHealth)
        // {
        //     var entity = m_Manager.CreateEntity();
        //     m_Manager.AddComponentData(entity, new Health { MaxValue = maxHealth, CurrentValue = maxHealth });
        //     m_Manager.AddBuffer<DamageEvent>(entity);
        //     return entity;
        // }
    }
}
