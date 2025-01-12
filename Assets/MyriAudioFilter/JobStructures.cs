using System;
using Unity.Collections;
using Unity.Mathematics;

namespace Latios.Myri {
	internal struct BufferFrameLookup : IEquatable<BufferFrameLookup> {
		public FilterSamples	buffer;
		public int				spawnFrameOrOffset;

		public unsafe readonly bool Equals(BufferFrameLookup other) {
			return buffer.samplesBuffer == other.buffer.samplesBuffer && buffer.stereo == other.buffer.stereo && buffer.length == other.buffer.length && spawnFrameOrOffset == other.spawnFrameOrOffset;
		}

		public readonly override int GetHashCode() {
			return new int2(buffer.GetHashCode(), spawnFrameOrOffset).GetHashCode();
		}
	}
	
	internal struct FilterEmitter
	{

		public FilterSamples			samples;
		public AudioSourceFilter		source;
		public RigidTransform			transform;
		public AudioSourceEmitterCone	cone;
		public bool						useCone;
	}

	
	internal unsafe struct FilterSamples {
		public bool		stereo;
		public float*	samplesBuffer;
		public int		length;

		public readonly override int	GetHashCode() {
			return new int3(stereo ? 1 : 0, (int)((ulong)samplesBuffer >> 4), length).GetHashCode();
		}
	}
}
