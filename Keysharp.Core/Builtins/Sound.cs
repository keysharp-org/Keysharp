namespace Keysharp.Builtins
{
	/// <summary>
	/// Public interface for sound-related functions.
	/// </summary>
	public static class Sound
	{
#if LINUX
		private static Dictionary<int, string> GetDevices(bool sinks)
		{
			var devices = new Dictionary<int, string>();
			var arg = sinks ? "sinks" : "sources";
			if ($"pactl list {arg} short".Bash(out var str) != 0)
				return devices;

			foreach (var line in str.SplitLines())
			{
				var splits = line.Split(SpaceTab, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

				if (splits.Length > 1)
				{
					if (int.TryParse(splits[0], out var index))
						_ = devices.GetOrAdd(index, splits[1]);
				}
			}

			return devices;
		}
#endif

		/// <summary>
		/// Emits a tone from the PC speaker.
		/// </summary>
		/// <param name="frequency">If omitted, it defaults to 523. Otherwise, specify the frequency of the sound, a number between 37 and 32767.</param>
		/// <param name="duration">If omitted, it defaults to 150. Otherwise, specify the duration of the sound, in milliseconds.</param>
		public static object SoundBeep(object frequency = null, object duration = null)
		{
			var freq = frequency.Ai(523);
			var time = duration.Ai(150);
#if WINDOWS
			// Console.Beep is Win32 Beep(), which is exactly what AHK calls.
			Console.Beep(Math.Clamp(freq, SoundPlayback.MinFrequency, SoundPlayback.MaxFrequency), Math.Max(0, time));
#else

			// Linux and macOS have no tone generator, so synthesize the sine and play it. This is what makes
			// Frequency and Duration mean something on macOS (which used to emit a fixed system alert) and
			// removes the Linux dependency on alsa-utils' speaker-test.
			if (!SoundPlayback.TryPlayTone(Script.TheScript, freq, time, out var error))
				return Errors.ErrorOccurred($"SoundBeep failed: {error}");

#endif
			return DefaultObject;
		}

#if WINDOWS

		/// <summary>
		/// Retrieves a native COM interface of a sound device or component.
		/// </summary>
		/// <param name="iid">An interface identifier (GUID) in the form "{xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx}".</param>
		/// <param name="component">If blank or omitted, an interface implemented by the device itself will be retrieved. Otherwise, specify the component's display name and/or index, e.g. 1, "Line in" or "Line in:2".</param>
		/// <param name="device">If blank or omitted, it defaults to the system's default device for playback<br/>
		/// (which is not necessarily device 1). Otherwise, specify the device's display name and/or index,<br/>
		/// e.g. 1, "Speakers", "Speakers:2" or "Speakers (Example HD Audio)".
		/// </param>
		/// <returns>The COM interface for the specified sound interface.</returns>
		public static object SoundGetInterface(object iid, object component = null, object device = null) => DoSound(SoundCommands.SoundGetInterface, iid, component, device);

#endif

		/// <summary>
		/// Retrieves a mute setting of a sound device.
		/// </summary>
		/// <param name="component">If blank or omitted, it defaults to the master mute setting. Otherwise, specify the component's display name and/or index, e.g. 1, "Line in" or "Line in:2".</param>
		/// <param name="device">If blank or omitted, it defaults to the system's default device for playback<br/>
		/// (which is not necessarily device 1). Otherwise, specify the device's display name and/or index,<br/>
		/// e.g. 1, "Speakers", "Speakers:2" or "Speakers (Example HD Audio)".
		/// </param>
		/// <returns>0 for unmuted, else 1.</returns>
		public static object SoundGetMute(object component = null, object device = null) => DoSound(SoundCommands.SoundGetMute, component, device);

		/// <summary>
		/// Retrieves the name of a sound device or component.
		/// </summary>
		/// <param name="component">If blank or omitted, it defaults to the master mute setting. Otherwise, specify the component's display name and/or index, e.g. 1, "Line in" or "Line in:2".</param>
		/// <param name="device">If blank or omitted, it defaults to the system's default device for playback<br/>
		/// (which is not necessarily device 1). Otherwise, specify the device's display name and/or index,<br/>
		/// e.g. 1, "Speakers", "Speakers:2" or "Speakers (Example HD Audio)".
		/// </param>
		/// <returns>The name of the device or component, which can be empty.</returns>
		public static object SoundGetName(object component = null, object device = null) => DoSound(SoundCommands.SoundGetName, component, device);

		/// <summary>
		/// Retrieves a volume setting of a sound device.
		/// </summary>
		/// <param name="component">If blank or omitted, it defaults to the master mute setting. Otherwise, specify the component's display name and/or index, e.g. 1, "Line in" or "Line in:2".</param>
		/// <param name="device">If blank or omitted, it defaults to the system's default device for playback<br/>
		/// (which is not necessarily device 1). Otherwise, specify the device's display name and/or index,<br/>
		/// e.g. 1, "Speakers", "Speakers:2" or "Speakers (Example HD Audio)".
		/// </param>
		/// <returns>A floating point number between 0.0 and 100.0.</returns>
		public static object SoundGetVolume(object component = null, object device = null) => DoSound(SoundCommands.SoundGetVolume, component, device);

		/// <summary>
		/// Plays a sound, video, or other supported file type.
		/// </summary>
		/// <param name="filename">
		/// The name of the file to be played, which is assumed to be in <see cref="A_WorkingDir"/> if an absolute path isn't specified.<br/>
#if WINDOWS
		/// To produce standard system sounds, specify an asterisk followed by a number as shown below (note that the Wait parameter has no effect in this mode):<br/>
		/// *-1: simple beep<br/>
		/// *16: hand (stop/error)<br/>
		/// *32: question<br/>
		/// *48: exclamation<br/>
		/// *64: asterisk (info)<br/>
#endif
		/// </param>
		/// <param name="wait">If blank or omitted, it defaults to 0 (false). Otherwise, specify one of the following values:<br/>
		///     0 (false): The current thread will move on to the next statement(s) while the file is playing.<br/>
		///     1 (true) or Wait: The current thread waits until the file is finished playing before continuing.<br/>
		///     Even while waiting, new threads can be launched via hotkey, custom menu item, or timer.<br/>
		///     Known limitation: If the Wait parameter is not used, the system might consider the playing file to<br/>
		///     be "in use" until the script closes or until another file is played(even a nonexistent file).<br/>
		/// </param>
		/// <exception cref="Error">An <see cref="Error"/> exception is thrown on failure.</exception>
		public static object SoundPlay(object filename, object wait = null)
		{
			var script = Script.TheScript;
			var file = filename.As();
			var w = wait.As();

			// "*n" selects a standard system sound. Wait has no effect in this mode (as documented).
			if (file.Length > 1 && file[0] == '*')
			{
				if (!int.TryParse(file.AsSpan(1), out var n))
					return Errors.ValueErrorOccurred($"Invalid SoundPlay system sound: {file}.");

				return PlaySystemSound(script, n);
			}

			try
			{
				var doWait = w == "1" || string.Compare(w, "WAIT", true) == 0;

				if (!SoundPlayback.TryPlay(script, file, doWait, out var error))
					return Errors.ErrorOccurred(error);

				return DefaultObject;
			}
			catch (Exception ex)
			{
				return Errors.ErrorOccurred(ex.Message);
			}
		}

		/// <summary>
		/// Plays one of SoundPlay's "*n" standard system sounds.
		/// </summary>
		/// <param name="which">-1 for a simple beep, or 16/32/48/64 for hand/question/exclamation/asterisk.</param>
		private static object PlaySystemSound(Script script, int which)
		{
#if WINDOWS
			_ = SoundPlayback.TryPlaySystemSound(which);
#else

			// Prefer the desktop's own alert sound so "*n" matches what the rest of the system does; fall back
			// to a synthesized beep so the call is never silently inaudible where no sound theme is installed.
			if (SoundPlayback.SystemSoundFile(which) is string path && SoundPlayback.TryPlay(script, path, wait: false, out _))
				return DefaultObject;

			_ = SoundPlayback.TryPlayTone(script, 523, 150, out _);
#endif
			return DefaultObject;
		}

		/// <summary>
		/// Changes a mute setting of a sound device.
		/// </summary>
		/// <param name="newSetting">One of the following values:<br/>
		///     1 or True: turns on the setting.<br/>
		///     0 or False: turns off the setting.<br/>
		///    -1: toggles the setting(sets it to the opposite of its current state).
		/// </param>
		/// <param name="component">If blank or omitted, it defaults to the master mute setting. Otherwise, specify the component's display name and/or index, e.g. 1, "Line in" or "Line in:2".</param>
		/// <param name="device">If blank or omitted, it defaults to the system's default device for playback<br/>
		/// (which is not necessarily device 1). Otherwise, specify the device's display name and/or index,<br/>
		/// e.g. 1, "Speakers", "Speakers:2" or "Speakers (Example HD Audio)".
		/// </param>
		public static object SoundSetMute(object newSetting, object component = null, object device = null)
		{
			_ = DoSound(SoundCommands.SoundSetMute, newSetting, component, device);
			return DefaultObject;
		}


		/// <summary>
		/// Changes a volume setting of a sound device.
		/// </summary>
		/// <param name="newSetting">A string containing a percentage number between -100 and 100 inclusive.<br/>
		/// If the number begins with a plus or minus sign, the current setting will be adjusted up or down by the indicated amount.<br/>
		/// Otherwise, the setting will be set explicitly to the level indicated by newSetting.<br/>
		/// If the percentage number begins with a minus sign or is unsigned, it does not need to be enclosed in quotation marks.
		/// </param>
		/// <param name="component">If blank or omitted, it defaults to the master mute setting. Otherwise, specify the component's display name and/or index, e.g. 1, "Line in" or "Line in:2".</param>
		/// <param name="device">If blank or omitted, it defaults to the system's default device for playback<br/>
		/// (which is not necessarily device 1). Otherwise, specify the device's display name and/or index,<br/>
		/// e.g. 1, "Speakers", "Speakers:2" or "Speakers (Example HD Audio)".
		/// </param>
		public static object SoundSetVolume(object newSetting, object component = null, object device = null)
		{
			_ = DoSound(SoundCommands.SoundSetVolume, newSetting, component, device);
			return DefaultObject;
		}

#if LINUX
		private static object DoSound(SoundCommands soundCmd, object obj0, object obj1 = null, object obj2 = null)
		{
			var soundSet = false;
			var device = obj1;
			SoundControlType type;
			var sink = true;

			if (soundCmd >= SoundCommands.SoundSetVolume)
			{
				soundSet = true;
				type = (SoundControlType)((int)soundCmd - (int)SoundCommands.SoundSetVolume);
				sink = obj1.Ab(true);
				device = obj2;
			}
			else
			{
				sink = obj0.Ab(true);
				type = (SoundControlType)(int)soundCmd;
			}

			var settingScalar = 0.0;

			if (soundSet)
				settingScalar = Math.Clamp(obj0.Ad() * 0.01 * 65536.0, -65536.0, 65536.0);//pactl uses a range of 0-65536.

			var valStr = obj0 == null ? "" : obj0.ToString();
			var adjust = valStr.Length > 0 && (valStr[0] == '-' || valStr[0] == '+');
			var found = false;
			var sinkStr = sink ? "Sink" : "Source";
			var devices = GetDevices(sink);
			var devStr = "";

			if (device == null)
			{
				devStr = sink ? "@DEFAULT_SINK@" : "@DEFAULT_SOURCE@";
				found = true;
			}
			else
			{
				devStr = device.ToString();

				if (int.TryParse(devStr, out var deviceIndex) && devices.TryGetValue(deviceIndex, out var _))
				{
					found = true;
				}
				else
				{
					foreach (var devKv in devices)
					{
						if (devKv.Value.StartsWith(devStr, StringComparison.OrdinalIgnoreCase))
						{
							found = true;
							break;
						}
					}
				}

				if (!found)
					return Errors.TargetErrorOccurred($"{sinkStr} device {device} not found.");
			}

			sinkStr = sinkStr.ToLower();

			switch (soundCmd)
			{
				case SoundCommands.SoundGetVolume:
				{
					if ($"pactl get-{sinkStr}-volume {devStr}".Bash(out var ret) != 0)
						return Errors.OSErrorOccurred("", $"Failed to query volume for {devStr}.");
					var lines = ret.SplitLines().ToList();

					if (lines.Count > 1)
					{
						var lines0 = lines[0];
						var firstPercent = lines0.IndexOf('%');
						var lastPercent = lines0.LastIndexOf('%');
						double prc1 = 1.0, prc2 = 1.0;

						if (firstPercent != -1)
						{
							var val1Index = lines0.AsSpan(0, firstPercent).LastIndexOf(' ');

							if (val1Index != -1)
							{
								var val1 = lines0.AsSpan(val1Index + 1, (firstPercent - val1Index) - 1);

								if (!double.TryParse(val1, out prc1))
									return Errors.OSErrorOccurred("", $"Could not parse first volume value of {val1}.");
							}
						}

						if (lastPercent != -1 && lastPercent > firstPercent)
						{
							var val2Index = lines0.AsSpan(0, lastPercent).LastIndexOf(' ');

							if (val2Index != -1)
							{
								var val2 = lines0.AsSpan(val2Index + 1, (lastPercent - val2Index) - 1);

								if (!double.TryParse(val2, out prc2))
									return Errors.OSErrorOccurred("", $"Could not parse second volume value of {val2}.");
							}
						}

						return (prc1 + prc2) / 2.0;
					}
				}
				break;

				case SoundCommands.SoundGetMute:
				{
					if ($"pactl get-{sinkStr}-mute {devStr}".Bash(out var ret) != 0)
						return Errors.OSErrorOccurred("", $"Failed to query mute state for {devStr}.");
					return ret.EndsWith("yes", StringComparison.OrdinalIgnoreCase) ? 1L : 0L;
				}

				case SoundCommands.SoundGetName:
				{
					if (device == null)
					{
						return "pactl get-default-sink".Bash(out var defSink) == 0
							? defSink
							: Errors.OSErrorOccurred("", "Failed to query default sink.");
					}
					else if (int.TryParse(devStr, out var deviceIndex))
					{
						if (!devices.TryGetValue(deviceIndex, out var deviceName))
							return Errors.TargetErrorOccurred($"{sinkStr} device {device} not found.");
						else
							return deviceName;
					}
					else
						return Errors.TargetErrorOccurred($"{devStr} was not a valid integer.");
				}

				case SoundCommands.SoundSetVolume:
				{
					if (adjust)
					{
						var currentVolume = SoundGetVolume(obj0, obj1).Ad() * 0.01 * 65536.0;
						settingScalar = Math.Clamp(currentVolume + settingScalar, 0.0, 65536.0);
					}

					if ($"pactl set-{sinkStr}-volume {devStr} {(int)settingScalar}".Bash() != 0)
						return Errors.OSErrorOccurred("", $"Failed to set volume for {devStr}.");
				}
				break;

				case SoundCommands.SoundSetMute:
				{
					var act = Conversions.ConvertOnOffToggle(obj0);
					if ($"pactl set-{sinkStr}-mute {devStr} {(act == ToggleValueType.On ? "1" : act == ToggleValueType.Toggle ? "-1" : "0")}".Bash() != 0)
						return Errors.OSErrorOccurred("", $"Failed to set mute state for {devStr}.");
				}
				break;

				default:
					break;
			}

			return DefaultObject;
		}

#elif WINDOWS

		/// <summary>
		/// Internal helper to help with various sound processing commands.
		/// </summary>
		/// <param name="soundCmd">The sound command to perform.</param>
		/// <param name="obj0">The sound component to operate on, or the value to use.</param>
		/// <param name="obj1">The sound device to operate on, or the sound component.</param>
		/// <param name="obj2">The sound device to operate on.</param>
		/// <returns>Various values depending on the sound command being processed.</returns>
		/// <exception cref="TargetError">A <see cref="TargetError"/> exception is thrown if the component/device cannot not found.</exception>
		/// <exception cref="Error">An <see cref="Error"/> exception is thrown if the channels, levels or range cannot not found.</exception>
		private static object DoSound(SoundCommands soundCmd, object obj0, object obj1 = null, object obj2 = null)
		{
			var soundSet = false;
			var search = new SoundComponentSearch();
			var comp = obj0;
			var dev = obj1;

			if (soundCmd >= SoundCommands.SoundSetVolume)
			{
				soundSet = true;
				search.targetControl = (SoundControlType)((int)soundCmd - (int)SoundCommands.SoundSetVolume);
				comp = obj1;
				dev = obj2;
			}
			else
			{
				search.targetControl = (SoundControlType)(int)soundCmd;
			}

			switch (search.targetControl)
			{
				case SoundControlType.Volume:
					search.targetIid = new Guid("7FB7B48F-531D-44A2-BCB3-5AD5A134B3DC");
					break;

				case SoundControlType.Mute:
					search.targetIid = new Guid("DF45AEEA-B74A-4B6B-AFAD-2366B6AA012E");
					break;

				case SoundControlType.IID:
					search.targetIid = new Guid(obj0.As());
					comp = obj1;
					dev = obj2;
					break;
			}

			var settingScalar = 0.0f;

			if (soundSet)
				settingScalar = Math.Clamp((float)(obj0.Ad() * 0.01), -1.0f, 1.0f);

			var resultFloat = 0.0f;
			var resultBool = false;
			var valStr = obj0 == null ? "" : obj0.ToString();
			var adjust = valStr.Length > 0 && (valStr[0] == '-' || valStr[0] == '+');
			var mmDev = GetDevice(dev);

			if (mmDev == null)
				return Errors.TargetErrorOccurred($"Component {comp}, device {dev} not found.");

			if (comp == null || comp.ToString().Length == 0)//Component is Master (omitted).
			{
				if (search.targetControl == SoundControlType.IID)
				{
					//Query the device itself first and only then Activate(), matching AHK: an interface the
					//device implements directly (IMMEndpoint, IPropertyStore, ...) is not reachable via Activate.
					var devPtr = Marshal.GetIUnknownForObject(mmDev.deviceInterface);
					nint resultPtr;

					try
					{
						if (Marshal.QueryInterface(devPtr, in search.targetIid, out resultPtr) < 0)
						{
							resultPtr = 0;

							//An IID the device does not support is an expected outcome here, not an error.
							if (mmDev.deviceInterface.Activate(ref search.targetIid, ClsCtx.ALL, 0, out var activated) >= 0 && activated != null)
							{
								//Need the specific interface pointer, else ComCall() will fail when using IAudioMeterInformation.
								var iptr = Marshal.GetIUnknownForObject(activated);

								if (Marshal.QueryInterface(iptr, in search.targetIid, out var ptr) >= 0)
									resultPtr = ptr;

								_ = Marshal.Release(iptr);
							}
						}
					}
					finally
					{
						_ = Marshal.Release(devPtr);
					}

					//For consistency with ComObjQuery, the result is returned even on failure.
					return resultPtr.ToInt64();
				}
				else if (search.targetControl == SoundControlType.Name)
				{
					return mmDev.FriendlyName;
				}
				else
				{
					var aev = mmDev.AudioEndpointVolume;

					if (search.targetControl == SoundControlType.Volume)
					{
						if (!soundSet || adjust)
						{
							resultFloat = aev.MasterVolumeLevelScalar;
						}

						if (soundSet)
						{
							if (adjust)
								settingScalar = Math.Clamp(settingScalar + resultFloat, 0.0f, 1.0f);

							aev.MasterVolumeLevelScalar = settingScalar;
						}
						else
						{
							resultFloat *= 100;
							return (double)resultFloat;
						}
					}
					else//Mute.
					{
						if (!soundSet || adjust)
							resultBool = aev.Mute;

						if (soundSet)
							aev.Mute = adjust ? !resultBool : settingScalar > 0;
						else
							return resultBool ? 1L : 0L;
					}
				}
			}
			else
			{
				//Mirrors AHK's SoundConvertComponent(): a component which parses as an integer is an instance
				//index with no name filter, otherwise it is Name[:Instance] split at the *last* colon.
				var cs = comp as string ?? comp.ToString();

				if (int.TryParse(cs, out var compInstance))
				{
					search.targetName = "";
					search.targetInstance = compInstance;
				}
				else
				{
					var colon = cs.LastIndexOf(':');

					if (colon != -1)
					{
						search.targetName = cs[..colon];
						search.targetInstance = cs[(colon + 1)..].Ai();
					}
					else
					{
						search.targetName = cs;
						search.targetInstance = 1;
					}
				}

				if (!FindComponent(mmDev, search))
				{
					return Errors.TargetErrorOccurred($"Component {comp} not found.");
				}
				else if (search.targetControl == SoundControlType.IID)
				{
					return search.control;//The nint.
				}
				else if (search.targetControl == SoundControlType.Name)
				{
					return search.name;
				}
				else if (search.control == null)
				{
					//AHK raises ERR_SOUND_CONTROLTYPE here; returning 0 silently reports a real volume.
					return Errors.TargetErrorOccurred($"Component {comp} doesn't support this control type.");
				}
				else if (search.targetControl == SoundControlType.Volume)
				{
					object comobj = search.control is long ll ? Marshal.GetObjectForIUnknown((nint)ll) : search.control;

					if (comobj is IAudioVolumeLevel avl)
					{
						if (avl.GetChannelCount(out var channelCount) < 0)
						{
							ReleaseControl(search);
							return Errors.ErrorOccurred("Could not get channel count.");
						}

						//One block holding three per-channel slices, matching AHK's level/level_min/level_range.
						float[] level = new float[3 * channelCount];
						float f, maxLevel = 0;

						for (var ii = 0u; ii < channelCount; ++ii)
						{
							if (avl.GetLevel(ii, out var db) < 0 ||
									avl.GetLevelRange(ii, out var minDb, out var maxDb, out f) < 0)
							{
								ReleaseControl(search);
								return Errors.ErrorOccurred("Could not get level or level range.");
							}

							//Convert dB to scalar.
							var levelMin = channelCount + ii;
							var levelRange = (channelCount * 2) + ii;
							level[levelMin] = (float)Math.Pow(10.0, minDb / 20.0);
							level[levelRange] = (float)Math.Pow(10.0, maxDb / 20.0) - level[levelMin];
							//Compensate for differing level ranges. (No effect if range is -96..0 dB.)
							level[ii] = ((float)Math.Pow(10.0, db / 20.0) - level[levelMin]) / level[levelRange];

							// Windows reports the highest level as the overall volume.
							if (maxLevel < level[ii])
								maxLevel = level[ii];
						}

						if (soundSet)
						{
							if (adjust)
								settingScalar = Math.Clamp(settingScalar + maxLevel, 0.0f, 1.0f);

							for (var ii = 0u; ii < channelCount; ++ii)
							{
								var levelMin = channelCount + ii;
								var levelRange = (channelCount * 2) + ii;
								f = settingScalar;

								if (maxLevel != 0)
									f *= level[ii] / maxLevel;//Preserve balance.

								f = level[levelMin] + f * level[levelRange];//Compensate for differing level ranges.
								level[ii] = 20 * (float)Math.Log10(f);//Convert scalar to dB.
							}

							//SetLevelAllChannel used to throw from the marshaller on failure; now that it carries
							//[PreserveSig] the status has to be raised here, or a failed set looks like success.
							//AHK assigns hr here too and raises an OSError from it, so this matches.
							Guid guid = Guid.Empty;
							var setHr = avl.SetLevelAllChannel(level, channelCount, ref guid);

							if (setHr < 0)
							{
								ReleaseControl(search);
								return Errors.OSErrorOccurredForHR(setHr);
							}
						}
						else
							resultFloat = maxLevel * 100;
					}
				}
				else if (search.targetControl == SoundControlType.Mute)
				{
					object comobj = search.control is long ll ? Marshal.GetObjectForIUnknown((nint)ll) : search.control;

					if (comobj is IAudioMute am)
					{
						var res = 0;

						if (!soundSet || adjust)
							res = am.GetMute(out resultBool);

						if (soundSet && res >= 0)
						{
							Guid guid = Guid.Empty;
							res = am.SetMute(adjust ? !resultBool : settingScalar > 0, ref guid);
						}

						//AHK assigns hr for both calls and raises an OSError from it before returning.
						if (res < 0)
						{
							ReleaseControl(search);
							return Errors.OSErrorOccurredForHR(res);
						}
					}
				}

				ReleaseControl(search);
			}

			//Mute is documented as returning 0 or 1, and Linux/macOS already do. Returning a bool made
			//SoundSetMute(SoundGetMute()) a no-op, because a bool does not convert back to a number.
			return search.targetControl switch
		{
				SoundControlType.Volume => (double)resultFloat,
					SoundControlType.Mute => resultBool ? 1L : 0L,
					_ => null,
			};
		}

		/// <summary>
		/// Internal helper to release the interface pointer <see cref="FindComponent(MMDevice, SoundComponentSearch)"/>
		/// took a reference on. Not called for <see cref="SoundControlType.IID"/>, which deliberately
		/// transfers ownership of the pointer to the script.
		/// </summary>
		/// <param name="search">The completed search holding the control.</param>
		private static void ReleaseControl(SoundComponentSearch search)
		{
			if (search.control is long ptr && ptr != 0)
				_ = Marshal.Release((nint)ptr);

			search.control = null;
		}

		/// <summary>
		/// Internal helper to determine whether a specific device exists.
		/// </summary>
		/// <param name="mmDev">The device to search for.</param>
		/// <param name="search">The type of search to do.</param>
		/// <returns>True if found, else false.</returns>
		private static bool FindComponent(MMDevice mmDev, SoundComponentSearch search)
		{
			search.count = 0;
			search.control = null;
			search.name = null;
			search.ignoreRemainingSubunits = false;
			var top = mmDev.DeviceTopology;

			if (top.GetConnector(0, out var conn) >= 0)
			{
				//The endpoint's data flow decides which direction FindComponent walks, so it must be
				//recorded on the search rather than discarded, or capture devices search the wrong way.
				if (conn.GetDataFlow(out search.dataFlow) >= 0)
				{
					if (conn.GetConnectedTo(out var conTo) >= 0)
					{
						if (conTo is IPart part)
							_ = FindComponent(part, search);
					}
				}
			}

			return search.count == search.targetInstance;
		}

		/// <summary>
		/// Internal helper to determine whether a specific component exists.
		/// </summary>
		/// <param name="root">The root of the device hierarchy.</param>
		/// <param name="search">The type of search to do.</param>
		/// <returns>True if found, else false.</returns>
		private static bool FindComponent(IPart root, SoundComponentSearch search)
		{
			IPartsList partsList;

			if ((search.dataFlow == DataFlow.Render ?
					root.EnumPartsIncoming(out partsList) :
					root.EnumPartsOutgoing(out partsList)) < 0)
				return false;

			if (partsList.GetCount(out var partCount) < 0)
				partCount = 0;

			for (var i = 0u; i < partCount; i++)
			{
				if (partsList.GetPart(i, out var part) < 0)
					continue;

				//The type of the enumerated child decides Connector vs Subunit, not the type of the
				//part being recursed from; testing root here classified every child as its parent.
				if (part.GetPartType(out var partType) >= 0)
				{
					if (partType == PartTypeEnum.Connector)
					{
						//An empty target name matches any connector; otherwise the name must match in full,
						//as AHK compares with _wcsicmp (prefix matching is used for devices, not components).
						if (partCount == 1//Ignore Connectors with no Subunits of their own.
								&& (string.IsNullOrEmpty(search.targetName) ||
									(part.GetName(out var partName) >= 0 && string.Equals(partName, search.targetName, StringComparison.OrdinalIgnoreCase))
								   )
						   )
						{
							if (++search.count == search.targetInstance)
							{
								switch (search.targetControl)
								{
									case SoundControlType.Volume:
										break;

									case SoundControlType.Mute:
										break;

									case SoundControlType.Name:
										_ = part.GetName(out search.name);
										break;

									case SoundControlType.IID:
									{
										//Permit retrieving the IPart or IConnector itself.  Since there may be
										//multiple connected Subunits (and they can be enumerated or retrieved
										//via the Connector IPart), this is only done for the Connector.
										//Need the specific interface pointer, else ComCall() will fail when using IAudioMeterInformation.
										var iptr = Marshal.GetIUnknownForObject(part);

										if (Marshal.QueryInterface(iptr, in search.targetIid, out var ptr) >= 0)
										{
											if (ptr != 0)
												search.control = ptr.ToInt64();
										}

										_ = Marshal.Release(iptr);
										break;
									}
								}

								return true;
							}
						}
					}
					else//Subunit.
					{
						//Recursively find the Connector nodes linked to this part.
						if (FindComponent(part, search))
						{
							//A matching connector part has been found with this part as one of the nodes used
							//to reach it.  Therefore, if this part supports the requested control interface,
							//it can in theory be used to control the component.  An example path might be:
							//   Output < Master Mute < Master Volume < Sum < Mute < Volume < CD Audio
							//Parts are considered from right to left, as we return from recursion.
							if (search.control == null && !search.ignoreRemainingSubunits)
							{
								//Query this part for the requested interface and let caller check the result.
								//Most subunits do not support it, which is expected and must not throw.
								if (part.Activate(ClsCtx.ALL, ref search.targetIid, out search.control) >= 0 && search.control != null)
								{
									//Need the specific interface pointer, else ComCall() will fail when using IAudioMeterInformation.
									var iptr = Marshal.GetIUnknownForObject(search.control);

									if (Marshal.QueryInterface(iptr, in search.targetIid, out var ptr) >= 0)
									{
										if (ptr != 0)
											search.control = ptr.ToInt64();
									}

									_ = Marshal.Release(iptr);
								}

								//If this subunit has siblings, ignore any controls further up the line
								//as they're likely shared by other components (i.e. master controls).
								if (partCount > 1)
									search.ignoreRemainingSubunits = true;
							}

							return true;
						}
					}
				}
			}

			return false;
		}

		/// <summary>
		/// Internal helper to get a device from a string description or a number.
		/// </summary>
		/// <param name="obj0">The name or number of the device to search for.</param>
		/// <returns>The device if found, else null.</returns>
		private static MMDevice GetDevice(object obj0)
		{
			var deviceEnum = new MMDeviceEnumerator();
			MMDevice mmDev = null;

			if (obj0 == null || obj0.ToString() == "")
			{
				mmDev = deviceEnum.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);
			}
			else
			{
				var targetIndex = 0;
				var targetName = "";

				//Mirrors AHK's SoundSetGet_GetDevice(): Name:Index (split at the *last* colon), else a bare
				//index, else a name. A numeric string is an index, exactly as a numeric value is.
				var ds = obj0 as string ?? obj0.ToString();
				var colon = ds.LastIndexOf(':');

				if (colon != -1)
				{
					targetName = ds[..colon];
					targetIndex = ds[(colon + 1)..].Ai() - 1;
				}
				else if (int.TryParse(ds, out targetIndex))
				{
					targetName = "";
					--targetIndex;
				}
				else
					targetName = ds;

				var devices = deviceEnum.EnumerateAudioEndPoints(DataFlow.All, DeviceState.Active | DeviceState.Unplugged).ToList();

				if (targetName.Length > 0)
				{
					foreach (var device in devices)
					{
						//Keysharp.Builtins.Dialogs.MsgBox(device.FriendlyName
						//                           + "\r\n" + device.DeviceFriendlyName
						//                           + "\r\n" + device.ID
						//                           + "\r\n" + device.InstanceId);
						if (device.FriendlyName.StartsWith(targetName, StringComparison.OrdinalIgnoreCase) && targetIndex-- == 0)
						{
							mmDev = device;
							break;
						}
					}
				}
				else
				{
					if (targetIndex < devices.Count)
						mmDev = devices[targetIndex];
				}
			}

			return mmDev;
		}

		/// <summary>
		/// Internal helper to aid in searching for devices and components.
		/// </summary>
		private class SoundComponentSearch
		{
			//Internal use/results:
			internal object control;

			internal int count;

			//Internal use:
			internal DataFlow dataFlow = DataFlow.Render;

			internal bool ignoreRemainingSubunits;
			internal string name;
			internal SoundControlType targetControl;

			//Parameters of search:
			internal Guid targetIid;

			internal int targetInstance;
			internal string targetName;
			// Valid only when target_control == SoundControlType::Name.
		};
#elif OSX
		// CoreAudio property selectors (FourCC values)
		private const uint kAudioObjectSystemObject = 1u;
		private const uint kAudioHardwarePropertyDefaultOutputDevice              = 0x644F7574u; // 'dOut'
		private const uint kAudioHardwarePropertyDevices                          = 0x64657623u; // 'dev#'
		private const uint kAudioHardwareServiceDevicePropertyVirtualMasterVolume = 0x766D7663u; // 'vmvc'
		private const uint kAudioDevicePropertyVolumeScalar                       = 0x766F6C75u; // 'volu'
		private const uint kAudioDevicePropertyMute                               = 0x6D757465u; // 'mute'
		private const uint kAudioDevicePropertyTransportType                      = 0x7472616Eu; // 'tran'
		private const uint kAudioObjectPropertyName                               = 0x6C6E616Du; // 'lnam'
		private const uint kAudioObjectPropertyScopeGlobal                        = 0x676C6F62u; // 'glob'
		private const uint kAudioObjectPropertyScopeOutput                        = 0x6F757470u; // 'outp'
		private const uint kAudioObjectPropertyScopeInput                         = 0x696E7074u; // 'inpt'
		private const uint kAudioObjectPropertyElementMain                        = 0u;
		private const uint kAudioDeviceTransportTypeAggregate                     = 0x61676772u; // 'aggr'
		private const uint kAudioDeviceTransportTypeVirtual                       = 0x76697274u; // 'virt'

		[StructLayout(LayoutKind.Sequential)]
		private struct AudioObjectPropertyAddress
		{
			public uint mSelector;
			public uint mScope;
			public uint mElement;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct CFRange
		{
			public long location;
			public long length;
		}

		[DllImport("/System/Library/Frameworks/CoreAudio.framework/CoreAudio")]
		private static extern int AudioObjectGetPropertyData(uint objectId, ref AudioObjectPropertyAddress addr, uint qualifierSize, nint qualifierData, ref uint dataSize, nint outData);

		[DllImport("/System/Library/Frameworks/CoreAudio.framework/CoreAudio")]
		private static extern int AudioObjectSetPropertyData(uint objectId, ref AudioObjectPropertyAddress addr, uint qualifierSize, nint qualifierData, uint dataSize, nint inData);

		[DllImport("/System/Library/Frameworks/CoreAudio.framework/CoreAudio")]
		private static extern int AudioObjectGetPropertyDataSize(uint objectId, ref AudioObjectPropertyAddress addr, uint qualifierSize, nint qualifierData, out uint outDataSize);

		[DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
		private static extern long CFStringGetLength(nint cfStr);

		[DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
		private static extern void CFStringGetCharacters(nint cfStr, CFRange range, nint buffer);

		[DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
		private static extern void CFRelease(nint cfTypeRef);

		private static int GetPropertyFloat(uint objectId, AudioObjectPropertyAddress addr, out float value)
		{
			var ptr = Marshal.AllocHGlobal(sizeof(float));

			try
			{
				uint size = sizeof(float);
				var result = AudioObjectGetPropertyData(objectId, ref addr, 0, nint.Zero, ref size, ptr);
				value = result == 0 ? BitConverter.Int32BitsToSingle(Marshal.ReadInt32(ptr)) : 0f;
				return result;
			}
			finally
			{
				Marshal.FreeHGlobal(ptr);
			}
		}

		private static int SetPropertyFloat(uint objectId, AudioObjectPropertyAddress addr, float value)
		{
			var ptr = Marshal.AllocHGlobal(sizeof(float));

			try
			{
				Marshal.WriteInt32(ptr, BitConverter.SingleToInt32Bits(value));
				return AudioObjectSetPropertyData(objectId, ref addr, 0, nint.Zero, sizeof(float), ptr);
			}
			finally
			{
				Marshal.FreeHGlobal(ptr);
			}
		}

		private static int GetPropertyUInt(uint objectId, AudioObjectPropertyAddress addr, out uint value)
		{
			var ptr = Marshal.AllocHGlobal(sizeof(uint));

			try
			{
				uint size = sizeof(uint);
				var result = AudioObjectGetPropertyData(objectId, ref addr, 0, nint.Zero, ref size, ptr);
				value = result == 0 ? (uint)Marshal.ReadInt32(ptr) : 0u;
				return result;
			}
			finally
			{
				Marshal.FreeHGlobal(ptr);
			}
		}

		private static int SetPropertyUInt(uint objectId, AudioObjectPropertyAddress addr, uint value)
		{
			var ptr = Marshal.AllocHGlobal(sizeof(uint));

			try
			{
				Marshal.WriteInt32(ptr, (int)value);
				return AudioObjectSetPropertyData(objectId, ref addr, 0, nint.Zero, sizeof(uint), ptr);
			}
			finally
			{
				Marshal.FreeHGlobal(ptr);
			}
		}

		private static uint[] GetPropertyUInts(uint objectId, AudioObjectPropertyAddress addr)
		{
			if (AudioObjectGetPropertyDataSize(objectId, ref addr, 0, nint.Zero, out var dataSize) != 0 || dataSize == 0)
				return [];

			var ptr = Marshal.AllocHGlobal((int)dataSize);

			try
			{
				if (AudioObjectGetPropertyData(objectId, ref addr, 0, nint.Zero, ref dataSize, ptr) != 0)
					return [];

				var count = (int)(dataSize / sizeof(uint));
				var result = new uint[count];

				for (var i = 0; i < count; i++)
					result[i] = (uint)Marshal.ReadInt32(ptr, i * sizeof(uint));

				return result;
			}
			finally
			{
				Marshal.FreeHGlobal(ptr);
			}
		}

		private static string GetPropertyString(uint objectId, AudioObjectPropertyAddress addr)
		{
			var ptrSize = nint.Size;
			var cfStrHolder = Marshal.AllocHGlobal(ptrSize);

			try
			{
				uint size = (uint)ptrSize;

				if (AudioObjectGetPropertyData(objectId, ref addr, 0, nint.Zero, ref size, cfStrHolder) != 0)
					return "";

				var cfStr = Marshal.ReadIntPtr(cfStrHolder);

				if (cfStr == nint.Zero)
					return "";

				try
				{
					var len = CFStringGetLength(cfStr);

					if (len <= 0)
						return "";

					var charBuf = Marshal.AllocHGlobal((int)(len * 2));

					try
					{
						CFStringGetCharacters(cfStr, new CFRange { location = 0, length = len }, charBuf);
						return Marshal.PtrToStringUni(charBuf, (int)len) ?? "";
					}
					finally
					{
						Marshal.FreeHGlobal(charBuf);
					}
				}
				finally
				{
					CFRelease(cfStr);
				}
			}
			finally
			{
				Marshal.FreeHGlobal(cfStrHolder);
			}
		}

		private static uint GetDefaultOutputDevice()
		{
			var addr = new AudioObjectPropertyAddress
			{
				mSelector = kAudioHardwarePropertyDefaultOutputDevice,
				mScope = kAudioObjectPropertyScopeGlobal,
				mElement = kAudioObjectPropertyElementMain
			};
			GetPropertyUInt(kAudioObjectSystemObject, addr, out var deviceId);
			return deviceId;
		}

		private static Dictionary<int, (uint id, string name)> GetDevices()
		{
			var result = new Dictionary<int, (uint, string)>();
			var devicesAddr = new AudioObjectPropertyAddress
			{
				mSelector = kAudioHardwarePropertyDevices,
				mScope = kAudioObjectPropertyScopeGlobal,
				mElement = kAudioObjectPropertyElementMain
			};
			var nameAddr = new AudioObjectPropertyAddress
			{
				mSelector = kAudioObjectPropertyName,
				mScope = kAudioObjectPropertyScopeGlobal,
				mElement = kAudioObjectPropertyElementMain
			};
			var transportAddr = new AudioObjectPropertyAddress
			{
				mSelector = kAudioDevicePropertyTransportType,
				mScope = kAudioObjectPropertyScopeGlobal,
				mElement = kAudioObjectPropertyElementMain
			};
			var deviceIds = GetPropertyUInts(kAudioObjectSystemObject, devicesAddr);
			var outputDevices = new System.Collections.Generic.List<(uint id, string name)>();
			var inputDevices = new System.Collections.Generic.List<(uint id, string name)>();
			var vmvcAddr = new AudioObjectPropertyAddress
			{
				mSelector = kAudioHardwareServiceDevicePropertyVirtualMasterVolume,
				mScope = kAudioObjectPropertyScopeOutput,
				mElement = kAudioObjectPropertyElementMain
			};

			foreach (var did in deviceIds)
			{
				GetPropertyUInt(did, transportAddr, out var transport);

				if (transport == kAudioDeviceTransportTypeAggregate || transport == kAudioDeviceTransportTypeVirtual)
					continue;

				var name = GetPropertyString(did, nameAddr);
				// Devices with vmvc on output scope are output-capable; enumerate them first.
				if (AudioObjectGetPropertyDataSize(did, ref vmvcAddr, 0, nint.Zero, out _) == 0)
					outputDevices.Add((did, name));
				else
					inputDevices.Add((did, name));
			}

			var idx = 0;
			foreach (var dev in outputDevices) result[idx++] = dev;
			foreach (var dev in inputDevices) result[idx++] = dev;
			return result;
		}

		private static int GetDeviceVolume(uint deviceId, out float volume)
		{
			var addr = new AudioObjectPropertyAddress
			{
				mSelector = kAudioHardwareServiceDevicePropertyVirtualMasterVolume,
				mScope = kAudioObjectPropertyScopeOutput,
				mElement = kAudioObjectPropertyElementMain
			};
			var result = GetPropertyFloat(deviceId, addr, out volume);

			if (result != 0)
			{
				// Input-only devices (e.g. microphone): read input gain via scalar on input scope.
				addr.mSelector = kAudioDevicePropertyVolumeScalar;
				addr.mScope = kAudioObjectPropertyScopeInput;
				result = GetPropertyFloat(deviceId, addr, out volume);

				if (result != 0)
				{
					addr.mElement = 1u;
					result = GetPropertyFloat(deviceId, addr, out volume);
				}
			}

			return result;
		}

		private static int SetDeviceVolume(uint deviceId, float volume)
		{
			var addr = new AudioObjectPropertyAddress
			{
				mSelector = kAudioHardwareServiceDevicePropertyVirtualMasterVolume,
				mScope = kAudioObjectPropertyScopeOutput,
				mElement = kAudioObjectPropertyElementMain
			};
			var result = SetPropertyFloat(deviceId, addr, volume);

			if (result != 0)
			{
				// Input-only devices (e.g. microphone): set input gain via scalar on input scope.
				addr.mSelector = kAudioDevicePropertyVolumeScalar;
				addr.mScope = kAudioObjectPropertyScopeInput;
				result = SetPropertyFloat(deviceId, addr, volume);

				if (result != 0)
				{
					addr.mElement = 1u;
					result = SetPropertyFloat(deviceId, addr, volume);
				}
			}

			return result;
		}

		private static int GetDeviceMute(uint deviceId, out bool muted)
		{
			var addr = new AudioObjectPropertyAddress
			{
				mSelector = kAudioDevicePropertyMute,
				mScope = kAudioObjectPropertyScopeOutput,
				mElement = kAudioObjectPropertyElementMain
			};
			var result = GetPropertyUInt(deviceId, addr, out var val);

			if (result != 0)
			{
				// Input-only devices (e.g. microphone) use input scope.
				addr.mScope = kAudioObjectPropertyScopeInput;
				result = GetPropertyUInt(deviceId, addr, out val);
			}

			muted = val != 0;
			return result;
		}

		private static int SetDeviceMute(uint deviceId, bool muted)
		{
			var muteVal = muted ? 1u : 0u;
			var addr = new AudioObjectPropertyAddress
			{
				mSelector = kAudioDevicePropertyMute,
				mScope = kAudioObjectPropertyScopeOutput,
				mElement = kAudioObjectPropertyElementMain
			};
			var result = SetPropertyUInt(deviceId, addr, muteVal);

			if (result != 0)
			{
				// Input-only devices (e.g. microphone) use input scope.
				addr.mScope = kAudioObjectPropertyScopeInput;
				result = SetPropertyUInt(deviceId, addr, muteVal);
			}

			return result;
		}

		private static object DoSound(SoundCommands soundCmd, object obj0, object obj1 = null, object obj2 = null)
		{
			var soundSet = soundCmd >= SoundCommands.SoundSetVolume;
			var device = soundSet ? obj2 : obj1;

			// macOS has no component topology like Windows. If a caller passes a numeric component
			// with no device (e.g. SoundGetName(n)), treat it as a device index so scripts that
			// enumerate devices via the component parameter work correctly.
			if (!soundSet && device == null)
			{
				var compStr = obj0?.ToString() ?? "";

				if (compStr.Length > 0 && int.TryParse(compStr, out _))
					device = obj0;
			}

			uint deviceId;

			if (device == null || device.ToString().Length == 0)
			{
				deviceId = GetDefaultOutputDevice();

				if (deviceId == 0)
					return Errors.OSErrorOccurred("", "No default output device found.");
			}
			else
			{
				var devStr = device.ToString();
				var devs = GetDevices();
				deviceId = 0;

				if (int.TryParse(devStr, out var idx) && devs.TryGetValue(idx - 1, out var byIndex))
					deviceId = byIndex.id;

				if (deviceId == 0)
				{
					foreach (var kv in devs)
					{
						if (kv.Value.name.StartsWith(devStr, StringComparison.OrdinalIgnoreCase))
						{
							deviceId = kv.Value.id;
							break;
						}
					}
				}

				if (deviceId == 0)
					return Errors.TargetErrorOccurred($"Device {device} not found.");
			}

			switch (soundCmd)
			{
				case SoundCommands.SoundGetVolume:
				{
					var rc = GetDeviceVolume(deviceId, out var vol);

					if (rc != 0)
						return Errors.OSErrorOccurred("", $"Failed to query volume (CoreAudio error 0x{(uint)rc:X8}).");

					return (double)(vol * 100f);
				}

				case SoundCommands.SoundGetMute:
				{
					var rc = GetDeviceMute(deviceId, out var muted);

					if (rc != 0)
						return Errors.OSErrorOccurred("", $"Failed to query mute state (CoreAudio error 0x{(uint)rc:X8}).");

					return muted ? 1L : 0L;
				}

				case SoundCommands.SoundGetName:
				{
					var nameAddr = new AudioObjectPropertyAddress
					{
						mSelector = kAudioObjectPropertyName,
						mScope = kAudioObjectPropertyScopeGlobal,
						mElement = kAudioObjectPropertyElementMain
					};
					return GetPropertyString(deviceId, nameAddr);
				}

				case SoundCommands.SoundSetVolume:
				{
					var valStr = obj0?.ToString() ?? "";
					var adjust = valStr.Length > 0 && (valStr[0] == '-' || valStr[0] == '+');
					float newVol;

					if (adjust)
					{
						var rc = GetDeviceVolume(deviceId, out var currentVol);

						if (rc != 0)
							return Errors.OSErrorOccurred("", $"Failed to query current volume (CoreAudio error 0x{(uint)rc:X8}).");

						newVol = Math.Clamp(currentVol + (float)(obj0.Ad() * 0.01), 0f, 1f);
					}
					else
					{
						newVol = Math.Clamp((float)(obj0.Ad() * 0.01), 0f, 1f);
					}

					var setRc = SetDeviceVolume(deviceId, newVol);

					if (setRc != 0)
						return Errors.OSErrorOccurred("", $"Failed to set volume (CoreAudio error 0x{(uint)setRc:X8}).");
				}
				break;

				case SoundCommands.SoundSetMute:
				{
					var act = Conversions.ConvertOnOffToggle(obj0);
					bool muted;

					if (act == ToggleValueType.Toggle)
					{
						var rc = GetDeviceMute(deviceId, out var currentMute);

						if (rc != 0)
							return Errors.OSErrorOccurred("", $"Failed to query mute state (CoreAudio error 0x{(uint)rc:X8}).");

						muted = !currentMute;
					}
					else
					{
						muted = act == ToggleValueType.On;
					}

					var setRc = SetDeviceMute(deviceId, muted);

					if (setRc != 0)
						return Errors.OSErrorOccurred("", $"Failed to set mute state (CoreAudio error 0x{(uint)setRc:X8}).");
				}
				break;
			}

			return DefaultObject;
		}
#else
		private static object DoSound(SoundCommands soundCmd, object obj0, object obj1 = null, object obj2 = null)
		{
			return DefaultObject;
		}
#endif

		/// <summary>
		/// Enum for specifying different sound operations which will be passed to <see cref="DoSound(SoundCommands, object, object, object)"/>
		/// </summary>
		private enum SoundCommands
		{
			SoundGetVolume = 0, SoundGetMute, SoundGetName
#if WINDOWS
			, SoundGetInterface
#endif
			, SoundSetVolume, SoundSetMute
		}

		/// <summary>
		/// Enum for specifying different sound control types.
		/// </summary>
		private enum SoundControlType
		{
			Volume,
			Mute,
			Name
#if WINDOWS
			, IID
#endif
		}
	}
}
