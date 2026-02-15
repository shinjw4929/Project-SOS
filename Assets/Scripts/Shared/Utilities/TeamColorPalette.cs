using System.Runtime.CompilerServices;
using Unity.Mathematics;

namespace Shared
{
    public static class TeamColorPalette
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float4 GetTeamColor(int teamId)
        {
            switch (teamId)
            {
                case -1: return new float4(1f, 1f, 1f, 1f);             // 적: 빈 틴트 (원본 유지)
                case 0:  return new float4(1f, 1f, 1f, 1f);             // 씬 배치: 빈 틴트 (원본 유지)
                case 1:  return new float4(1.0f, 0.3f, 0.3f, 1f);    // 유저 1: 빨강
                case 2:  return new float4(0.3f, 0.6f, 1.0f, 1f);    // 유저 2: 파랑
                case 3:  return new float4(0.3f, 0.9f, 0.3f, 1f);    // 유저 3: 초록
                case 4:  return new float4(1.0f, 0.9f, 0.2f, 1f);    // 유저 4: 노랑
                case 5:  return new float4(1.0f, 0.6f, 0.2f, 1f);    // 유저 5: 주황
                case 6:  return new float4(0.7f, 0.3f, 1.0f, 1f);    // 유저 6: 보라
                case 7:  return new float4(0.2f, 0.9f, 0.9f, 1f);    // 유저 7: 시안
                case 8:  return new float4(1.0f, 0.5f, 0.7f, 1f);    // 유저 8: 핑크
                default: return new float4(1f, 1f, 1f, 1f);
            }
        }

    }
}
