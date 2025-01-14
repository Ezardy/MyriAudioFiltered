using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
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

			[ReadOnly] public NativeReference<int> audioFrame;

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
					int	spawnFrame		= clipFrameLookups[clipIndex].spawnFrameOrOffset;
					var	channelWeight	= weights[clipIndex].channelWeights[channelIndex];
					var	itdWeights		= weights[clipIndex].itdWeights;

					double itdMaxOffset	= sampleRate * ITD_TIME;

					for (int itd = 0; itd < itdWeights.Length; itd++) {
						float	weight = itdWeights[itd] * channelWeight;
						double	itdOffset = math.lerp(0, -itdMaxOffset, itd / (double)(itdWeights.Length - 1));
						itdOffset        = math.select(itdOffset, math.lerp(-itdMaxOffset, 0, itd / (double)(itdWeights.Length - 1)), isRightChannel);
						itdOffset        = math.select(itdOffset, 0, itdWeights.Length == 1);
						if (weight > 0f) {
							int	jumpFrames = audioFrame.Value - spawnFrame;
							int	clipStart = jumpFrames * samplesPerFrame + (int)math.round(itdOffset);
							SampleMatchedRate(clipFrameLookups[clipIndex].buffer, clipStart, isRightChannel, weight, outputSamples);
						}
					}
				}
			}

			unsafe readonly void SampleMatchedRate(in FilterSamples samples, int clipStart, bool isRightChannel, float weight, NativeArray<float> output)
			{

				NativeSlice<float>	mergedSamples = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<float>(samples.samplesBuffer, samples.length, Allocator.None);
				NativeSlice<float>	samplesSlice = !samples.stereo ? mergedSamples : isRightChannel ? mergedSamples.SliceWithStride<float>(1) : mergedSamples.SliceWithStride<float>(0);

				int	outputStartIndex = math.max(-clipStart, 0);
				int	remainingClipSamples = samplesSlice.Length - clipStart;
				int	remainingSamples = math.min(output.Length, math.select(0, remainingClipSamples, remainingClipSamples > 0));
				AtomicSafetyHandle	safetyHandle = AtomicSafetyHandle.Create();
				NativeSliceUnsafeUtility.SetAtomicSafetyHandle(ref samplesSlice, safetyHandle);
				for (int i = outputStartIndex; i < remainingSamples; i += 1)
					output[i] += samplesSlice[clipStart + i] * weight;
				AtomicSafetyHandle.Release(safetyHandle);
			}
		}
	}
}
