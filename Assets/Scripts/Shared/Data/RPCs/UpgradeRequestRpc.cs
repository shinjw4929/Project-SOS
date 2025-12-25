using Unity.NetCode;

namespace Shared
{
    public struct UpgradeRequestRpc : IRpcCommand
    {
        public UnitTypeEnum unitType;
        public UpgradeStatType statType;
        public float multiplierChange;
    }

    public enum UpgradeStatType : byte
    {
        MoveSpeed = 0,
        AttackPower = 1,
    }
}
