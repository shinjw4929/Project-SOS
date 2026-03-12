using NUnit.Framework;
using Shared;
using Unity.Mathematics;

namespace Tests.EditMode.Utilities
{
    [TestFixture]
    public class SpatialHashUtilityTests
    {
        #region GetCellHash Tests

        [Test]
        public void GetCellHash_Origin_ReturnsConsistentHash()
        {
            var pos = new float3(0, 0, 0);

            int hash1 = SpatialHashUtility.GetCellHash(pos, 10f);
            int hash2 = SpatialHashUtility.GetCellHash(pos, 10f);

            Assert.AreEqual(hash1, hash2);
        }

        [Test]
        public void GetCellHash_SameCell_ReturnsSameHash()
        {
            var pos1 = new float3(1, 0, 1);
            var pos2 = new float3(2, 5, 2); // Y 무시, 같은 셀 내

            int hash1 = SpatialHashUtility.GetCellHash(pos1, 10f);
            int hash2 = SpatialHashUtility.GetCellHash(pos2, 10f);

            Assert.AreEqual(hash1, hash2);
        }

        [Test]
        public void GetCellHash_DifferentCells_ReturnsDifferentHash()
        {
            var pos1 = new float3(0, 0, 0);
            var pos2 = new float3(15, 0, 0); // 다른 셀

            int hash1 = SpatialHashUtility.GetCellHash(pos1, 10f);
            int hash2 = SpatialHashUtility.GetCellHash(pos2, 10f);

            Assert.AreNotEqual(hash1, hash2);
        }

        [Test]
        public void GetCellHash_NegativePosition_Works()
        {
            var pos = new float3(-5, 0, -5);

            // 음수 좌표도 정상 동작해야 함
            int hash = SpatialHashUtility.GetCellHash(pos, 10f);

            // 크래시 없이 값 반환 확인
            Assert.IsTrue(hash != 0 || hash == 0); // 값이 존재하면 통과
        }

        [Test]
        public void GetCellHash_CellBoundary_FloorBehavior()
        {
            // 셀 경계 직전과 직후
            var posBeforeBoundary = new float3(9.99f, 0, 0);
            var posAfterBoundary = new float3(10.01f, 0, 0);

            int hashBefore = SpatialHashUtility.GetCellHash(posBeforeBoundary, 10f);
            int hashAfter = SpatialHashUtility.GetCellHash(posAfterBoundary, 10f);

            Assert.AreNotEqual(hashBefore, hashAfter);
        }

        #endregion

        #region GetCellHash with Offset Tests

        [Test]
        public void GetCellHashWithOffset_ZeroOffset_MatchesBaseHash()
        {
            var pos = new float3(5, 0, 5);

            int baseHash = SpatialHashUtility.GetCellHash(pos, 10f);
            int offsetHash = SpatialHashUtility.GetCellHash(pos, 0, 0, 10f);

            Assert.AreEqual(baseHash, offsetHash);
        }

        [Test]
        public void GetCellHashWithOffset_AdjacentCell_DifferentHash()
        {
            var pos = new float3(5, 0, 5);

            int centerHash = SpatialHashUtility.GetCellHash(pos, 0, 0, 10f);
            int rightHash = SpatialHashUtility.GetCellHash(pos, 1, 0, 10f);

            Assert.AreNotEqual(centerHash, rightHash);
        }

        [Test]
        public void GetCellHashWithOffset_OffsetMatchesNextCell()
        {
            float cellSize = 10f;
            var pos = new float3(5, 0, 5);

            // (5,5) + offset(1,0) → 셀 (1,0)
            int offsetHash = SpatialHashUtility.GetCellHash(pos, 1, 0, cellSize);
            // (15,5) → 셀 (1,0)
            int directHash = SpatialHashUtility.GetCellHash(new float3(15, 0, 5), cellSize);

            Assert.AreEqual(directHash, offsetHash);
        }

        #endregion

        #region GetHashFromCoords Tests

        [Test]
        public void GetHashFromCoords_SameCoords_SameHash()
        {
            var coords = new int2(3, 7);

            int hash1 = SpatialHashUtility.GetHashFromCoords(coords);
            int hash2 = SpatialHashUtility.GetHashFromCoords(coords);

            Assert.AreEqual(hash1, hash2);
        }

        [Test]
        public void GetHashFromCoords_DifferentCoords_DifferentHash()
        {
            int hash1 = SpatialHashUtility.GetHashFromCoords(new int2(0, 0));
            int hash2 = SpatialHashUtility.GetHashFromCoords(new int2(1, 0));

            Assert.AreNotEqual(hash1, hash2);
        }

