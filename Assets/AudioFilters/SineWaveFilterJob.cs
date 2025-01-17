using Latios.Myri;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

[BurstCompile]
public partial struct SineWaveFilterJob : IJobEntity {
	public int				samplesPerFrame;
	public int				outputSampleRate;

	public readonly void	Execute(ref AudioSourceFilter source, ref SineWaveFilter filter, ref DynamicBuffer<AudioSourceFilterBufferInput> buffer) {
		if (source.play) {
			int					channels = source.stereo ? 2 : 1;
			int					offset = source.offset;
			int					frameCount = buffer.Length / samplesPerFrame / channels;
			NativeArray<float>	bufferArray = buffer.Reinterpret<float>().AsNativeArray();
			int					frameDelta = math.abs(source.frame - source.lastFrame);
			int					newFrameCount = source.frame == 0 ||  frameDelta > frameCount ? frameCount : frameDelta;
			
			int	framesToUpdate = newFrameCount;
			for (int i = offset; framesToUpdate > 0 && i < frameCount; i += 1, framesToUpdate -= 1)
				Filter(ref filter, bufferArray.GetSubArray(samplesPerFrame * channels * i, samplesPerFrame * channels), channels);
			for (int i = 0; framesToUpdate > 0 && i < offset; i += 1, framesToUpdate -= 1)
				Filter(ref filter, bufferArray.GetSubArray(samplesPerFrame * channels * i, samplesPerFrame * channels), channels);
			source.offset = (offset + newFrameCount) % frameCount;
		}
	}

	private readonly void	Filter(ref SineWaveFilter filter, NativeArray<float> dspBuffer, int channels) {
		for (int i = 0; i < dspBuffer.Length; i += channels) {
			for (int j = 0; j < channels; j++)
				dspBuffer[i + j] = math.sin(filter.phase) * filter.amplitude;
			filter.phase += filter.frequency * filter.tau / outputSampleRate;
			if (filter.phase > filter.tau)
				filter.phase -= filter.tau;
		}
	}
}
