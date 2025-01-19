using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class CameraAuthoring : MonoBehaviour {
	[Range(0f, 5f)]
	public float	speed = 1f;
	[Range(0f, 2f)]
	public float	sensetivity = 1f;
}

public class CameraAuthoringBaker : Baker<CameraAuthoring> {
	public override void Bake(CameraAuthoring authoring) {
		Entity	entity = GetEntity(TransformUsageFlags.Dynamic);
		AddComponent(entity, new CameraDesiredActions() {speed = authoring.speed, sensetivity = authoring.sensetivity});
	}
}

internal struct CameraDesiredActions : IComponentData {
	public float	level;
	public float2	move;
	public float2	look;
	public float	speed;
	public float	sensetivity;
}
