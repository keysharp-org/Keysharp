using System.Runtime.CompilerServices;

namespace Keysharp.Runtime
{
	// Group lifetime roots so generated finally blocks stay small without allocating argument arrays.
	[PublicHiddenFromUser]
	public static class Lifetime
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void KeepAlive(object a, object b) { GC.KeepAlive(a); GC.KeepAlive(b); }

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void KeepAlive(object a, object b, object c) { KeepAlive(a, b); GC.KeepAlive(c); }

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void KeepAlive(object a, object b, object c, object d)
		{
			GC.KeepAlive(a);
			GC.KeepAlive(b);
			GC.KeepAlive(c);
			GC.KeepAlive(d);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void KeepAlive(object a, object b, object c, object d, object e) { KeepAlive(a, b, c, d); GC.KeepAlive(e); }

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void KeepAlive(object a, object b, object c, object d, object e, object f) { KeepAlive(a, b, c, d); KeepAlive(e, f); }

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void KeepAlive(object a, object b, object c, object d, object e, object f, object g) { KeepAlive(a, b, c, d); KeepAlive(e, f, g); }

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void KeepAlive(object a, object b, object c, object d, object e, object f, object g, object h) { KeepAlive(a, b, c, d); KeepAlive(e, f, g, h); }
	}
}
