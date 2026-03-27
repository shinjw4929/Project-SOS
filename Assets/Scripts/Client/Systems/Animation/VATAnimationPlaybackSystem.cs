using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Shared;

namespace Client
{
    /// <summary>
    /// VATAnimParam.Value를 매 프레임 계산한다.
    /// ISystem의 OnUpdate는 Job 스케줄링만 수행하므로 Burst 불필요 -- Job 자체가 [BurstCompile].
    /// BlobAsset에서 클립 메타데이터를 읽어 normalizedTime, clipStartRow, clipRowCount를 정규화하여 셰이더에 전달.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(TeamColorSystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial struct VATAnimationPlaybackSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<VATAnimationState>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var job = new VATPlaybackJob
            {
                AnimStateLookup = SystemAPI.GetComponentLookup<VATAnimationState>(true),
                ClipLibraryLookup = SystemAPI.GetComponentLookup<VATClipLibrary>(true),
                ElapsedTime = SystemAPI.Time.ElapsedTime
            };
            state.Dependency = job.ScheduleParallel(state.Dependency);
        }
    }

    [BurstCompile]
    public partial struct VATPlaybackJob : IJobEntity
    {
        [ReadOnly] public ComponentLookup<VATAnimationState> AnimStateLookup;
        [ReadOnly] public ComponentLookup<VATClipLibrary> ClipLibraryLookup;
        public double ElapsedTime;

        void Execute(in VATAnimTarget target, ref VATAnimParam param)
        {
            if (target.RootEntity == Entity.Null) return;
            if (!AnimStateLookup.TryGetComponent(target.RootEntity, out var animState)) return;
            if (!ClipLibraryLookup.TryGetComponent(target.RootEntity, out var clipLib)) return;

            ref var blobData = ref clipLib.Value.Value;
            int clipIndex = math.clamp(animState.CurrentClipIndex, 0, blobData.Clips.Length - 1);
            ref var clip = ref blobData.Clips[clipIndex];

            float elapsed = math.max(0f, (float)(ElapsedTime - animState.AnimStartTime));
            float clipDuration = clip.RowCount / math.max(clip.Fps, 0.001f);

            float normalizedTime;
            if (clip.Loop)
                normalizedTime = math.fmod(elapsed, clipDuration) / clipDuration;
            else
                normalizedTime = math.saturate(elapsed / clipDuration);

            float textureHeight = blobData.TextureHeight;
            param.Value = new float4(
                normalizedTime,
                clip.StartRow / textureHeight,
                clip.RowCount / textureHeight,
                0
            );
        }
    }
}
