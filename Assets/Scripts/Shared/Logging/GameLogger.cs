using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Logging;

namespace Shared
{
    [BurstCompile]
    public static class GameLogger
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedString32Bytes GetPrefix(LogWorld world, LogCategory category)
        {
            FixedString32Bytes prefix = default;
            prefix.Append(world == LogWorld.Server ? (FixedString32Bytes)"[S:" : (FixedString32Bytes)"[C:");
            switch (category)
            {
                case LogCategory.Combat:   prefix.Append((FixedString32Bytes)"Combat] "); break;
                case LogCategory.Movement: prefix.Append((FixedString32Bytes)"Movement] "); break;
                case LogCategory.Network:  prefix.Append((FixedString32Bytes)"Network] "); break;
                case LogCategory.Economy:  prefix.Append((FixedString32Bytes)"Economy] "); break;
                case LogCategory.Wave:     prefix.Append((FixedString32Bytes)"Wave] "); break;
                default:                   prefix.Append((FixedString32Bytes)"System] "); break;
            }
            return prefix;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Debug(LogWorld world, LogCategory category, in FixedString128Bytes message)
        {
            FixedString512Bytes msg = default;
            msg.Append(GetPrefix(world, category));
            msg.Append(message);
            Log.Debug(msg);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Info(LogWorld world, LogCategory category, in FixedString128Bytes message)
        {
            FixedString512Bytes msg = default;
            msg.Append(GetPrefix(world, category));
            msg.Append(message);
            Log.Info(msg);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Warning(LogWorld world, LogCategory category, in FixedString128Bytes message)
        {
            FixedString512Bytes msg = default;
            msg.Append(GetPrefix(world, category));
            msg.Append(message);
            Log.Warning(msg);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Error(LogWorld world, LogCategory category, in FixedString128Bytes message)
        {
            FixedString512Bytes msg = default;
            msg.Append(GetPrefix(world, category));
            msg.Append(message);
            Log.Error(msg);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void InfoWithValue(LogWorld world, LogCategory category, in FixedString64Bytes label, int value)
        {
            FixedString128Bytes combined = default;
            combined.Append(label);
            combined.Append(value);
            Info(world, category, in combined);
        }
    }
}
