using NUnit.Framework;
using Shared;
using Unity.Mathematics;

namespace Tests.EditMode.Utilities
{
    [TestFixture]
    public class TeamColorPaletteTests
    {
        #region Known TeamId Tests

        [Test]
        public void GetTeamColor_Enemy_ReturnsWhite()
        {
            float4 result = TeamColorPalette.GetTeamColor(-1);

            AssertColor(1f, 1f, 1f, 1f, result);
        }

        [Test]
        public void GetTeamColor_ScenePlaced_ReturnsWhite()
        {
            float4 result = TeamColorPalette.GetTeamColor(0);

            AssertColor(1f, 1f, 1f, 1f, result);
        }

        [Test]
        public void GetTeamColor_User1_ReturnsRed()
        {
            float4 result = TeamColorPalette.GetTeamColor(1);

            AssertColor(1.0f, 0.3f, 0.3f, 1f, result);
        }

        [Test]
        public void GetTeamColor_User2_ReturnsBlue()
        {
            float4 result = TeamColorPalette.GetTeamColor(2);

            AssertColor(0.3f, 0.6f, 1.0f, 1f, result);
        }

        [Test]
        public void GetTeamColor_User3_ReturnsGreen()
        {
            float4 result = TeamColorPalette.GetTeamColor(3);

            AssertColor(0.3f, 0.9f, 0.3f, 1f, result);
        }

        [Test]
        public void GetTeamColor_User4_ReturnsYellow()
        {
            float4 result = TeamColorPalette.GetTeamColor(4);

            AssertColor(1.0f, 0.9f, 0.2f, 1f, result);
        }

        [Test]
        public void GetTeamColor_User5_ReturnsOrange()
        {
            float4 result = TeamColorPalette.GetTeamColor(5);

            AssertColor(1.0f, 0.6f, 0.2f, 1f, result);
        }

        [Test]
        public void GetTeamColor_User6_ReturnsPurple()
        {
            float4 result = TeamColorPalette.GetTeamColor(6);

            AssertColor(0.7f, 0.3f, 1.0f, 1f, result);
        }

        [Test]
        public void GetTeamColor_User7_ReturnsCyan()
        {
            float4 result = TeamColorPalette.GetTeamColor(7);

            AssertColor(0.2f, 0.9f, 0.9f, 1f, result);
        }

        [Test]
        public void GetTeamColor_User8_ReturnsPink()
        {
            float4 result = TeamColorPalette.GetTeamColor(8);

            AssertColor(1.0f, 0.5f, 0.7f, 1f, result);
        }

        #endregion

        #region Default / Edge Cases

        [Test]
        public void GetTeamColor_UnknownPositiveId_ReturnsWhite()
        {
            float4 result = TeamColorPalette.GetTeamColor(9);

            AssertColor(1f, 1f, 1f, 1f, result);
        }

        [Test]
        public void GetTeamColor_LargeId_ReturnsWhite()
        {
            float4 result = TeamColorPalette.GetTeamColor(999);

            AssertColor(1f, 1f, 1f, 1f, result);
        }

        [Test]
        public void GetTeamColor_NegativeLargeId_ReturnsWhite()
        {
            float4 result = TeamColorPalette.GetTeamColor(-100);

            AssertColor(1f, 1f, 1f, 1f, result);
        }

        [Test]
        public void GetTeamColor_AllTeams_AlphaIsOne()
        {
            for (int i = -1; i <= 8; i++)
            {
                float4 color = TeamColorPalette.GetTeamColor(i);
                Assert.AreEqual(1f, color.w, 0.001f, $"TeamId {i}의 알파가 1이 아닙니다.");
            }
        }

        [Test]
        public void GetTeamColor_AllUserTeams_HaveDistinctColors()
        {
            // 유저 팀 (1~8)은 모두 서로 다른 색상이어야 함
            var colors = new float4[8];
            for (int i = 0; i < 8; i++)
            {
                colors[i] = TeamColorPalette.GetTeamColor(i + 1);
            }

            for (int i = 0; i < colors.Length; i++)
            {
                for (int j = i + 1; j < colors.Length; j++)
                {
                    bool same = math.abs(colors[i].x - colors[j].x) < 0.001f &&
                                math.abs(colors[i].y - colors[j].y) < 0.001f &&
                                math.abs(colors[i].z - colors[j].z) < 0.001f;
                    Assert.IsFalse(same, $"TeamId {i + 1}과 {j + 1}의 색상이 동일합니다.");
                }
            }
        }

        #endregion

        #region Helper

        static void AssertColor(float r, float g, float b, float a, float4 actual)
        {
            Assert.AreEqual(r, actual.x, 0.001f, "R 채널 불일치");
            Assert.AreEqual(g, actual.y, 0.001f, "G 채널 불일치");
            Assert.AreEqual(b, actual.z, 0.001f, "B 채널 불일치");
            Assert.AreEqual(a, actual.w, 0.001f, "A 채널 불일치");
        }

        #endregion
    }
}
