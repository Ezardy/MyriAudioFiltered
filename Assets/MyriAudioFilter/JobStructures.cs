using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Myri {
	internal struct BufferFrameLookup : IEquatable<BufferFrameLookup> {
		public FilterSamples	buffer;
		public int				spawnFrameOrOffset;

		public readonly bool Equals(BufferFrameLookup other) {
			return buffer.samples.AsNativeArray() == other.buffer.samples.AsNativeArray() && buffer.stereo == other.buffer.stereo && spawnFrameOrOffset == other.spawnFrameOrOffset;
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

	
	internal struct FilterSamples {
		public bool					stereo;
		public DynamicBuffer<float>	samples;

		public readonly override int	GetHashCode() {
			return new int2(stereo ? 1 : 0, samples.AsNativeArray().GetHashCode()).GetHashCode();
		}
	}
}
