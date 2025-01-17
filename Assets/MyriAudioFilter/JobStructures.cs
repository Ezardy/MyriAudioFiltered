using System;
using Unity.Collections;
using Unity.Mathematics;

namespace Latios.Myri {
	internal struct BufferFrameLookup : IEquatable<BufferFrameLookup> {
		public int				bufferStart;
		public int				channels;
		public int				spawnFrameOrOffset;

		public readonly bool Equals(BufferFrameLookup other) {
			return bufferStart == other.bufferStart && channels == other.channels && spawnFrameOrOffset == other.spawnFrameOrOffset;
		}

		public readonly override int GetHashCode() {
			return new int2(bufferStart, spawnFrameOrOffset).GetHashCode();
		}
	}
	
	internal struct FilterEmitter
	{
		public int						bufferIndex;
		public AudioSourceFilter		source;
		public RigidTransform			transform;
		public AudioSourceEmitterCone	cone;
		public bool						useCone;
	}
}
