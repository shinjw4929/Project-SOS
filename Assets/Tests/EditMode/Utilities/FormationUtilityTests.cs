using NUnit.Framework;
using Unity.Mathematics;
using Shared;

namespace Tests.EditMode
{
    public class FormationUtilityTests
    {
        [Test]
        public void SingleUnit_ReturnsZeroOffset()
        {
            var offset = FormationUtility.CalculateFormationOffset(0, 1, 2.0f, new float3(0, 0, 1));
            Assert.AreEqual(0f, offset.x, 0.001f);
            Assert.AreEqual(0f, offset.y, 0.001f);
            Assert.AreEqual(0f, offset.z, 0.001f);
        }

        [Test]
        public void TwoUnits_SideBySide()
        {
            float spacing = 2.0f;
            var dir = new float3(0, 0, 1); // forward
            var offset0 = FormationUtility.CalculateFormationOffset(0, 2, spacing, dir);
            var offset1 = FormationUtility.CalculateFormationOffset(1, 2, spacing, dir);

            // 2 units in 2x1 grid: should be spaced apart on X axis
            float dist = math.distance(offset0, offset1);
            Assert.Greater(dist, spacing * 0.5f, "Two units should be spaced apart");
        }

        [Test]
        public void FourUnits_2x2Grid()
        {
            float spacing = 2.0f;
            var dir = new float3(0, 0, 1);

            var offsets = new float3[4];
            for (int i = 0; i < 4; i++)
                offsets[i] = FormationUtility.CalculateFormationOffset(i, 4, spacing, dir);

            // All offsets should be different
            for (int i = 0; i < 4; i++)
                for (int j = i + 1; j < 4; j++)
                    Assert.Greater(math.distance(offsets[i], offsets[j]), 0.1f,
                        $"Slot {i} and {j} should have different offsets");
        }

        [Test]
        public void NineUnits_3x3Grid()
        {
            float spacing = 2.0f;
            var dir = new float3(0, 0, 1);

            var offsets = new float3[9];
            for (int i = 0; i < 9; i++)
                offsets[i] = FormationUtility.CalculateFormationOffset(i, 9, spacing, dir);

            // Center unit (index 4) should be near origin
            Assert.Less(math.length(offsets[4]), spacing, "Center unit should be near origin");
        }

        [Test]
        public void YComponent_AlwaysZero()
        {
            var dir = new float3(1, 0, 1);
            for (int i = 0; i < 10; i++)
            {
                var offset = FormationUtility.CalculateFormationOffset(i, 10, 2.0f, dir);
                Assert.AreEqual(0f, offset.y, 0.001f, $"Slot {i} Y should be 0");
            }
        }

        [Test]
        public void DirectionRotation_AffectsOffset()
        {
            float spacing = 2.0f;
            var dirForward = new float3(0, 0, 1);
            var dirRight = new float3(1, 0, 0);

            var offsetFwd = FormationUtility.CalculateFormationOffset(0, 4, spacing, dirForward);
            var offsetRight = FormationUtility.CalculateFormationOffset(0, 4, spacing, dirRight);

            // Different directions should produce different offsets
            bool different = math.abs(offsetFwd.x - offsetRight.x) > 0.01f ||
                             math.abs(offsetFwd.z - offsetRight.z) > 0.01f;
            Assert.IsTrue(different, "Different move directions should produce different offsets");
        }
    }
}
