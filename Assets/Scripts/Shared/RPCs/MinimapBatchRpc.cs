using Unity.Mathematics;
using Unity.NetCode;

namespace Shared
{
    /// <summary>
    /// 미니맵용 적 위치 배치 RPC. 32개 float2(xz) 좌표를 한 번에 전송.
    /// 서버가 1초 주기로 전체 적 위치를 수집, 32개씩 분할하여 브로드캐스트.
    /// 대역폭: 헤더 9B + 32×8B = 265B/RPC, 2400적 → ~20KB/s per connection.
    /// </summary>
    public struct MinimapBatchRpc : IRpcCommand
    {
        public uint FrameId;
        public ushort StartIndex;
        public ushort TotalCount;
        public byte ValidCount;

        public float2 P00, P01, P02, P03, P04, P05, P06, P07;
        public float2 P08, P09, P10, P11, P12, P13, P14, P15;
        public float2 P16, P17, P18, P19, P20, P21, P22, P23;
        public float2 P24, P25, P26, P27, P28, P29, P30, P31;

        public float2 GetPosition(int index)
        {
            return index switch
            {
                0 => P00, 1 => P01, 2 => P02, 3 => P03,
                4 => P04, 5 => P05, 6 => P06, 7 => P07,
                8 => P08, 9 => P09, 10 => P10, 11 => P11,
                12 => P12, 13 => P13, 14 => P14, 15 => P15,
                16 => P16, 17 => P17, 18 => P18, 19 => P19,
                20 => P20, 21 => P21, 22 => P22, 23 => P23,
                24 => P24, 25 => P25, 26 => P26, 27 => P27,
                28 => P28, 29 => P29, 30 => P30, 31 => P31,
                _ => default,
            };
        }

        public void SetPosition(int index, float2 value)
        {
            switch (index)
            {
                case 0: P00 = value; break; case 1: P01 = value; break;
                case 2: P02 = value; break; case 3: P03 = value; break;
                case 4: P04 = value; break; case 5: P05 = value; break;
                case 6: P06 = value; break; case 7: P07 = value; break;
                case 8: P08 = value; break; case 9: P09 = value; break;
                case 10: P10 = value; break; case 11: P11 = value; break;
                case 12: P12 = value; break; case 13: P13 = value; break;
                case 14: P14 = value; break; case 15: P15 = value; break;
                case 16: P16 = value; break; case 17: P17 = value; break;
                case 18: P18 = value; break; case 19: P19 = value; break;
                case 20: P20 = value; break; case 21: P21 = value; break;
                case 22: P22 = value; break; case 23: P23 = value; break;
                case 24: P24 = value; break; case 25: P25 = value; break;
                case 26: P26 = value; break; case 27: P27 = value; break;
                case 28: P28 = value; break; case 29: P29 = value; break;
                case 30: P30 = value; break; case 31: P31 = value; break;
            }
        }
    }
}
