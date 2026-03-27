namespace Shared
{
    public enum SoundType : byte
    {
        None = 0,

        // 전투
        MeleeHit = 10,
        RangedShot = 11,
        UnitDeath = 12,
        EnemyDeath = 13,

        // 작업
        WorkerGather = 20,
        BuildingPlace = 21,
        BuildingComplete = 22,

        // 이동 (향후 확장용)
        MoveCommand = 30,
    }
}
