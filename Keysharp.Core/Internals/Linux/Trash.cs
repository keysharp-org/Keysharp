#if LINUX
using System.ComponentModel;
using Microsoft.Win32.SafeHandles;

namespace Keysharp.Internals.Linux
{
	internal static class Trash
	{
		private const int O_PATH = 1 << 21, O_CLOEXEC = 1 << 19, AT_EMPTY_PATH = 0x1000, AT_REMOVEDIR = 0x200;
		private const uint STATX_MODE = 2, STATX_UID = 8, STATX_MNT_ID = 0x1000;

		// Linux defines these offsets for every architecture, unlike struct stat.
		[StructLayout(LayoutKind.Explicit, Size = 256)]
		private struct Statx
		{
			[FieldOffset(0)] internal uint Mask;
			[FieldOffset(20)] internal uint Uid;
			[FieldOffset(28)] internal ushort Mode;
			[FieldOffset(144)] internal ulong Mount;
		}

		[DllImport("libc", EntryPoint = "open", SetLastError = true)]
		private static extern int Open(string path, int flags);

		[DllImport("libc", EntryPoint = "unlinkat", SetLastError = true)]
		private static extern int UnlinkAt(SafeFileHandle directory, string name, int flags);

		[DllImport("libc", EntryPoint = "statx", SetLastError = true)]
		private static extern int GetStatus(SafeFileHandle fd, string path, int flags, uint mask, out Statx status);

		internal static void Empty(string path)
		{
			using var source = OpenPath(Path.GetFullPath(path)) ?? throw new DirectoryNotFoundException(path);
			var mount = Status(source).Mount;
			var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } xdg && Path.IsPathFullyQualified(xdg)
				? xdg : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");

			for (var anchor = dataHome; anchor != null; anchor = Path.GetDirectoryName(anchor))
			{
				using var home = OpenPath(anchor);
				if (home == null)
					continue;

				if (Status(home).Mount != mount)
					break;

				if (anchor == dataHome)
					EmptyUserTrash(Path.Combine(FdPath(home), "Trash"), mount, EffectiveUserId());
				return;
			}

			var prefix = mount.ToString(CultureInfo.InvariantCulture) + " ";
			var line = File.ReadLines("/proc/self/mountinfo").First(s => s.StartsWith(prefix, StringComparison.Ordinal));
			var root = line.Split(' ', 6)[4].Replace(@"\040", " ").Replace(@"\011", "\t").Replace(@"\012", "\n").Replace(@"\134", "\\");
			EmptyVolumeTrash(root, mount);
		}

		internal static void EmptyVolumeTrash(string root, ulong? expectedMount = null)
		{
			using var top = OpenPath(root, directory: true);
			if (top == null)
				throw new DirectoryNotFoundException(root);
			var mount = Status(top).Mount;
			if (expectedMount.HasValue && expectedMount != mount)
				throw new IOException($"The mount at {root} changed.");

			var topPath = FdPath(top);
			var uid = EffectiveUserId();
			var user = uid.ToString(CultureInfo.InvariantCulture);
			using var shared = OpenPath(Path.Combine(topPath, ".Trash"), directory: true);
			if (shared != null)
			{
				var status = Status(shared);
				if (status.Mount == mount && (status.Mode & (int)UnixFileMode.StickyBit) != 0)
					EmptyUserTrash(Path.Combine(FdPath(shared), user), mount, uid);
			}
			EmptyUserTrash(Path.Combine(topPath, ".Trash-" + user), mount, uid);
		}

		private static void EmptyUserTrash(string path, ulong mount, uint uid)
		{
			using var trash = OpenPath(path, directory: true);
			if (trash == null)
				return;

			var status = Status(trash);
			// A permission check would add nothing: every folder is pinned by handle and links are removed, not followed.
			if (status.Mount != mount || status.Uid != uid)
				return;

			using var files = OpenPath(Path.Combine(FdPath(trash), "files"), directory: true);
			using var info = OpenPath(Path.Combine(FdPath(trash), "info"), directory: true);
			if (files != null && info != null && Status(info).Mount == mount)
				EmptyDirectory(files, mount, FdPath(info));
		}

		private static void EmptyDirectory(SafeFileHandle folder, ulong mount, string info = null)
		{
			if (Status(folder).Mount != mount)
				throw new IOException("The trash contains a mounted directory.");

			foreach (var entry in new DirectoryInfo(FdPath(folder)).EnumerateFileSystemInfos())
			{
				using var child = entry is DirectoryInfo && (entry.Attributes & FileAttributes.ReparsePoint) == 0
					? OpenPath(entry.FullName, directory: true) : null;
				if (child != null)
					EmptyDirectory(child, mount);

				if (UnlinkAt(folder, entry.Name, child == null ? 0 : AT_REMOVEDIR) != 0)
				{
					var error = Marshal.GetLastPInvokeError();
					if (error == 2) // Another cleaner removed the payload and owns its metadata.
						continue;
					throw new IOException(entry.FullName, new Win32Exception(error));
				}

				// The info file reserves this name while another process recycles a new item.
				if (info != null)
					File.Delete(Path.Combine(info, entry.Name + ".trashinfo"));
			}
		}

		// Pin the directory before inspecting it. A rename or replacement of any parent cannot redirect a deletion.
		private static SafeFileHandle OpenPath(string path, bool directory = false)
		{
			// ARM and PowerPC place O_DIRECTORY and O_NOFOLLOW two bits below the generic Linux definitions.
			var directoryFlags = RuntimeInformation.ProcessArchitecture is Architecture.Arm or Architecture.Armv6 or Architecture.Arm64 or Architecture.Ppc64le
				? 0xc000 : 0x30000;
			var fd = Open(path, O_PATH | O_CLOEXEC | (directory ? directoryFlags : 0));
			if (fd >= 0)
				return new SafeFileHandle(fd, ownsHandle: true);

			var error = Marshal.GetLastPInvokeError();
			return error is 2 or 20 or 40 ? null : throw new IOException(path, new Win32Exception(error));
		}

		private static string FdPath(SafeFileHandle handle) => $"/proc/self/fd/{handle.DangerousGetHandle()}";

		private static Statx Status(SafeFileHandle handle)
		{
			const uint required = STATX_MODE | STATX_UID;
			if (GetStatus(handle, "", AT_EMPTY_PATH, required | STATX_MNT_ID, out var status) != 0 || (status.Mask & required) != required)
				throw new IOException("Cannot read trash file system metadata.");
			if ((status.Mask & STATX_MNT_ID) == 0)
			{
				// Kernels predating STATX_MNT_ID expose the same mount ID in fdinfo.
				var line = File.ReadLines($"/proc/self/fdinfo/{handle.DangerousGetHandle()}").First(s => s.StartsWith("mnt_id:", StringComparison.Ordinal));
				status.Mount = ulong.Parse(line.AsSpan(7).Trim(), CultureInfo.InvariantCulture);
			}

			return status;
		}
	}
}
#endif
