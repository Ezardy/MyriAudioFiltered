using Latios;
using Latios.Myri.Systems;
using Latios.Systems;
using Unity.Entities;

[UpdateInGroup(typeof(PreSyncPointGroup))]
[UpdateBefore(typeof(AudioSystem))]
public partial class AudioFiltersRootSuperSystem : RootSuperSystem {
	protected override void CreateSystems() {
		GetOrCreateAndAddUnmanagedSystem<AudioFilterSystem>();
	}
}
