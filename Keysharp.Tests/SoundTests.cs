namespace Keysharp.Tests;

public partial class SoundTests : TestRunner
{
	[Test, Category("Sound")]
	public void SoundBeep() => Assert.IsTrue(TestScript("sound-soundbeep", true));

	// The synthesized tone is what makes SoundBeep honour Frequency/Duration on Linux and macOS, so the
	// WAV it produces is verified here rather than trusted — this runs on every platform and needs no
	// audio device. (Playback itself still needs a real desktop; see the manual-verification notes.)
	[Test, Category("Misc"), Category("Internal")]
	public void ToneWav()
	{
		const int rate = 44100;
		var wav = SoundPlayback.BuildToneWav(440, 250, rate);
		var text = System.Text.Encoding.ASCII;
		Assert.That(text.GetString(wav, 0, 4), Is.EqualTo("RIFF"));
		Assert.That(text.GetString(wav, 8, 4), Is.EqualTo("WAVE"));
		Assert.That(text.GetString(wav, 12, 4), Is.EqualTo("fmt "));
		Assert.That(text.GetString(wav, 36, 4), Is.EqualTo("data"));
		Assert.That(BitConverter.ToInt32(wav, 16), Is.EqualTo(16), "PCM fmt chunk size");
		Assert.That(BitConverter.ToInt16(wav, 20), Is.EqualTo(1), "WAVE_FORMAT_PCM");
		Assert.That(BitConverter.ToInt16(wav, 22), Is.EqualTo(1), "mono");
		Assert.That(BitConverter.ToInt32(wav, 24), Is.EqualTo(rate));
		Assert.That(BitConverter.ToInt32(wav, 28), Is.EqualTo(rate * 2), "byte rate");
		Assert.That(BitConverter.ToInt16(wav, 32), Is.EqualTo(2), "block align");
		Assert.That(BitConverter.ToInt16(wav, 34), Is.EqualTo(16), "bits per sample");
		// Declared sizes must agree with the buffer, or a player rejects the file outright.
		var dataBytes = BitConverter.ToInt32(wav, 40);
		Assert.That(dataBytes, Is.EqualTo(rate * 250 / 1000 * 2), "250 ms of 16-bit mono");
		Assert.That(44 + dataBytes, Is.EqualTo(wav.Length));
		Assert.That(BitConverter.ToInt32(wav, 4), Is.EqualTo(wav.Length - 8), "RIFF size");

		// Duration scales the sample count; frequency does not.
		Assert.That(BitConverter.ToInt32(SoundPlayback.BuildToneWav(440, 500, rate), 40), Is.EqualTo(rate * 500 / 1000 * 2));
		Assert.That(BitConverter.ToInt32(SoundPlayback.BuildToneWav(880, 250, rate), 40), Is.EqualTo(dataBytes));

		// Count zero crossings over the steady middle of the tone (skipping the fade ramps) to confirm the
		// samples really carry the requested pitch: 440 Hz over 0.15 s is ~132 crossings.
		static int Crossings(byte[] w, int rate, double skipSeconds, double windowSeconds)
		{
			var first = 44 + ((int)(rate * skipSeconds) * 2);
			var count = (int)(rate * windowSeconds);
			var crossings = 0;
			var previous = BitConverter.ToInt16(w, first);

			for (var i = 1; i < count; i++)
			{
				var sample = BitConverter.ToInt16(w, first + (i * 2));

				if ((previous < 0 && sample >= 0) || (previous >= 0 && sample < 0))
					crossings++;

				previous = sample;
			}

			return crossings;
		}

		Assert.That(Crossings(wav, rate, 0.05, 0.15), Is.EqualTo(132).Within(2), "440 Hz over 0.15 s");
		Assert.That(Crossings(SoundPlayback.BuildToneWav(880, 250, rate), rate, 0.05, 0.15), Is.EqualTo(264).Within(2), "880 Hz over 0.15 s");

		// Fades in and out, so a tone does not click at either end.
		Assert.That(BitConverter.ToInt16(wav, 44), Is.EqualTo(0), "starts silent");
		Assert.That(BitConverter.ToInt16(wav, wav.Length - 2), Is.EqualTo(0), "ends silent");

		// Out-of-range input is clamped to the documented 37..32767 Hz rather than throwing, and a
		// zero/negative duration yields a valid, empty WAV.
		Assert.That(SoundPlayback.MinFrequency, Is.EqualTo(37));
		Assert.That(SoundPlayback.BuildToneWav(440, 0, rate).Length, Is.EqualTo(44));
		Assert.That(SoundPlayback.BuildToneWav(440, -5, rate).Length, Is.EqualTo(44));
		Assert.That(
			Crossings(SoundPlayback.BuildToneWav(1, 250, rate), rate, 0.05, 0.15),
			Is.EqualTo(Crossings(SoundPlayback.BuildToneWav(SoundPlayback.MinFrequency, 250, rate), rate, 0.05, 0.15)),
			"a below-range frequency clamps to the minimum");
	}
}
