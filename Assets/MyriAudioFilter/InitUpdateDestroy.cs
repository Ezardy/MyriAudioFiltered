using Latios.Transforms.Abstract;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Myri {
	internal static partial class InitUpdateDestroy {
		[BurstCompile]
		public struct UpdateFilterJob : IJobChunk {
			public BufferTypeHandle<AudioSourceFilterBufferInput>					bufferHandle;
			public ComponentTypeHandle<AudioSourceFilter>							filteredHandle;
			[ReadOnly] public ComponentTypeHandle<AudioSourceEmitterCone>			coneHandle;
			[ReadOnly] public WorldTransformReadOnlyAspect.TypeHandle				worldTransformHandle;
			[NativeDisableParallelForRestriction] public NativeArray<FilterEmitter>	emitters;
			[ReadOnly] public NativeReference<int>									audioFrame;
			[ReadOnly] public NativeReference<int>									lastPlayedAudioFrame;
			[ReadOnly] public NativeReference<int>									lastConsumedBufferId;
			[NativeDisableParallelForRestriction] public NativeArray<float>			filterBuffers;
			public int																bufferId;

			[ReadOnly, DeallocateOnJobCompletion] public NativeArray<int>	firstEntityInChunkIndices;

			public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask) {

				var firstEntityIndex	= firstEntityInChunkIndices[unfilteredChunkIndex];
				var filters				= chunk.GetNativeArray(ref filteredHandle);
				var buffers				= chunk.GetBufferAccessor(ref bufferHandle);

				for (int i = 0; i < chunk.Count; i++) {
					AudioSourceFilter	filter = filters[i];
					filter.lastFrame = filter.frame;
					filter.frame = audioFrame.Value;
					filters[i] = filter;
				}

				if (chunk.Has(ref coneHandle)) {
					var worldTransforms	= worldTransformHandle.Resolve(chunk);
					var cones			= chunk.GetNativeArray(ref coneHandle);
					for (int i = 0; i < chunk.Count; i++) {
						NativeArray<float> buf = buffers[i].Reinterpret<float>().AsNativeArray();
						int	bufferStart = buf.Length * (filters[i].stereo ? 1 : 2) * (firstEntityIndex + i);
						buf.CopyTo(filterBuffers.GetSubArray(bufferStart, buf.Length));
						emitters[firstEntityIndex + i] = new FilterEmitter {
							bufferIndex = bufferStart,
							source = filters[i],
							transform = new RigidTransform(worldTransforms[i].rotation, worldTransforms[i].position),
							cone = cones[i],
							useCone = true
						};
					}
				} else {
					var worldTransforms = worldTransformHandle.Resolve(chunk);
					for (int i = 0; i < chunk.Count; i++) {
						NativeArray<float> buf = buffers[i].Reinterpret<float>().AsNativeArray();
						int	bufferStart = buf.Length * (filters[i].stereo ? 1 : 2) * (firstEntityIndex + i);
						buf.CopyTo(filterBuffers.GetSubArray(bufferStart, buf.Length));
						emitters[firstEntityIndex + i] = new FilterEmitter {
							bufferIndex = bufferStart,
							source = filters[i],
							transform = new RigidTransform(worldTransforms[i].rotation, worldTransforms[i].position),
							cone		= default,
							useCone		= false
						};
					}
				}
			}
		}
	}
}
