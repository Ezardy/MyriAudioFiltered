using Latios;
using Unity.Burst;
using Unity.Entities;
using Unity.Jobs;
using Latios.Myri;

[RequireMatchingQueriesForUpdate]
[BurstCompile]
public partial struct AudioFilterSystem : ISystem {
	LatiosWorldUnmanaged	latiosWorld;
	int						samplesPerFrame;
	int						outputSampleRate;

	[BurstCompile]
	public void OnCreate(ref SystemState state) {
		latiosWorld = state.GetLatiosWorldUnmanaged();
		UnityEngine.AudioSettings.GetDSPBufferSize(out samplesPerFrame, out _);
		outputSampleRate = UnityEngine.AudioSettings.outputSampleRate;
		latiosWorld.worldBlackboardEntity.AddComponentDataIfMissing(new AudioSettings {
			safetyAudioFrames = 2,
			audioFramesPerUpdate = 1,
			lookaheadAudioFrames = 0,
			logWarningIfBuffersAreStarved = false
		});
	}

	[BurstCompile]
	public readonly void OnDestroy(ref SystemState state) {
		state.CompleteDependency();
	}

	[BurstCompile]
	public void OnUpdate(ref SystemState state) {
		JobHandle		ecsJH = state.Dependency;
		AudioSettings	settings = latiosWorld.worldBlackboardEntity.GetComponentData<AudioSettings>();

		JobHandle	resizeHandle = new BufferResizeJob() {
			settings = settings,
			samplesPerFrame = samplesPerFrame
		}.ScheduleParallel(ecsJH);

		JobHandle	sineHandle = new SineWaveFilterJob() {
			samplesPerFrame = samplesPerFrame,
			outputSampleRate = outputSampleRate,
		}.ScheduleParallel(resizeHandle);

		state.Dependency = sineHandle;
	}
}
