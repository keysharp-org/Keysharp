#if LINUX
namespace Keysharp.Internals.DBus
{
	internal enum DBusBus
	{
		Session,
		System
	}

	/// <summary>
	/// Resolves the socket addresses of the two well-known buses. Tmds.DBus.Protocol 0.95 removed the Address
	/// helper type, so the env-var-then-conventional-path fallback lives here.
	/// </summary>
	internal static class DBusAddresses
	{
		[DllImport("libc", SetLastError = false)]
		private static extern uint getuid();

		internal static string For(DBusBus bus) => bus == DBusBus.System ? System : Session;

		internal static string Session
		{
			get
			{
				var env = Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS");
				return !string.IsNullOrEmpty(env) ? env : $"unix:path=/run/user/{getuid()}/bus";
			}
		}

		internal static string System
		{
			get
			{
				var env = Environment.GetEnvironmentVariable("DBUS_SYSTEM_BUS_ADDRESS");
				return !string.IsNullOrEmpty(env) ? env : "unix:path=/var/run/dbus/system_bus_socket";
			}
		}
	}
}
#endif