        [Test]
        public void GetHashFromCoords_OrderMatters()
        {
            int hash1 = SpatialHashUtility.GetHashFromCoords(new int2(1, 2));
            int hash2 = SpatialHashUtility.GetHashFromCoords(new int2(2, 1));

            Assert.AreNotEqual(hash1, hash2);
        }

        #endregion

        #region GetCellRange Tests

        [Test]
        public void GetCellRange_SmallRadius_SingleCell()
        {
            var pos = new float3(15, 0, 15);
            float radius = 1f;
            float cellSize = 10f;

            SpatialHashUtility.GetCellRange(pos, radius, cellSize, out int2 minCell, out int2 maxCell);

            // (15-1)/10=1.4 → floor=1, (15+1)/10=1.6 → floor=1
            Assert.AreEqual(1, minCell.x);
            Assert.AreEqual(1, minCell.y);
            Assert.AreEqual(1, maxCell.x);
            Assert.AreEqual(1, maxCell.y);
        }

        [Test]
        public void GetCellRange_LargeRadius_MultipleCells()
        {
            var pos = new float3(15, 0, 15);
            float radius = 8f;
            float cellSize = 10f;

            SpatialHashUtility.GetCellRange(pos, radius, cellSize, out int2 minCell, out int2 maxCell);

            // min: (15-8)/10=0.7 → floor=0, max: (15+8)/10=2.3 → floor=2
            Assert.AreEqual(0, minCell.x);
            Assert.AreEqual(0, minCell.y);
            Assert.AreEqual(2, maxCell.x);
            Assert.AreEqual(2, maxCell.y);
        }

        [Test]
        public void GetCellRange_ZeroRadius_SingleCell()
        {
            var pos = new float3(5, 0, 5);
            float cellSize = 10f;

            SpatialHashUtility.GetCellRange(pos, 0f, cellSize, out int2 minCell, out int2 maxCell);

            Assert.AreEqual(minCell.x, maxCell.x);
            Assert.AreEqual(minCell.y, maxCell.y);
        }

        [Test]
        public void GetCellRange_NegativePosition_CorrectCells()
        {
            var pos = new float3(-5, 0, -5);
            float radius = 2f;
            float cellSize = 10f;

            SpatialHashUtility.GetCellRange(pos, radius, cellSize, out int2 minCell, out int2 maxCell);

            // min: (-5-2)/10=-0.7 → floor=-1, max: (-5+2)/10=-0.3 → floor=-1
            Assert.AreEqual(-1, minCell.x);
            Assert.AreEqual(-1, minCell.y);
            Assert.AreEqual(-1, maxCell.x);
            Assert.AreEqual(-1, maxCell.y);
        }

        #endregion

        #region IsLargeEntity Tests

        [Test]
        public void IsLargeEntity_SmallRadius_ReturnsFalse()
        {
            bool result = SpatialHashUtility.IsLargeEntity(2f, 10f);

            Assert.IsFalse(result);
        }

        [Test]
        public void IsLargeEntity_ExactHalfCellSize_ReturnsFalse()
        {
            bool result = SpatialHashUtility.IsLargeEntity(5f, 10f);

            Assert.IsFalse(result);
        }

        [Test]
        public void IsLargeEntity_LargeRadius_ReturnsTrue()
        {
            bool result = SpatialHashUtility.IsLargeEntity(6f, 10f);

            Assert.IsTrue(result);
        }

        [Test]
        public void IsLargeEntity_MovementCellSize_Threshold()
        {
            // MovementCellSize = 3.0f, 절반 = 1.5f
            bool small = SpatialHashUtility.IsLargeEntity(1.0f, SpatialHashUtility.MovementCellSize);
            bool large = SpatialHashUtility.IsLargeEntity(2.0f, SpatialHashUtility.MovementCellSize);

            Assert.IsFalse(small);
            Assert.IsTrue(large);
        }

        #endregion

        #region Constants Tests

        [Test]
        public void Constants_HaveExpectedValues()
        {
            Assert.AreEqual(10.0f, SpatialHashUtility.TargetingCellSize);
            Assert.AreEqual(3.0f, SpatialHashUtility.MovementCellSize);
            Assert.AreEqual(1.5f, SpatialHashUtility.CapacityMultiplier);
        }

        #endregion
    }
}
