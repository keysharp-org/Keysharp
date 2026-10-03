namespace Keysharp.Builtins
{
	/// <summary>
	/// Public interface for sound-related functions.
	/// </summary>
	public static class Sound
	{

		/// <summary>
		/// Emits a tone from the PC speaker.
		/// </summary>
		/// <param name="frequency">If omitted, it defaults to 523. Otherwise, specify the frequency of the sound, a number between 37 and 32767.</param>
		/// <param name="duration">If omitted, it defaults to 150. Otherwise, specify the duration of the sound, in milliseconds.</param>
		public static object SoundBeep(object frequency = null, object duration = null)
		{
			if (!frequency.CoerceInt(out var freq, 523) || !duration.CoerceInt(out var time, 150))
				return DefaultObject;

			if (freq is < SoundPlayback.MinFrequency or > SoundPlayback.MaxFrequency)
				return Errors.ValueErrorOccurred($"Frequency must be from {SoundPlayback.MinFrequency} through {SoundPlayback.MaxFrequency}.", frequency);
			if (time < 0)
				time = 150;

			if (time == 0)
				return DefaultObject;
#if WINDOWS
			// Console.Beep is Win32 Beep(), which is exactly what AHK calls.
			Console.Beep(freq, time);
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
		public static object SoundGetInterface([UserDeclaredName("IID")] object iid, object component = null, object device = null) => DoSound(SoundCommands.SoundGetInterface, iid, component, device);

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

			if (!filename.CoerceString(out var file) || !wait.CoerceString(out var w))
				return DefaultObject;

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

		private static object DoSound(SoundCommands command, object value, object componentOrDevice = null, object device = null)
		{
			var setting = command >= SoundCommands.SoundSetVolume;
			var component = setting ? componentOrDevice : value;
			var selector = setting ? device : componentOrDevice;
#if WINDOWS
			if (command == SoundCommands.SoundGetInterface)
			{
				if (!value.CoerceString(out var iid) || !componentOrDevice.CoerceString(out var interfaceComponent) || !device.CoerceString(out var interfaceDevice))
					return DefaultObject;

				try { return DoSoundWindows(command, iid, interfaceComponent, interfaceDevice); }
				catch (COMException ex) { return Errors.OSErrorOccurredForHR(ex.HResult); }
			}
#endif
			if (!component.CoerceString(out var componentText) || !selector.CoerceString(out var selectorText))
				return DefaultObject;

			if (componentText.Length > 0)
			{
#if WINDOWS
				try { return DoSoundWindows(command, value, componentText, selectorText); }
				catch (COMException ex) { return Errors.OSErrorOccurredForHR(ex.HResult); }
#else
				return Errors.TargetErrorOccurred($"Component {componentText} not found.");
#endif
			}

			var volumeControl = command is SoundCommands.SoundGetVolume or SoundCommands.SoundSetVolume;
			double newValue = 0;
			var adjust = false;

			if (setting)
			{
				if (!value.CoerceDouble(out newValue) || !value.CoerceString(out var text))
					return DefaultObject;

				adjust = text.Length > 0 && text[0] is '+' or '-';
			}

			var backend = Script.TheScript.AudioService.Backend;

			if (backend == null)
				return Errors.TargetErrorOccurred($"Sound device {selectorText} not found.");
			if (!TryResolveSoundDevice(backend, selectorText, out var endpoint, out var resolveError))
				return resolveError != 0 ? Errors.OSErrorOccurredForHR(resolveError) : Errors.TargetErrorOccurred($"Sound device {selectorText} not found.");

			if (command == SoundCommands.SoundGetName)
				return endpoint.Name;


			if (volumeControl)
			{
				double current = 0;

				if ((!setting || adjust) && !backend.TryGetVolume(endpoint.Kind, endpoint.Id, out current))
					return SoundControlError(backend);

				if (!setting)
					return current * 100.0;

				if (!backend.TrySetVolume(endpoint.Kind, endpoint.Id, Math.Clamp(newValue / 100.0 + (adjust ? current : 0), 0, 1)))
					return SoundControlError(backend);
			}
			else
			{
				var current = false;

				if ((!setting || adjust) && !backend.TryGetMute(endpoint.Kind, endpoint.Id, out current))
					return SoundControlError(backend);

				if (!setting)
					return current ? 1L : 0L;

				if (!backend.TrySetMute(endpoint.Kind, endpoint.Id, adjust ? !current : newValue > 0))
					return SoundControlError(backend);
			}

			return DefaultObject;
		}

		private static object SoundControlError(Keysharp.Internals.Audio.IAudioBackend backend) => backend.LastError != 0
			? Errors.OSErrorOccurredForHR(backend.LastError)
			: Errors.OSErrorOccurredWithMessage("The sound device does not expose the requested control or the operation failed.");

		internal static bool TryResolveSoundDevice(Keysharp.Internals.Audio.IAudioBackend backend, string selector,
			out Keysharp.Internals.Audio.AudioDeviceDescriptor device)
			=> TryResolveSoundDevice(backend, selector, out device, out _);

		internal static bool TryResolveSoundDevice(Keysharp.Internals.Audio.IAudioBackend backend, string selector,
			out Keysharp.Internals.Audio.AudioDeviceDescriptor device, out int error)
		{
			device = default;
			error = 0;

			if (selector.Length == 0)
			{
				var found = backend.TryGetDefaultDevice(Keysharp.Internals.Audio.AudioDeviceKind.Output, out device);
				if (!found) error = backend.LastError;
				return found;
			}

			var name = selector;
			var instance = 1;
			var colon = selector.LastIndexOf(':');

			if (colon >= 0)
			{
				name = selector[..colon];

				if (!int.TryParse(selector.AsSpan(colon + 1), out instance))
					return false;
			}
			else if (int.TryParse(selector, out var number))
			{
				name = "";
				instance = number;
			}

			if (instance < 1)
				return false;

			foreach (var endpoint in backend.EnumerateAllDevices())
				if (endpoint.Name.StartsWith(name, StringComparison.OrdinalIgnoreCase) && --instance == 0)
				{
					device = endpoint;
					return true;
				}

			error = backend.LastError;
			return false;
		}

#if WINDOWS

		/// <summary>
		/// Internal helper to help with various sound processing commands.
		/// </summary>
		/// <param name="soundCmd">The sound command to perform.</param>
		/// <param name="obj0">The sound component to operate on, or the value to use.</param>
		/// <param name="comp">The component selector.</param>
		/// <param name="dev">The device selector.</param>
		/// <returns>Various values depending on the sound command being processed.</returns>
		/// <exception cref="TargetError">A <see cref="TargetError"/> exception is thrown if the component/device cannot not found.</exception>
		/// <exception cref="Error">An <see cref="Error"/> exception is thrown if the channels, levels or range cannot not found.</exception>
		private static object DoSoundWindows(SoundCommands soundCmd, object obj0, string comp, string dev)
		{
			var soundSet = soundCmd >= SoundCommands.SoundSetVolume;
			var search = new SoundComponentSearch
			{
				targetControl = (SoundControlType)((int)soundCmd - (soundSet ? (int)SoundCommands.SoundSetVolume : 0))
			};

			switch (search.targetControl)
			{
				case SoundControlType.Volume:
					search.targetIid = new Guid("7FB7B48F-531D-44A2-BCB3-5AD5A134B3DC");
					break;

				case SoundControlType.Mute:
					search.targetIid = new Guid("DF45AEEA-B74A-4B6B-AFAD-2366B6AA012E");
					break;

				case SoundControlType.IID:
					search.targetIid = new Guid((string)obj0);
					break;
			}

			var settingScalar = 0.0f;
			var adjust = false;

			if (soundSet)
			{
				if (!obj0.CoerceDouble(out var settingPercent) || !obj0.CoerceString(out var settingText))
					return DefaultObject;

				settingScalar = Math.Clamp((float)(settingPercent * 0.01), -1.0f, 1.0f);
				adjust = settingText.Length > 0 && settingText[0] is '-' or '+';
			}

			object controlObject = null;
			var resultFloat = 0.0f;
			var resultBool = false;
			using var mmDev = GetDevice(dev, Script.TheScript.AudioService.Backend);

			try
			{
				if (mmDev == null)
					return Errors.TargetErrorOccurred($"Component {comp}, device {dev} not found.");

				if (comp.Length == 0)//Component is Master (omitted).
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
									try
									{
										var iptr = Marshal.GetIUnknownForObject(activated);
										try
										{
											if (Marshal.QueryInterface(iptr, in search.targetIid, out var ptr) >= 0)
												resultPtr = ptr;
										}
										finally { Marshal.Release(iptr); }
									}
									finally { Marshal.ReleaseComObject(activated); }
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
				}
				else
				{
					//Mirrors AHK's SoundConvertComponent(): a component which parses as an integer is an instance
					//index with no name filter, otherwise it is Name[:Instance] split at the *last* colon.
					var cs = comp;

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
							_ = cs[(colon + 1)..].TryCoerceInt(out var instanceIdx);
							search.targetInstance = instanceIdx;
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
						object comobj = controlObject = search.control is long ll ? Marshal.GetObjectForIUnknown((nint)ll) : search.control;

						if (comobj is IAudioVolumeLevel avl)
						{
							var channelHr = avl.GetChannelCount(out var channelCount);
							if (channelHr < 0)
							{
								ReleaseControl(search);
								return Errors.OSErrorOccurredForHR(channelHr);
							}

							//One block holding three per-channel slices, matching AHK's level/level_min/level_range.
							float[] level = new float[3 * channelCount];
							float f, maxLevel = 0;

							for (var ii = 0u; ii < channelCount; ++ii)
							{
								var levelHr = avl.GetLevel(ii, out var db);
								var rangeHr = avl.GetLevelRange(ii, out var minDb, out var maxDb, out f);
								if (levelHr < 0 || rangeHr < 0)
								{
									ReleaseControl(search);
									return Errors.OSErrorOccurredForHR(levelHr < 0 ? levelHr : rangeHr);
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
						object comobj = controlObject = search.control is long ll ? Marshal.GetObjectForIUnknown((nint)ll) : search.control;

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

								return search.targetControl switch
			{
					SoundControlType.Volume => (double)resultFloat,
						SoundControlType.Mute => resultBool ? 1L : 0L,
						_ => null,
				};
			}
			finally
			{
				if (search.targetControl != SoundControlType.IID) ReleaseControl(search);
				if (controlObject != null && Marshal.IsComObject(controlObject)) Marshal.ReleaseComObject(controlObject);
			}

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
			var topology = mmDev.DeviceTopology;
			IConnector connector = null;
			IConnector connected = null;
			try
			{
				if (topology.GetConnector(0, out connector) >= 0 && connector.GetDataFlow(out search.dataFlow) >= 0
					&& connector.GetConnectedTo(out connected) >= 0 && connected is IPart part)
					FindComponent(part, search);
				return search.count == search.targetInstance;
			}
			finally
			{
				if (connected != null) Marshal.ReleaseComObject(connected);
				if (connector != null) Marshal.ReleaseComObject(connector);
			}
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

			try
			{
				if (partsList.GetCount(out var partCount) < 0)
					partCount = 0;

				for (var i = 0u; i < partCount; i++)
				{
					if (partsList.GetPart(i, out var part) < 0)
						continue;

					try
					{
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
										var activated = search.control;
										search.control = null;
										var iptr = Marshal.GetIUnknownForObject(activated);

										if (Marshal.QueryInterface(iptr, in search.targetIid, out var ptr) >= 0)
										{
											if (ptr != 0)
												search.control = ptr.ToInt64();
										}

										_ = Marshal.Release(iptr);
										Marshal.ReleaseComObject(activated);
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
					finally { Marshal.ReleaseComObject(part); }
				}

				return false;
			}
			finally { Marshal.ReleaseComObject(partsList); }

		}

		/// <summary>
		/// Internal helper to get a device from a string description or a number.
		/// </summary>
		/// <param name="selector">The name or number of the device to search for.</param>
		/// <param name="backend">The endpoint backend.</param>
		/// <returns>The device if found, else null.</returns>
		internal static MMDevice GetDevice(string selector, Keysharp.Internals.Audio.IAudioBackend backend)
		{
			if (backend == null)
				return null;
			if (!TryResolveSoundDevice(backend, selector, out var descriptor, out var error))
			{
				if (error != 0) throw new COMException(null, error);
				return null;
			}

			using var enumerator = new MMDeviceEnumerator();
			return enumerator.TryGetDevice(descriptor.Id, out var device) ? device : null;
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
