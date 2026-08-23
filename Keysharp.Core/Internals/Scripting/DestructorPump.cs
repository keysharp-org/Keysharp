using Keysharp.Builtins;
#if WINDOWS
using Keysharp.Builtins.COM;
#endif
namespace Keysharp.Internals.Scripting
{
	internal static class DestructorPump
	{
		private static readonly Lock _lock = new();
		private static readonly Queue<Any> _q = new();       // enqueued by finalizers (strong refs -> resurrection)
		private static bool _pending = false;

		public static void Enqueue(Any obj)
		{
			var script = TheScript;
			if (script == null) return;

			bool post;

			lock (_lock)
			{
				_q.Enqueue(obj);     // keep strong ref: prevents collection until processed
				// Claim the right to post from INSIDE the lock. Enqueue runs on the GC finalizer thread, so the
				// old unsynchronized check-then-set let two threads both observe false and post twice. Worse, a
				// _pending stuck at true -- because its post targeted a scheduler that died with the previous
				// script -- permanently silenced every later Enqueue, so __Delete simply stopped running.
				post = !_pending;

				if (post)
					_pending = true;
			}

			if (!post) return;

			try
			{
				script.MainEventScheduler.DispatchContext.Post(_ => RunPendingDestructors(), null);
			}
			catch
			{
				// The post never happened, so release the claim; otherwise nothing would ever post again.
				lock (_lock) _pending = false;
			}
		}

		// Called on the script's logical main thread, serialized via its dispatch context -- but also directly
		// from ExitAppInternal, so the batch is a local rather than a shared static: two overlapping calls would
		// otherwise interleave into the same list and double-invoke (or lose) __Delete.
		public static void RunPendingDestructors()
		{
			var batch = new List<Any>();

			// Drain to a batch
			lock (_lock)
			{
				while (_q.Count > 0) batch.Add(_q.Dequeue());

				_pending = false;
			}

			if (batch.Count == 0) return;

			// Sort for outside-in (parents before children)
			var ordered = OrderOutsideIn(batch);

			// Now call __Delete in that order
			foreach (var any in ordered)
			{
				try
				{
					// Important: call script hook first, then native frees if you have any.
#if WINDOWS
					if (any is not ComValue)
#endif
						InvokeMeta(any, "__Delete");
					if (any is IDisposable idisp) idisp.Dispose();
				}
				catch { /* swallow per destructor semantics */ }
			}

			// Drop strong refs so GC can actually collect
			batch.Clear();
		}

		private static List<Any> OrderOutsideIn(List<Any> batch)
		{
			// Heuristic: if A references B (directly) and both are in the batch,
			// A should precede B (outside-in).
			// We build a graph using shallow enumeration of Keysharp-contained children.
			var set = new HashSet<Any>(batch);
			var edges = new Dictionary<Any, HashSet<Any>>(ReferenceEqualityComparer.Instance);
			foreach (var a in batch)
			{
				var children = TryEnumerateChildren(a); // see below
				foreach (var c in children)
				{
					if (ReferenceEquals(a, c)) continue;
					if (c is Any child && set.Contains(child))
					{
						if (!edges.TryGetValue(a, out var to)) edges[a] = to = new();
						to.Add(child);
					}
				}
			}

			// Topological order with parent->child edges means parents first.
			return TopoSort(batch, edges);
		}

		private static IEnumerable<object> TryEnumerateChildren(Any a)
		{
			// No parser changes: just lean on what you already expose.
			// 1) If Any wraps a map/array/object that Keysharp can enumerate, use that.
			// 2) If not, return empty (best-effort). Parent-first still helps many cases.
			try { return a.GetEnumerableMembersOrEmpty(); } catch { return System.Array.Empty<object>(); }
		}

		private static List<Any> TopoSort(List<Any> nodes, Dictionary<Any, HashSet<Any>> edges)
		{
			var incoming = new Dictionary<Any, int>(ReferenceEqualityComparer.Instance);
			foreach (var n in nodes) incoming[n] = 0;
			foreach (var kv in edges)
				foreach (var dst in kv.Value) incoming[dst]++;

			var q = new Queue<Any>(nodes.Where(n => incoming[n] == 0));
			var result = new List<Any>(nodes.Count);
			while (q.Count > 0)
			{
				var n = q.Dequeue();
				result.Add(n);
				if (!edges.TryGetValue(n, out var outs)) continue;
				foreach (var m in outs)
				{
					if (--incoming[m] == 0) q.Enqueue(m);
				}
			}

			// Fallback for cycles or unknown relationships: append remaining in stable order.
			if (result.Count != nodes.Count)
				foreach (var n in nodes)
					if (!result.Contains(n)) result.Add(n);

			return result;
		}
	}

}
