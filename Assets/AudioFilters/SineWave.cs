using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class SineWave : MonoBehaviour {
	private int		outputSampleRate;
	private float	phase = 0.0f;
	
	private void	Start() {
		outputSampleRate = AudioSettings.outputSampleRate;
		GetComponent<AudioSource>().Play();
	}

	private void	OnAudioFilterRead(float[] data, int channels) {
		const float TAU = Mathf.PI * 2.0f;
		const float frequency = 440.0f;

		for (int i = 0; i < data.Length; i += channels) {
			float sample = Mathf.Sin(phase) * 0.5f;

			for (int j = 0; j < channels; j++) {
				data[i + j] += sample;
			}

			phase += frequency * TAU / outputSampleRate;

			if (phase > TAU) {
				phase -= TAU;
			}
		}
	}
}
