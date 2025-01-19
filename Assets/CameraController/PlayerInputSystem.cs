using Latios;
using UnityEngine;
using Unity.Entities;

public partial class PlayerInputSystem : SubSystem {
	InputSystem_Actions	m_actions;

	protected override void	OnCreate() {
		m_actions = new();
		m_actions.Enable();
	}

	protected override void	OnDestroy() {
		m_actions.Dispose();
	}

	protected override void OnUpdate() {
		InputSystem_Actions.PlayerActions	actions = m_actions.Player;

		foreach (RefRW<CameraDesiredActions> camera in SystemAPI.Query<RefRW<CameraDesiredActions>>()) {
			camera.ValueRW.look = actions.Look.ReadValue<Vector2>();
			camera.ValueRW.move = actions.Move.ReadValue<Vector2>();
			camera.ValueRW.level = actions.Level.ReadValue<float>();
		}
	}
}
