using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Keysharp.Internals.Scripting
{
	/// <summary>
	/// The application startup handoff built from a script's `#App { … }` blocks and standalone startup directives.
	/// This shared storage does not define source syntax: #App contributes declarative final application data, while
	/// standalone directives contribute resolved compiler or execution-control state needed during startup.
	/// Built by the lowerer, embedded in the compiled assembly as the
	/// <see cref="ResourceName"/> JSON resource, and read back by the Script constructor before any chrome
	/// (tray icon, main window, dialogs) exists — so every key here binds earlier than the auto-exec section
	/// could. Compiler-facing keys (Name, the assembly attributes, Icon, ConsoleApp) are additionally applied
	/// while the assembly is built.
	/// </summary>
	internal sealed class AppManifest
	{
		internal const string ResourceName = "Keysharp.App.json";
		internal const string IconResourceName = "Keysharp.App.ico";
		internal const string TrayIconResourceName = "Keysharp.App.Tray.ico";
		internal const string FileResourcePrefix = "Keysharp.App.Files/";
		/// <summary>The #App Name value used as the assembly identity returned by Assembly.GetName().Name.</summary>
		[JsonPropertyName("name")] public string Name { get; set; }
		[JsonPropertyName("title")] public string Title { get; set; }
		[JsonPropertyName("description")] public string Description { get; set; }
		[JsonPropertyName("configuration")] public string Configuration { get; set; }
		[JsonPropertyName("company")] public string Company { get; set; }
		[JsonPropertyName("product")] public string Product { get; set; }
		[JsonPropertyName("copyright")] public string Copyright { get; set; }
		[JsonPropertyName("trademark")] public string Trademark { get; set; }
		/// <summary>Assembly + file version ("major.minor[.build[.revision]]").</summary>
		[JsonPropertyName("version")] public string Version { get; set; }
		/// <summary>Script-relative .ico path: the script's default icon (windows, dialogs, tray unless
		/// <see cref="TrayIcon"/> overrides it), and the exe's win32 icon when compiled.</summary>
		[JsonPropertyName("icon")] public string Icon { get; set; }
		/// <summary>The tray's own icon: null = the script icon, otherwise a canonical script-relative source.</summary>
		[JsonPropertyName("trayIcon")] public string TrayIcon { get; set; }
		/// <summary>True when startup tray creation is suppressed.</summary>
		[JsonPropertyName("noTrayIcon")] public bool? NoTrayIcon { get; set; }
		/// <summary>The user-facing selector for <see cref="TrayIcon"/>. A positive number is a one-based icon
		/// group, a negative number is a native resource ID, and <see cref="TrayIconResource"/> names a managed
		/// resource instead. Both are null for the first icon.</summary>
		[JsonPropertyName("trayIconNumber")] public long? TrayIconNumber { get; set; }
		[JsonPropertyName("trayIconResource")] public string TrayIconResource { get; set; }
		/// <summary>Initial A_GuiTheme (Classic/System/Dark), applied before runtime windows or dialogs are created.</summary>
		[JsonPropertyName("guiTheme")] public string GuiTheme { get; set; }
		/// <summary>Single-instance mode (Force/Ignore/Prompt/Off), null when the script never asked.</summary>
		[JsonPropertyName("singleInstance")] public string SingleInstance { get; set; }
		[JsonPropertyName("consoleApp")] public bool? ConsoleApp { get; set; }
		[JsonPropertyName("hookMutexName")] public string HookMutexName { get; set; }
		/// <summary>Script-relative, '/'-separated paths embedded for FileInstall.</summary>
		[JsonPropertyName("files")] public List<string> Files { get; set; } = [];

		// Compiler-side only: physical build-machine locations never enter the serialized manifest.
		[JsonIgnore] internal string IconSourcePath { get; set; }
		[JsonIgnore] internal byte[] TrayIconBytes { get; private set; }
		[JsonIgnore] internal List<(string Relative, string Source)> FileSources { get; } = [];

		internal bool TrayIconSuppressed => NoTrayIcon == true;
		internal bool HasCustomTrayIcon => TrayIcon != null && !TrayIconSuppressed;
		internal void SetTrayIconDefault()
		{
			TrayIcon = null;
			NoTrayIcon = null;
			TrayIconNumber = null;
			TrayIconResource = null;
			TrayIconBytes = null;
		}

		internal void SetTrayIconSuppressed()
		{
			SetTrayIconDefault();
			NoTrayIcon = true;
		}

		internal bool TrySetTrayIcon(string logicalPath, string sourcePath, object selector, out string error)
		{
			error = null;

			if (logicalPath == null
				|| !string.Equals(logicalPath, AppResourcePath.Normalize(logicalPath), StringComparison.Ordinal))
			{
				error = "the tray icon source must be a canonical relative path";
				return false;
			}

			if (!TryNormalizeTrayIconSelector(selector, out var number, out var resource, out error))
				return false;

			var bytes = Keysharp.Internals.Images.ImageHelper.LoadIconSetBytes(sourcePath, resource ?? (object)number);

			if (bytes == null || bytes.Length == 0)
			{
				error = $"could not load the selected tray icon from {sourcePath}";
				return false;
			}

			TrayIcon = logicalPath;
			NoTrayIcon = null;
			TrayIconNumber = number;
			TrayIconResource = resource;
			TrayIconBytes = bytes;
			return true;
		}

		private static bool TryNormalizeTrayIconSelector(object selector, out long? number, out string resource, out string error)
		{
			number = null;
			resource = null;
			error = null;

			if (selector == null)
				return true;

			if (selector is string text)
			{
				if (long.TryParse(text, System.Globalization.NumberStyles.Integer,
					System.Globalization.CultureInfo.InvariantCulture, out var parsed))
					selector = parsed;
				else if (!string.IsNullOrWhiteSpace(text))
				{
					resource = text;
					return true;
				}
			}

			if (selector is sbyte or byte or short or ushort or int or uint or long)
			{
				number = Convert.ToInt64(selector, System.Globalization.CultureInfo.InvariantCulture);

				if (number != 0)
					return true;
			}

			error = "the tray icon selector must be a non-zero integer or a non-empty managed resource name";
			return false;
		}

		internal static string FileResourceName(string relative) =>
			FileResourcePrefix + relative.Replace('\\', '/');

		private static readonly JsonSerializerOptions serializerOptions = new()
		{ DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

		internal string Write() => JsonSerializer.Serialize(this, serializerOptions);

		internal static bool IsValidAssemblyVersion(string value)
		{
			var components = value?.Split('.');

			if (components == null || components.Length is < 2 or > 4)
				return false;

			foreach (var component in components)
				if (component.Length == 0
					|| component.Any(c => !char.IsAsciiDigit(c))
					|| !int.TryParse(component, System.Globalization.NumberStyles.None,
						System.Globalization.CultureInfo.InvariantCulture, out var number)
					|| number > 65534)
					return false;

			return true;
		}

		internal static AppManifest Read(string json) => Read(json, "The #App manifest JSON");

		private static AppManifest Read(string json, string context)
		{
			try
			{
				using var document = JsonDocument.Parse(json);

				if (document.RootElement.ValueKind != JsonValueKind.Object)
					throw new InvalidDataException("the root value must be an object");

				var manifest = document.RootElement.Deserialize<AppManifest>(serializerOptions)
					?? throw new InvalidDataException("the root object could not be read");
				manifest.ValidateStructure();
				return manifest;
			}
			catch (Exception ex)
			{
				throw new InvalidDataException($"{context} is invalid: {ex.Message}", ex);
			}
		}

		internal static AppManifest FromAssembly(Assembly asm)
		{
			if (asm == null)
				return null;

			var context = $"The #App manifest resource '{ResourceName}' in assembly '{asm.FullName ?? asm.GetName().Name}'";
			Stream stream;

			try
			{
				stream = asm.GetManifestResourceStream(ResourceName);
			}
			catch (Exception ex)
			{
				throw new InvalidDataException($"{context} could not be opened: {ex.Message}", ex);
			}

			if (stream == null)
				return null;

			try
			{
				AppManifest manifest;

				using (stream)
				using (var reader = new StreamReader(stream, new UTF8Encoding(false, true), true))
					manifest = Read(reader.ReadToEnd(), context);

				if (manifest.Icon != null)
					ValidateEmbeddedIcon(asm, IconResourceName, "application icon", context);

				if (manifest.HasCustomTrayIcon)
					ValidateEmbeddedIcon(asm, TrayIconResourceName, "custom tray icon", context);

				return manifest;
			}
			catch (InvalidDataException)
			{
				throw;
			}
			catch (Exception ex)
			{
				throw new InvalidDataException($"{context} could not be read: {ex.Message}", ex);
			}
		}

		private static void ValidateEmbeddedIcon(Assembly asm, string resourceName, string description, string context)
		{
			using var stream = asm.GetManifestResourceStream(resourceName);

			if (stream == null)
				throw new InvalidDataException($"{context} names {description} but its embedded payload is missing");

			using var buffer = new MemoryStream();
			stream.CopyTo(buffer);
			var bytes = buffer.ToArray();

			if (!IsStructurallyValidIcon(bytes))
				throw new InvalidDataException($"{context} names {description} but its embedded payload is not a structurally valid ICO file");
		}

		private static bool IsStructurallyValidIcon(ReadOnlySpan<byte> bytes)
		{
			if (bytes.Length < 6
				|| BinaryPrimitives.ReadUInt16LittleEndian(bytes) != 0
				|| BinaryPrimitives.ReadUInt16LittleEndian(bytes[2..]) != 1)
				return false;

			var count = BinaryPrimitives.ReadUInt16LittleEndian(bytes[4..]);
			var directoryEnd = 6L + (16L * count);

			if (count == 0 || directoryEnd > bytes.Length)
				return false;

			for (var index = 0; index < count; index++)
			{
				var entry = bytes.Slice(6 + (16 * index), 16);
				var size = BinaryPrimitives.ReadUInt32LittleEndian(entry[8..]);
				var offset = BinaryPrimitives.ReadUInt32LittleEndian(entry[12..]);

				if (size == 0 || offset < directoryEnd || (ulong)offset + size > (ulong)bytes.Length)
					return false;
			}

			return true;
		}

		private void ValidateStructure()
		{
			foreach (var (name, value) in new[]
			{
				("name", Name), ("title", Title), ("description", Description), ("configuration", Configuration),
				("company", Company), ("product", Product), ("copyright", Copyright), ("trademark", Trademark),
				("version", Version), ("icon", Icon), ("trayIcon", TrayIcon), ("trayIconResource", TrayIconResource), ("guiTheme", GuiTheme),
				("singleInstance", SingleInstance), ("hookMutexName", HookMutexName)
			})
				if (value is { Length: 0 })
					throw new InvalidDataException($"'{name}' must not be empty");

			if (Version != null && !IsValidAssemblyVersion(Version))
				throw new InvalidDataException("'version' must contain 2 to 4 decimal components from 0 to 65534");

			void ValidateCanonicalPath(string name, string value)
			{
				if (value == null)
					return;

				if (!string.Equals(value, AppResourcePath.Normalize(value), StringComparison.Ordinal))
					throw new InvalidDataException($"'{name}' must be a canonical relative path");
			}

			void ValidateIconPath(string name, string value)
			{
				ValidateCanonicalPath(name, value);

				if (value != null && !value.EndsWith(".ico", StringComparison.OrdinalIgnoreCase))
					throw new InvalidDataException($"'{name}' must be a .ico path");
			}

			ValidateIconPath("icon", Icon);

			if (HasCustomTrayIcon)
				ValidateCanonicalPath("trayIcon", TrayIcon);

			if (TrayIconSuppressed && TrayIcon != null)
				throw new InvalidDataException("'noTrayIcon' cannot be true with a custom 'trayIcon' source");

			if (TrayIconNumber == 0)
				throw new InvalidDataException("'trayIconNumber' must be positive or negative");

			if (TrayIconNumber != null && TrayIconResource != null)
				throw new InvalidDataException("'trayIconNumber' and 'trayIconResource' are mutually exclusive");

			if (TrayIconResource != null && string.IsNullOrWhiteSpace(TrayIconResource))
				throw new InvalidDataException("'trayIconResource' must be a non-empty managed resource name");

			if (!HasCustomTrayIcon && (TrayIconNumber != null || TrayIconResource != null))
				throw new InvalidDataException("a tray icon selector requires a custom 'trayIcon' source");

			if (GuiTheme != null && !Keysharp.Runtime.Script.TryNormalizeGuiTheme(GuiTheme, out _))
				throw new InvalidDataException("'guiTheme' must be Classic, System or Dark");

			if (SingleInstance != null && !new[] { "Force", "Ignore", "Prompt", "Off" }.Contains(SingleInstance, StringComparer.OrdinalIgnoreCase))
				throw new InvalidDataException("'singleInstance' must be Force, Ignore, Prompt or Off");

			if (Files == null)
				throw new InvalidDataException("'files' must be an array");

			var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			foreach (var file in Files)
			{
				if (string.IsNullOrEmpty(file))
					throw new InvalidDataException("'files' entries must be non-empty strings");

				if (!string.Equals(file, AppResourcePath.Normalize(file), StringComparison.Ordinal))
					throw new InvalidDataException($"'files' entry '{file}' must be a canonical relative path");

				if (file.IndexOfAny(['*', '?']) >= 0)
					throw new InvalidDataException($"'files' entry '{file}' must not contain wildcards");

				if (!files.Add(file))
					throw new InvalidDataException($"'files' contains duplicate path '{file}'");
			}
		}
	}
}
