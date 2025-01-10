using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Latios.Myri {
	internal static partial class CullingAndWeighting {
		[BurstCompile]
		public struct FilterJob : IJobParallelForBatch {
			[ReadOnly] public NativeList<ListenerWithTransform>					listenersWithTransforms;
			[ReadOnly] public NativeArray<FilterEmitter>						emitters;
			[NativeDisableParallelForRestriction] public NativeStream.Writer	weights;
			[NativeDisableParallelForRestriction] public NativeStream.Writer	listenerEmitterPairs;

			public void Execute(int startIndex, int count) {
				var scratchCache = new NativeList<float4>(Allocator.Temp);

				var baseWeights = new NativeArray<Weights>(listenersWithTransforms.Length, Allocator.Temp, NativeArrayOptions.ClearMemory);
				for (int i = 0; i < listenersWithTransforms.Length; i++) {
					int c = listenersWithTransforms[i].listener.ildProfile.Value.anglesPerLeftChannel.Length +
							listenersWithTransforms[i].listener.ildProfile.Value.anglesPerRightChannel.Length;
					Weights w = default;
					for (int j = 0; j < c; j++)
						w.channelWeights.Add(0f);
					c = listenersWithTransforms[i].listener.itdResolution;
					c = 2 * c + 1;
					for (int j = 0; j < c; j++)
						w.itdWeights.Add(0f);
					baseWeights[i] = w;
				}

				listenerEmitterPairs.BeginForEachIndex(startIndex / kBatchSize);
				weights.BeginForEachIndex(startIndex / kBatchSize);

				for (int i = startIndex; i < startIndex + count; i++)
				{
					var emitter = emitters[i];
					for (int j = 0; j < listenersWithTransforms.Length; j++)
					{
						if (math.distancesq(emitter.transform.pos,
											listenersWithTransforms[j].transform.pos) < emitter.source.outerRange * emitter.source.outerRange && emitter.source.play) {
							var w = baseWeights[j];

							EmitterParameters e = new() {
								cone            = emitter.cone,
								innerRange      = emitter.source.innerRange,
								outerRange      = emitter.source.outerRange,
								rangeFadeMargin = emitter.source.rangeFadeMargin,
								transform       = emitter.transform,
								useCone         = emitter.useCone,
								volume          = emitter.source.volume
							};
							ComputeWeights(ref w, e, listenersWithTransforms[j], scratchCache);

							weights.Write(w);
							listenerEmitterPairs.Write(new int2(j, i));
						}
					}
				}

				listenerEmitterPairs.EndForEachIndex();
				weights.EndForEachIndex();
			}
		}
	}
}
