using Latios;
using Latios.Myri;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

[BurstCompile]
public partial struct SineWaveFilterSystem : ISystem {
	int	outputSampleRate;

	[BurstCompile]
	public void OnCreate(ref SystemState state) {
		outputSampleRate = UnityEngine.AudioSettings.outputSampleRate;
	}

	[BurstCompile]
	public readonly void OnDestroy(ref SystemState state) {
	}

	[BurstCompile]
	public void OnUpdate(ref SystemState state) {
		foreach ((RefRO<AudioSourceFilter> source, DynamicBuffer<AudioSourceFilterBufferInput> buffer, RefRW<SineWaveFilter> filter) in SystemAPI.Query<RefRO<AudioSourceFilter>, DynamicBuffer<AudioSourceFilterBufferInput>, RefRW<SineWaveFilter>>()) {
			if (source.ValueRO.play) {
				NativeArray<float>	samples = buffer.AsNativeArray().Reinterpret<float>();
				int	channels = source.ValueRO.stereo ? 2 : 1;
				for (int i = 0; i < samples.Length; i += channels) {
					for (int j = 0; j < channels; j++)
						samples[i + j] = math.sin(filter.ValueRO.phase) * filter.ValueRO.amplitude;
					filter.ValueRW.phase += filter.ValueRO.frequency * filter.ValueRO.tau / outputSampleRate;
					if (filter.ValueRO.phase > filter.ValueRO.tau)
						filter.ValueRW.phase -= filter.ValueRO.tau;
				}
			}
		}
	}
}
