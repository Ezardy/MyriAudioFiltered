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

			[ReadOnly] public NativeReference<int>	targetFrame;

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

					for (int itd = 0; itd < itdWeights.Length; itd++) {
						float	weight = itdWeights[itd] * channelWeight;
						if (weight > 0f)
							SampleMatchedRate(clipFrameLookups[clipIndex], isRightChannel, weight, outputSamples);
					}
				}
			}

			unsafe readonly void SampleMatchedRate(in BufferFrameLookup bufferFrameLookups, bool isRightChannel, float weight, NativeArray<float> output)
			{
				NativeSlice<float>	mergedSamples = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<float>(bufferFrameLookups.buffer.samplesBuffer, bufferFrameLookups.buffer.length, Allocator.None);
				NativeSlice<float>	samplesSlice = !bufferFrameLookups.buffer.stereo ? mergedSamples : isRightChannel ? mergedSamples.SliceWithStride<float>(1) : mergedSamples.SliceWithStride<float>(0);

				AtomicSafetyHandle	safetyHandle = AtomicSafetyHandle.Create();
				NativeSliceUnsafeUtility.SetAtomicSafetyHandle(ref samplesSlice, safetyHandle);
				int	p1start = bufferFrameLookups.spawnFrameOrOffset * samplesPerFrame;
				int	p1Length = output.Length - p1start;
				int i;
				for (i = 0; i < p1Length; i += 1)
					output[i] = samplesSlice[i + p1start] * weight;
				for (; i < output.Length; i += 1)
					output[i] = samplesSlice[i - p1Length] * weight;
				AtomicSafetyHandle.Release(safetyHandle);
			}
		}
	}
}
