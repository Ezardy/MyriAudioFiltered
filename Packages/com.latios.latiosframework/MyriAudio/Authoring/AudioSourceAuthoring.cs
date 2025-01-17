using Latios.Authoring;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Latios.Myri.Authoring
{
	[DisallowMultipleComponent]
	[AddComponentMenu("Latios/Myri/Audio Source (Myri)")]
	public class AudioSourceAuthoring : MonoBehaviour
	{
		[Tooltip("If it is set, the clip field will not be used. AudioSystem will use samples from a dynamic buffer")]
		public bool	useFilter;
		[Tooltip("If this and 'Use Filter' are set, filter buffer will be used by AudioSystem on Start")]
		public bool	playOnStart;
		[Tooltip("Is filter buffer Stereo")]
		public bool	isStereoFilterBuffer;
		[Tooltip("An audio clip which will be converted into a DOTS representation and played by this source")]
		public AudioClip clip;
		[Tooltip("Whether or not the source should play the clip in a loop")]
		public bool looping;
		[Tooltip("If enabled, the entity will automatically be destroyed once the clip is finished playing. This option is ignored for looping sources.")]
		public bool autoDestroyOnFinish;
		[Tooltip("The raw volume applied to the audio source, before spatial falloff is applied")]
		public float volume = 1f;
		[Tooltip("When the listener is within this distance to the source, no falloff attenuation is applied")]
		public float innerRange = 5f;
		[Tooltip("When the listener is outside this distance to the source, no audio is heard")]
		public float outerRange = 25f;
		[Tooltip("A distance from the outerRange is which the falloff attenuation is dampened towards 0 where it otherwise wouldn't")]
		public float rangeFadeMargin = 1f;
		[Tooltip(
			 "If true, the audio source begins playing from the beginning when it spawns. This option only affects looping sources. Do not use this for large amounts of looped sources.")
		]
		public bool playFromBeginningAtSpawn;
		[Tooltip("The number of unique voices entities instantiated from this converted Game Object may use. This option only affects looping sources.")]
		public int voices;

		[Header("Cone")]
		[Tooltip("If enabled, directional attenuation is applied")]
		public bool useCone;
		[Tooltip("The inner angle from the entity's forward direction in which no attenuation is applied")]
		public float innerAngle = 30f;
		[Tooltip("The outer angle from the entity's forward direction in which full attenuation is applied")]
		public float outerAngle = 60f;
		[Tooltip("The attenuation to apply at the outer angle. A value of 0 makes the source inaudible outside of the outer angle.")]
		[Range(0f, 1f)]
		public float outerAngleVolume = 0f;
	}

	[TemporaryBakingType]
	public struct AudioSourceBakerWorker : ISmartBakeItem<AudioSourceAuthoring>
	{
		SmartBlobberHandle<AudioClipBlob>	m_handle;
		bool								m_looped;
		bool								m_filtered;

		public bool Bake(AudioSourceAuthoring authoring, IBaker baker)
		{
			var entity = baker.GetEntity(TransformUsageFlags.Renderable);

			baker.GetComponent<AudioClipOverrideBase>();

			m_looped = authoring.looping;
			m_filtered = authoring.useFilter;
			if (authoring.useFilter) {
				baker.AddBuffer<AudioSourceFilterBufferInput>(entity);
				baker.AddComponent(entity, new AudioSourceFilter {
					stereo = authoring.isStereoFilterBuffer,
					play = authoring.playOnStart,
					innerRange = authoring.innerRange,
					outerRange = authoring.outerRange,
					rangeFadeMargin = authoring.rangeFadeMargin,
					volume = authoring.volume
				});
			}
			else if (!authoring.looping)
			{
				baker.AddComponent(entity, new AudioSourceOneShot
				{
					innerRange      = authoring.innerRange,
					outerRange      = authoring.outerRange,
					rangeFadeMargin = authoring.rangeFadeMargin,
					volume          = authoring.volume
				});
				if (authoring.autoDestroyOnFinish)
				{
					baker.AddComponent<AudioSourceDestroyOneShotWhenFinished>(entity);
				}
			}
			else
			{
				baker.AddComponent(entity, new AudioSourceLooped
				{
					innerRange           = authoring.innerRange,
					outerRange           = authoring.outerRange,
					rangeFadeMargin      = authoring.rangeFadeMargin,
					volume               = authoring.volume,
					offsetIsBasedOnSpawn = authoring.playFromBeginningAtSpawn
				});
			}
			if (authoring.useCone)
			{
				baker.AddComponent(entity, new AudioSourceEmitterCone
				{
					cosInnerAngle         = math.cos(math.radians(authoring.innerAngle)),
					cosOuterAngle         = math.cos(math.radians(authoring.outerAngle)),
					outerAngleAttenuation = authoring.outerAngleVolume
				});
			}

			m_handle = baker.RequestCreateBlobAsset(authoring.clip, authoring.voices);
			return m_handle.IsValid || m_filtered;
		}

		public void PostProcessBlobRequests(EntityManager entityManager, Entity entity)
		{
			if (m_filtered) {
				DynamicBuffer<float>	buffer = entityManager.GetBuffer<AudioSourceFilterBufferInput>(entity).Reinterpret<float>();
				AudioSourceFilter		filter = entityManager.GetComponentData<AudioSourceFilter>(entity);
				UnityEngine.AudioSettings.GetDSPBufferSize(out int m_samplesPerFrame, out _);
				int	samplesCount = (filter.stereo ? m_samplesPerFrame * 2 : m_samplesPerFrame) * 3;
				for (int i = 0; i < samplesCount ; i += 1)
					buffer.Add(0.0f);
			} else if (m_looped) {
				var source    = entityManager.GetComponentData<AudioSourceLooped>(entity);
				source.m_clip = m_handle.Resolve(entityManager);
				entityManager.SetComponentData(entity, source);
			} else {
				var source    = entityManager.GetComponentData<AudioSourceOneShot>(entity);
				source.m_clip = m_handle.Resolve(entityManager);
				entityManager.SetComponentData(entity, source);
			}
		}
	}

	public class AudioSourceBaker : SmartBaker<AudioSourceAuthoring, AudioSourceBakerWorker>
	{
	}
}

