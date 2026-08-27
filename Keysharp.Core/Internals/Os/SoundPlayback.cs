namespace Keysharp.Internals.Os
{
	/// <summary>
	/// Audio playback used by SoundBeep and SoundPlay, on every platform.
	/// <para>
	/// Everything is built on one primitive — "play this audio file" — with tones synthesized to an in-memory
	/// WAV first, so <c>SoundBeep</c> honours its Frequency and Duration everywhere rather than degrading to a
	/// fixed system alert. The backend differs per OS: Windows drives MCI (<c>mciSendString</c>), which is what
	/// AHK uses and what gives it formats beyond WAV; Linux and macOS hand the file to an external player,
	/// because the BCL offers neither a tone generator nor a decoder there.
	/// </para>
	/// </summary>
	internal static class SoundPlayback
	{
		// AHK documents 37..32767 Hz for SoundBeep.
		internal const int MinFrequency = 37;
		internal const int MaxFrequency = 32767;

		/// <summary>
		/// Builds a 16-bit mono PCM WAV containing a sine tone.
		/// </summary>
		/// <param name="frequency">Tone frequency in Hz, clamped to the documented range.</param>
		/// <param name="durationMs">Tone length in milliseconds.</param>
		/// <param name="sampleRate">Sample rate; 44100 is universally supported by the players used here.</param>
		internal static byte[] BuildToneWav(int frequency, int durationMs, int sampleRate = 44100)
		{
			// Clamp rather than throw: a script beeping over a computed pitch in a loop should not die on a
			// stray value, and Win32 Beep() silently accepts the whole range too.
			frequency = Math.Clamp(frequency, MinFrequency, MaxFrequency);
			durationMs = Math.Max(0, durationMs);
			const int channels = 1;
			const int bitsPerSample = 16;
			var blockAlign = channels * bitsPerSample / 8;
			var frames = (int)Math.Min((long)sampleRate * durationMs / 1000L, 10 * 60 * (long)sampleRate);
			var dataBytes = frames * blockAlign;
			using var ms = new MemoryStream(44 + dataBytes);
			using var w = new BinaryWriter(ms, System.Text.Encoding.ASCII, true);
			w.Write("RIFF"u8);
			w.Write(36 + dataBytes);
			w.Write("WAVE"u8);
			w.Write("fmt "u8);
			w.Write(16);                                  // PCM chunk size
			w.Write((short)1);                            // WAVE_FORMAT_PCM
			w.Write((short)channels);
			w.Write(sampleRate);
			w.Write(sampleRate * blockAlign);             // byte rate
			w.Write((short)blockAlign);
			w.Write((short)bitsPerSample);
			w.Write("data"u8);
			w.Write(dataBytes);
			// A tone that starts and stops at full amplitude clicks audibly, so ramp ~5 ms at each end.
			var fade = Math.Min(sampleRate / 200, frames / 2);
			var step = 2.0 * Math.PI * frequency / sampleRate;

			for (var i = 0; i < frames; i++)
			{
				var sample = Math.Sin(step * i);

				if (fade > 0)
				{
					if (i < fade)
						sample *= (double)i / fade;
					else if (i >= frames - fade)
						sample *= (double)(frames - 1 - i) / fade;
				}

				// 0.35 keeps a default beep comfortable rather than startling.
				w.Write((short)(sample * 0.35 * short.MaxValue));
			}

			w.Flush();
			return ms.ToArray();
		}

#if WINDOWS
		// One MCI item at a time, matching AHK's single AHK_PlayMe alias: opening a new file closes the
		// previous one, which is what makes a new SoundPlay stop whatever was playing.
		private const string MciAlias = "KeysharpPlayMe";
		// MCI paths are limited to ~127 chars, so AHK's buffer is MAX_PATH*2; keep the same room.
		private const int MciBuffer = 520;
		private static readonly Lock gate = new();
		private static bool soundWasPlayed;
		private static Script currentOwner;//The Script whose SoundPlay opened the current MCI item; matches the non-Windows branch below.
		private static long playbackSequence;

		/// <summary>"playing"/"stopped"/... for the open item, or "" when nothing is open.</summary>
		private static string MciMode()
		{
			var sb = new StringBuilder(MciBuffer);
			_ = WindowsAPI.mciSendString($"status {MciAlias} mode", sb, sb.Capacity, 0);
			return sb.ToString();
		}

		private static void MciClose() => _ = WindowsAPI.mciSendString($"close {MciAlias}", null, 0, 0);

		/// <summary>
		/// Closes any open MCI item. Called when a new file starts, and at script exit — an item left open can
		/// hang the process on exit on some systems, which is why AHK's destructor does the same.
		/// </summary>
		internal static void StopCurrent() => StopCurrent(null);

		internal static void StopCurrent(Script owner)
		{
			lock (gate)
				StopCurrentLocked(owner);
		}

		private static void StopCurrentLocked(Script owner)
		{
			if (!soundWasPlayed || (owner != null && !ReferenceEquals(currentOwner, owner)))
				return;

			if (MciMode().Length > 0)
				MciClose();

			soundWasPlayed = false;
			currentOwner = null;
			playbackSequence++;
		}

		/// <summary>
		/// Plays an audio file through MCI, so anything with an installed codec works (.wav, .mp3, .avi, ...).
		/// </summary>
		internal static bool TryPlay(Script owner, string path, bool wait, out string error)
		{
			error = null;
			long sequence;
			// Close first: that is what stops the previous file, and it is why SoundPlay on a nonexistent file
			// is the documented way to stop playback — the stop happens before the open can fail.
			StopCurrent();

			// MCI parses its command string, so a quote in the path would break out of the quoted filename.
			if (path.Contains('"'))
			{
				error = $"Cannot play sound file {path} because its path contains a quote character.";
				return false;
			}

			lock (gate)
			{
				// Another script can start playback between the documented early stop above and this open.
				StopCurrentLocked(null);

				if (WindowsAPI.mciSendString($"open \"{path}\" alias {MciAlias}", null, 0, 0) != 0)
				{
					error = $"Failed to open sound file {path}.";
					return false;
				}

				if (WindowsAPI.mciSendString($"play {MciAlias}", null, 0, 0) != 0)
				{
					MciClose();
					error = $"Failed to play sound file {path}.";
					return false;
				}

				soundWasPlayed = true;
				currentOwner = owner;
				sequence = ++playbackSequence;
			}

			if (!wait)
				return true;

			// Poll rather than "play ... wait": the blocking form freezes the message queue, and AHK documents
			// that timers/hotkeys still run while SoundPlay waits. Flow.Sleep pumps, so they do.
			for (;;)
			{
				lock (gate)
				{
					if (sequence != playbackSequence)
						break;

					var mode = MciMode();

					if (mode.Length == 0)   // item vanished; nothing left to wait for
					{
						soundWasPlayed = false;
						currentOwner = null;
						playbackSequence++;
						break;
					}

					if (mode == "stopped")
					{
						MciClose();
						soundWasPlayed = false;
						currentOwner = null;
						playbackSequence++;
						break;
					}
				}

				Keysharp.Internals.Flow.Sleep(20);
			}

			return true;
		}

		/// <summary>
		/// Plays one of SoundPlay's "*n" standard sounds. The number is an MB_ICON* value and -1 (0xFFFFFFFF)
		/// is the simple beep, so it maps straight onto MessageBeep the way AHK does — including values with
		/// no named constant, which a switch over the documented four would silently drop.
		/// </summary>
		internal static bool TryPlaySystemSound(int which)
			=> WindowsAPI.MessageBeep(unchecked((uint)which));
#else
		private static readonly Lock gate = new();
		private static readonly ConcurrentDictionary<string, string> resolvedPlayers = new();
		private static Process current;
		private static Script currentOwner;

		// Formats libsndfile (and therefore paplay) decodes without a full media framework.
		private static readonly string[] sndFileExtensions =
			[".wav", ".wave", ".flac", ".ogg", ".oga", ".opus", ".aiff", ".aif", ".aifc", ".au", ".snd", ".caf", ".w64"];
		// aplay talks straight to ALSA and understands only uncompressed containers.
		private static readonly string[] wavExtensions = [".wav", ".wave", ".au", ".snd", ".raw"];

		/// <summary>
		/// Candidate players in preference order. A null <c>Extensions</c> means the player brings its own
		/// decoders and can be handed anything; otherwise it is only offered files it can actually decode, so a
		/// box with just paplay does not silently fail on an .mp3 that mpv/ffplay could have handled.
		/// </summary>
		private static readonly (string Exe, string[] Args, string[] Extensions)[] players =
#if OSX
		[
			// CoreAudio via afplay: wav, aiff, caf, mp3, m4a/aac, ... Always present on macOS.
			("afplay", [], null),
		];
#else
		[
			("paplay",       [],                                                    sndFileExtensions),
			("aplay",        ["-q"],                                                wavExtensions),
			("ffplay",       ["-nodisp", "-autoexit", "-loglevel", "quiet"],        null),
			("mpv",          ["--no-video", "--really-quiet"],                      null),
			("gst-play-1.0", ["--quiet"],                                           null),
			("mpg123",       ["-q"],                                                null),
		];
#endif

		static SoundPlayback() => AppDomain.CurrentDomain.ProcessExit += (_, _) => StopCurrent();

		/// <summary>
		/// Stops whatever this script is currently playing. AHK stops the previous file when a new one starts,
		/// when SoundPlay is given a nonexistent file, and when the script exits.
		/// </summary>
		internal static void StopCurrent() => StopCurrent(null);

		internal static void StopCurrent(Script owner)
		{
			Process previous;

			lock (gate)
			{
				if (owner != null && !ReferenceEquals(currentOwner, owner))
					return;

				previous = current;
				current = null;
				currentOwner = null;
			}

			if (previous == null)
				return;

			try
			{
				if (!previous.HasExited)
					previous.Kill(entireProcessTree: true);
			}
			catch
			{
			}

			try
			{
				previous.Dispose();
			}
			catch
			{
			}
		}

		/// <summary>
		/// Synthesizes a tone and plays it, blocking until it finishes (SoundBeep is synchronous in AHK).
		/// </summary>
		internal static bool TryPlayTone(Script owner, int frequency, int durationMs, out string error)
		{
			// A tone is always a WAV, so the temp file keeps the single "play a file" primitive rather than
			// adding a second, player-specific raw-PCM-over-stdin path.
			string temp = null;

			try
			{
				temp = Path.Combine(Path.GetTempPath(), $"keysharp-tone-{Environment.ProcessId}-{Guid.NewGuid():N}.wav");
				File.WriteAllBytes(temp, BuildToneWav(frequency, durationMs));
				return TryPlay(owner, temp, wait: true, out error);
			}
			catch (Exception ex)
			{
				error = ex.Message;
				return false;
			}
			finally
			{
				// Safe even for the async case: TryPlay only returns without waiting when wait is false.
				if (temp != null)
				{
					try
					{
						File.Delete(temp);
					}
					catch
					{
					}
				}
			}
		}

		/// <summary>
		/// Plays an audio file through the best available external player.
		/// </summary>
		/// <param name="path">Path to the file; relative paths resolve against A_WorkingDir.</param>
		/// <param name="wait">Whether to block until playback finishes.</param>
		/// <param name="error">Failure description when this returns false.</param>
		internal static bool TryPlay(Script owner, string path, bool wait, out string error)
		{
			error = null;
			string full;

			try
			{
				full = Path.GetFullPath(path);   // A_WorkingDir is the process CWD
			}
			catch (Exception ex)
			{
				error = ex.Message;
				return false;
			}

			// Starting a new file stops the old one, and SoundPlay on a nonexistent file is the documented way
			// to stop playback — so the stop happens before the existence check, not after.
			StopCurrent();

			if (!File.Exists(full))
			{
				error = $"Cannot play sound file {path} because it does not exist.";
				return false;
			}

			if (!TryResolvePlayer(full, out var exe, out var args))
			{
				error = $"No supported audio player was found for {path}. "
						+ "Install one of: " + string.Join(", ", players.Select(p => p.Exe)) + ".";
				return false;
			}

			try
			{
				var psi = new ProcessStartInfo
				{
					FileName = exe,
					UseShellExecute = false,
					CreateNoWindow = true,
					RedirectStandardOutput = true,
					RedirectStandardError = true,
				};

				foreach (var arg in args)
					psi.ArgumentList.Add(arg);

				// ArgumentList (not a shell string): a path with spaces, quotes or $ is passed verbatim and
				// cannot be re-parsed as shell syntax.
				psi.ArgumentList.Add(full);
				var process = Process.Start(psi);

				if (process == null)
				{
					error = $"Failed to start {exe} to play {path}.";
					return false;
				}

				if (!wait)
				{
					lock (gate)
					{
						current = process;
						currentOwner = owner;
					}

					return true;
				}

				// Drain both pipes so a chatty player cannot fill its buffer and deadlock the wait.
				_ = process.StandardOutput.ReadToEndAsync();
				var stderr = process.StandardError.ReadToEndAsync();
				process.WaitForExit();

				if (process.ExitCode != 0)
				{
					var detail = stderr.IsCompletedSuccessfully ? stderr.Result?.Trim() : null;
					error = $"Playing {path} with {Path.GetFileName(exe)} failed"
							+ (detail.IsNullOrEmpty() ? "." : $": {detail}");
					process.Dispose();
					return false;
				}

				process.Dispose();
				return true;
			}
			catch (Exception ex)
			{
				error = ex.Message;
				return false;
			}
		}

		private static bool TryResolvePlayer(string file, out string exe, out string[] args)
		{
			var ext = Path.GetExtension(file);

			foreach (var (candidate, candidateArgs, extensions) in players)
			{
				if (extensions != null && !extensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
					continue;

				if (ResolveOnPath(candidate) is string resolved)
				{
					exe = resolved;
					args = candidateArgs;
					return true;
				}
			}

			exe = null;
			args = null;
			return false;
		}

		private static string ResolveOnPath(string exe)
			=> resolvedPlayers.GetOrAdd(exe, static name =>
		{
			foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
			{
				try
				{
					var full = Path.Combine(dir.Trim(), name);

					if (File.Exists(full))
						return full;
				}
				catch
				{
					// A malformed PATH entry must not stop the scan.
				}
			}

			return "";   // ConcurrentDictionary cannot cache null
		}) is { Length: > 0 } hit ? hit : null;

		/// <summary>
		/// The platform's file for one of SoundPlay's "*n" standard sounds, or null when the desktop does not
		/// ship one (in which case the caller falls back to a synthesized tone).
		/// </summary>
		internal static string SystemSoundFile(int which)
		{
			string[] candidates =
#if OSX
				which switch
				{
					16 => ["Basso"],      // hand / stop / error
					32 => ["Funk"],       // question
					48 => ["Sosumi"],     // exclamation
					64 => ["Glass"],      // asterisk / info
					_ => ["Ping"],        // *-1 and anything else: simple beep
				};

			foreach (var name in candidates)
			{
				var path = $"/System/Library/Sounds/{name}.aiff";

				if (File.Exists(path))
					return path;
			}
#else
				// freedesktop sound-theme names, shipped by sound-theme-freedesktop on most distributions.
				which switch
				{
					16 => ["dialog-error"],
					32 => ["dialog-question"],
					48 => ["dialog-warning"],
					64 => ["dialog-information"],
					_ => ["bell", "message"],
				};
			string[] roots = ["/usr/share/sounds/freedesktop/stereo", "/usr/local/share/sounds/freedesktop/stereo"];

			foreach (var name in candidates)
				foreach (var root in roots)
					foreach (var ext in new[] { ".oga", ".ogg", ".wav" })
					{
						var path = Path.Combine(root, name + ext);

						if (File.Exists(path))
							return path;
					}

#endif
			return null;
		}
#endif
	}
}
