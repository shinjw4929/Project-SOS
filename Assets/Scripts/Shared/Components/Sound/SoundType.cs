namespace Shared
{
    public enum SoundType : byte
    {
        None = 0,

        // 전투 — 아군
        MeleeHit = 10,
        RangedShot = 11,
        UnitDeath = 12,

        // 전투 — 적
        EnemyDeath = 13,
        EnemyMeleeHit = 14,
        EnemyRangedShot = 15,

        // 작업
        WorkerGather = 20,
        BuildingPlace = 21,
        BuildingComplete = 22,

        // 유닛 스폰
        UnitSpawn = 25,

        // 이동 (향후 확장용)
        MoveCommand = 30,
    }
}
