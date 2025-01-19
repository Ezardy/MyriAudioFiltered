using Latios;
using Unity.Burst;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Latios.Transforms;

[BurstCompile]
internal partial struct CameraControllerSystem : ISystem {
	LatiosWorldUnmanaged latiosWorld;

	[BurstCompile]
	public void OnCreate(ref SystemState state) {
		latiosWorld = state.GetLatiosWorldUnmanaged();
	}

	[BurstCompile]
	public readonly void OnDestroy(ref SystemState state) {
		state.CompleteDependency();
	}

	[BurstCompile]
	public void OnUpdate(ref SystemState state) {
		JobHandle	controlHandle = new CameraControllerJob().ScheduleParallel(state.Dependency);
		state.Dependency = controlHandle;
	}
}

internal partial struct CameraControllerJob : IJobEntity {
	public readonly void	Execute(TransformAspect transform, in CameraDesiredActions actions) {
		quaternion	xTurn = quaternion.AxisAngle(transform.rightDirection, math.radians(-actions.look.y * actions.sensetivity));
		quaternion	yTurn = quaternion.AxisAngle(transform.upDirection, math.radians(actions.look.x * actions.sensetivity));
		transform.RotateWorld(math.mul(xTurn, yTurn));
		float3	localTranslation = new(actions.move.x * actions.speed, actions.level * actions.speed, actions.move.y * actions.speed);
		transform.TranslateWorld(transform.TransformDirectionLocalToWorld(localTranslation));
	}
}
