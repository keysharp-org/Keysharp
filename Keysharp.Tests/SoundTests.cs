using Assert = NUnit.Framework.Legacy.ClassicAssert;
using Keysharp.Internals.Os;

namespace Keysharp.Tests
{
	public partial class SoundTests : TestRunner
	{
		[Test, Category("Sound")]
		public void SoundBeep()
		{
			_ = Sound.SoundBeep();
			_ = Sound.SoundBeep(700, 500);
			_ = Sound.SoundBeep(800, 500);
			_ = Sound.SoundBeep(900, 500);
			_ = Sound.SoundBeep(1000, 500);
			Assert.IsTrue(TestScript("sound-soundbeep", true));
		}

		// The synthesized tone is what makes SoundBeep honour Frequency/Duration on Linux and macOS, so the
		// WAV it produces is verified here rather than trusted — this runs on every platform and needs no
		// audio device. (Playback itself still needs a real desktop; see the manual-verification notes.)
		[Test, Category("Misc"), Category("Internal")]
		public void ToneWav()
		{
			const int rate = 44100;
			var wav = SoundPlayback.BuildToneWav(440, 250, rate);
			var text = System.Text.Encoding.ASCII;
			Assert.AreEqual("RIFF", text.GetString(wav, 0, 4));
			Assert.AreEqual("WAVE", text.GetString(wav, 8, 4));
			Assert.AreEqual("fmt ", text.GetString(wav, 12, 4));
			Assert.AreEqual("data", text.GetString(wav, 36, 4));
			Assert.AreEqual(16, BitConverter.ToInt32(wav, 16), "PCM fmt chunk size");
			Assert.AreEqual(1, BitConverter.ToInt16(wav, 20), "WAVE_FORMAT_PCM");
			Assert.AreEqual(1, BitConverter.ToInt16(wav, 22), "mono");
			Assert.AreEqual(rate, BitConverter.ToInt32(wav, 24));
			Assert.AreEqual(rate * 2, BitConverter.ToInt32(wav, 28), "byte rate");
			Assert.AreEqual(2, BitConverter.ToInt16(wav, 32), "block align");
			Assert.AreEqual(16, BitConverter.ToInt16(wav, 34), "bits per sample");
			// Declared sizes must agree with the buffer, or a player rejects the file outright.
			var dataBytes = BitConverter.ToInt32(wav, 40);
			Assert.AreEqual(rate * 250 / 1000 * 2, dataBytes, "250 ms of 16-bit mono");
			Assert.AreEqual(wav.Length, 44 + dataBytes);
			Assert.AreEqual(wav.Length - 8, BitConverter.ToInt32(wav, 4), "RIFF size");

			// Duration scales the sample count; frequency does not.
			Assert.AreEqual(rate * 500 / 1000 * 2, BitConverter.ToInt32(SoundPlayback.BuildToneWav(440, 500, rate), 40));
			Assert.AreEqual(dataBytes, BitConverter.ToInt32(SoundPlayback.BuildToneWav(880, 250, rate), 40));

			// Count zero crossings over the steady middle of the tone (skipping the fade ramps) to confirm the
			// samples really carry the requested pitch: 440 Hz over 0.15 s is ~132 crossings.
			static int Crossings(byte[] w, int rate, double skipSeconds, double windowSeconds)
			{
				var first = 44 + (int)(rate * skipSeconds) * 2;
				var count = (int)(rate * windowSeconds);
				var crossings = 0;
				var previous = BitConverter.ToInt16(w, first);

				for (var i = 1; i < count; i++)
				{
					var sample = BitConverter.ToInt16(w, first + i * 2);

					if ((previous < 0 && sample >= 0) || (previous >= 0 && sample < 0))
						crossings++;

					previous = sample;
				}

				return crossings;
			}

			Assert.AreEqual(132, Crossings(wav, rate, 0.05, 0.15), 2, "440 Hz over 0.15 s");
			Assert.AreEqual(264, Crossings(SoundPlayback.BuildToneWav(880, 250, rate), rate, 0.05, 0.15), 2, "880 Hz over 0.15 s");

			// Fades in and out, so a tone does not click at either end.
			Assert.AreEqual(0, BitConverter.ToInt16(wav, 44), "starts silent");
			Assert.AreEqual(0, BitConverter.ToInt16(wav, wav.Length - 2), "ends silent");

			// Out-of-range input is clamped to the documented 37..32767 Hz rather than throwing, and a
			// zero/negative duration yields a valid, empty WAV.
			Assert.AreEqual(SoundPlayback.MinFrequency, 37);
			Assert.AreEqual(44, SoundPlayback.BuildToneWav(440, 0, rate).Length);
			Assert.AreEqual(44, SoundPlayback.BuildToneWav(440, -5, rate).Length);
			Assert.AreEqual(
				Crossings(SoundPlayback.BuildToneWav(SoundPlayback.MinFrequency, 250, rate), rate, 0.05, 0.15),
				Crossings(SoundPlayback.BuildToneWav(1, 250, rate), rate, 0.05, 0.15),
				"a below-range frequency clamps to the minimum");
		}
	}
}
