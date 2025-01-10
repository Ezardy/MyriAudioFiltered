using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Latios.Myri {
	internal static partial class Batching {
		[BurstCompile]
		public struct BatchFilterJob : IJob
		{
			[ReadOnly] public NativeArray<FilterEmitter>	emitters;
			[ReadOnly] public NativeStream.Reader			pairWeights;
			[ReadOnly] public NativeStream.Reader			listenerEmitterPairs;

			public NativeList<BufferFrameLookup>	clipFrameLookups;
			public NativeList<Weights>				batchedWeights;
			public NativeList<int>					targetListenerIndices;

			public void Execute()
			{
				var hashmap = new NativeHashMap<BufferFrameListener, int>(INITIAL_ALLOCATION_SIZE, Allocator.Temp);
				if (clipFrameLookups.Capacity < INITIAL_ALLOCATION_SIZE)
					clipFrameLookups.Capacity = INITIAL_ALLOCATION_SIZE;
				if (batchedWeights.Capacity < INITIAL_ALLOCATION_SIZE)
					batchedWeights.Capacity = INITIAL_ALLOCATION_SIZE;
				if (targetListenerIndices.Capacity < INITIAL_ALLOCATION_SIZE)
					targetListenerIndices.Capacity = INITIAL_ALLOCATION_SIZE;

				int streamIndices = listenerEmitterPairs.ForEachCount;
				for (int streamIndex = 0; streamIndex < streamIndices; streamIndex++)
				{
					int countInStream = listenerEmitterPairs.BeginForEachIndex(streamIndex);
					pairWeights.BeginForEachIndex(streamIndex);

					for (; countInStream > 0; countInStream--)
					{
						int2 listenerEmitterPairIndices = listenerEmitterPairs.Read<int2>();
						var  pairWeight                 = pairWeights.Read<Weights>();

						var e = emitters[listenerEmitterPairIndices.y];
						if (!e.source.play)
							continue;

						BufferFrameListener	cfl = new() {
							lookup = new() {buffer = e.samples, spawnFrameOrOffset = e.source.m_spawnedAudioFrame},
							listenerIndex = listenerEmitterPairIndices.x
						};
						if (hashmap.TryGetValue(cfl, out int foundIndex)) {
							ref Weights w  = ref batchedWeights.ElementAt(foundIndex);
							w             += pairWeight;
						} else {
							hashmap.Add(cfl, clipFrameLookups.Length);
							clipFrameLookups.Add(cfl.lookup);
							batchedWeights.Add(pairWeight);
							targetListenerIndices.Add(cfl.listenerIndex);
						}
					}
					listenerEmitterPairs.EndForEachIndex();
					pairWeights.EndForEachIndex();
				}
			}
		}

		private struct BufferFrameListener : IEquatable<BufferFrameListener> {
			public BufferFrameLookup	lookup;
			public int					listenerIndex;

			public readonly bool Equals(BufferFrameListener other) {
				return lookup.Equals(other.lookup) && listenerIndex.Equals(other.listenerIndex);
			}

			public override readonly int GetHashCode() {
				return new int2(lookup.GetHashCode(), listenerIndex).GetHashCode();
			}
		}
	}
}
