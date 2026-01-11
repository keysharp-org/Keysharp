#if LINUX
using Tmds.DBus;

namespace Keysharp.Core.Linux
{
	[StructLayout(LayoutKind.Sequential)]
	public readonly struct AtSpiReference
	{
		public readonly string Name;
		public readonly ObjectPath Path;

		public AtSpiReference(string name, ObjectPath path)
		{
			Name = name;
			Path = path;
		}
	}

	[DBusInterface("org.a11y.atspi.Registry")]
	public interface IRegistry : IDBusObject
	{
		Task<AtSpiReference> GetDesktopAsync(int i);
	}

	[DBusInterface("org.a11y.atspi.Accessible")]
	public interface IAccessible : IDBusObject
	{
		Task<AtSpiReference> GetApplicationAsync();
	}

	[DBusInterface("org.a11y.atspi.Application")]
	public interface IApplication : IDBusObject
	{
		Task<int> GetProcessIdAsync();
	}

	[DBusInterface("org.a11y.atspi.Component")]
	public interface IComponent : IDBusObject
	{
		Task<AtSpiReference> GetAccessibleAtPointAsync(int x, int y, uint coordType);
	}

	internal static class AtSpiClient
	{
		private const string RegistryBusName = "org.a11y.atspi.Registry";
		private static readonly ObjectPath RegistryPath = new("/org/a11y/atspi/registry");
		private static readonly Lock initLock = new();
		private static Connection session;
		private static bool initFailed;

		private enum CoordType : uint
		{
			Screen = 0
		}

		private static Connection GetSession()
		{
			lock (initLock)
			{
				if (initFailed)
					return null;

				if (session != null)
					return session;

				try
				{
					var address = Environment.GetEnvironmentVariable("AT_SPI_BUS_ADDRESS");
					session = string.IsNullOrEmpty(address) ? new Connection(Tmds.DBus.Address.Session) : new Connection(address);
					session.ConnectAsync().GetAwaiter().GetResult();
				}
				catch
				{
					session = null;
					initFailed = true;
				}

				return session;
			}
		}

		private static bool IsRootPath(ObjectPath path) => path.ToString() == "/";

		internal static bool TryGetPidAtPoint(int x, int y, out uint pid)
		{
			pid = 0;

			var conn = GetSession();
			if (conn == null)
				return false;

			try
			{
				var registry = conn.CreateProxy<IRegistry>(RegistryBusName, RegistryPath);
				var desktop = registry.GetDesktopAsync(0).GetAwaiter().GetResult();
				if (IsRootPath(desktop.Path))
					return false;

				var component = conn.CreateProxy<IComponent>(desktop.Name, desktop.Path);
				var acc = component.GetAccessibleAtPointAsync(x, y, (uint)CoordType.Screen).GetAwaiter().GetResult();
				if (IsRootPath(acc.Path))
					return false;

				var accessible = conn.CreateProxy<IAccessible>(acc.Name, acc.Path);
				var appRef = accessible.GetApplicationAsync().GetAwaiter().GetResult();
				if (IsRootPath(appRef.Path))
					return false;

				var app = conn.CreateProxy<IApplication>(appRef.Name, appRef.Path);
				var appPid = app.GetProcessIdAsync().GetAwaiter().GetResult();
				if (appPid <= 0)
					return false;

				pid = (uint)appPid;
				return true;
			}
			catch
			{
				return false;
			}
		}
	}
}
#endif
