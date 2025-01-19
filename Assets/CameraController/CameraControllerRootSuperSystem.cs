using Latios;
using Latios.Transforms.Systems;
using Unity.Entities;

[UpdateBefore(typeof(TransformSuperSystem))]
public partial class CameraControllerRootSuperSystem : RootSuperSystem {
	protected override void CreateSystems() {
		GetOrCreateAndAddManagedSystem<PlayerInputSystem>();
		GetOrCreateAndAddUnmanagedSystem<CameraControllerSystem>();
	}
}
