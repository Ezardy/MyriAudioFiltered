using Unity.Entities;
using Unity.Jobs;

namespace Latios.Myri {
	public struct AudioSourceFilter : IComponentData {
		public int		offset;
		public int		lastFrame;
		public int		frame;
		public bool		stereo;
		public bool		play;
		public float	volume;
		public float	innerRange;
		public float	outerRange;
		public float	rangeFadeMargin;

		public void ResetPlaybackState() {
			frame = 0;
			lastFrame = 0;
		}
	}

	[InternalBufferCapacity(0)]
	public struct AudioSourceFilterBufferInput : IBufferElementData {
		public float	sample;
	}

	public partial struct AudioFilterJobHandle : ICollectionComponent {
		public JobHandle	handle;

		public JobHandle TryDispose(JobHandle inputDeps) {
			JobHandle jobHandle;
			if (handle.IsCompleted)
				jobHandle = inputDeps;
			else
				jobHandle = JobHandle.CombineDependencies(inputDeps, handle);
			return jobHandle;
		}
	}
}
