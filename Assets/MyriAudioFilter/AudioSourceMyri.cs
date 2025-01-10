using Unity.Entities;

namespace Latios.Myri {
	public struct AudioSourceFilter : IComponentData {
		internal int	m_spawnedAudioFrame;
		internal int	m_spawnedBufferId;

		public bool	stereo;
		public bool		play;
		public float	volume;
		public float	innerRange;
		public float	outerRange;
		public float	rangeFadeMargin;

		public void ResetPlaybackState() {
			m_spawnedAudioFrame = 0;
			m_spawnedBufferId = 0;
		}
		internal readonly bool	IsInitialized => (m_spawnedBufferId != 0) | (m_spawnedBufferId != m_spawnedAudioFrame);
	}

	public struct AudioSourceFilterBufferInput : IBufferElementData {
		public float	sample;
	}
}
