using System.Linq;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Latios.Myri {
	internal static partial class Sampling {
		[BurstCompile]
		public struct SampleFilterBufferJob : IJobParallelForDefer
		{
			[ReadOnly] public NativeArray<BufferFrameLookup>	clipFrameLookups;
			[ReadOnly] public NativeArray<Weights>				weights;
			[ReadOnly] public NativeArray<int>					targetListenerIndices;

			[ReadOnly] public NativeArray<ListenerBufferParameters>	listenerBufferParameters;
			[ReadOnly] public NativeArray<int2>						forIndexToListenerAndChannelIndices;

			[NativeDisableParallelForRestriction] public NativeArray<float>	outputSamplesMegaBuffer;

			public int	sampleRate;

			public int	samplesPerFrame;

			public void Execute(int forIndex)
			{
				int		listenerIndex				= forIndexToListenerAndChannelIndices[forIndex].x;
				int		channelIndex				= forIndexToListenerAndChannelIndices[forIndex].y;
				var		targetListenerParameters	= listenerBufferParameters[listenerIndex];
				bool	isRightChannel				= targetListenerParameters.leftChannelsCount <= channelIndex;

				var outputSamples = outputSamplesMegaBuffer.GetSubArray(targetListenerParameters.bufferStart + targetListenerParameters.samplesPerChannel * channelIndex,
																		targetListenerParameters.samplesPerChannel);

				for (int clipIndex = 0; clipIndex < clipFrameLookups.Length; clipIndex++) {
					if (targetListenerIndices[clipIndex] != listenerIndex)
						continue;
					var	channelWeight	= weights[clipIndex].channelWeights[channelIndex];
					var	itdWeights		= weights[clipIndex].itdWeights;

					double itdMaxOffset = sampleRate * ITD_TIME;

					for (int itd = 0; itd < itdWeights.Length; itd++) {
						float weight = itdWeights[itd] * channelWeight;

						double itdOffset	= math.lerp(0, -itdMaxOffset, itd / (double)(itdWeights.Length - 1));
						itdOffset			= math.select(itdOffset, math.lerp(-itdMaxOffset, 0, itd / (double)(itdWeights.Length - 1)), isRightChannel);
						itdOffset			= math.select(itdOffset, 0, itdWeights.Length == 1);
						if (weight > 0f)
							SampleMatchedRate(clipFrameLookups[clipIndex].buffer, (int)math.round(itdOffset), isRightChannel, weight, outputSamples);
					}
				}
			}

			readonly void SampleMatchedRate(in FilterSamples samples, int clipStart, bool isRightChannel, float weight, NativeArray<float> output)
			{
				int	outputStartIndex = math.max(-clipStart, 0);
				int	remainingSamples = samples.stereo ? samples.samples.Length / 2 : samples.samples.Length;

				NativeSlice<float>	mergedSamples = samples.samples.AsNativeArray();
				NativeSlice<float>	samplesSlice = !samples.stereo ? mergedSamples : isRightChannel ? mergedSamples.SliceWithStride<float>(1) : mergedSamples.SliceWithStride<float>(0);
				for (int i = outputStartIndex; i < remainingSamples; i += 1)
					output[i] += samplesSlice[clipStart + i] * weight;
			}
		}
	}
}
