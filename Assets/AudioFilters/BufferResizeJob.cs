using Latios.Myri;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

[BurstCompile]
public partial struct BufferResizeJob : IJobEntity {
	public AudioSettings	settings;
	public int				samplesPerFrame;

	public readonly void	Execute(in AudioSourceFilter source, ref DynamicBuffer<AudioSourceFilterBufferInput> buffer) {
		buffer.Resize((settings.audioFramesPerUpdate + settings.safetyAudioFrames + settings.lookaheadAudioFrames) * samplesPerFrame * (source.stereo ? 2 : 1), NativeArrayOptions.ClearMemory);
	}
}
