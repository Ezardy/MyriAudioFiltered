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
}
