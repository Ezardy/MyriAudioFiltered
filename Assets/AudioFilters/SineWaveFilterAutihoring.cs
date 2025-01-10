using Latios.Authoring;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class SineWaveFilterAutihoring : MonoBehaviour {
	public float	tau = math.PI * 2;
	public float	frequency = 440.0f;
	public float	amplitude = 0.5f;
}

public class SineWaveFilterAutihoringBaker : Baker<SineWaveFilterAutihoring> {
	public override void Bake(SineWaveFilterAutihoring authoring) {
		Entity	entity = GetEntity(TransformUsageFlags.Dynamic);
		AddComponent(entity, new SineWaveFilter {tau = authoring.tau, frequency = authoring.frequency, amplitude = authoring.amplitude, phase = 0});
	}
}

public struct SineWaveFilter : IComponentData {
	public float	tau;
	public float	frequency;
	public float	amplitude;
	public float	phase;
}