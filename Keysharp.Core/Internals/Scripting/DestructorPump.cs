using Keysharp.Builtins;
namespace Keysharp.Internals.Scripting
{
	/// <summary>
	/// The finalizer of an <see cref="Any"/> which needs cleanup when it is collected (see <see cref="Any.HasFinalizer"/>).
	/// It and its owner refer to each other, so they become unreachable together, and its finalizer brings the owner back
	/// for the pump.
	/// </summary>
	internal sealed class GCCleanupSentinel(Any owner, bool again = false)
	{
		internal Any Owner = owner;
		// The owner was collected before and given its __Delete then (see Any.ArmAgain).
		internal readonly bool Again = again;

		~GCCleanupSentinel() => Owner?.Collected(this);
	}

	internal sealed class DestructorPump
	{
		private readonly Script owner;
		private readonly Lock runGate = new();
		private readonly Lock _lock = new();
		private readonly Queue<(Any Owner, bool Again)> _q = new();       // enqueued by finalizers (strong refs -> resurrection)
		private bool _pending = false;
		private bool stopped;
		private bool registered;

		internal DestructorPump(Script owner) => this.owner = owner;

		/// <summary>Whether any object ever enabled GC-time cleanup, i.e. whether exit needs a collection to find them.</summary>
		internal bool HasRegistrations => Volatile.Read(ref registered);

		// Read first, so the objects armed after the first leave the shared flag's cache line alone.
		internal void NoteRegistration()
		{
			if (!registered)
				Volatile.Write(ref registered, true);
		}

		public void Enqueue(Any obj, bool again = false)
		{
			bool post;
			bool disposeOnly;

			lock (_lock)
			{
				disposeOnly = stopped || owner.IsDisposed;

				if (disposeOnly)
				{
					post = false;
				}
				else
				{
					_q.Enqueue((obj, again));     // keep strong ref: prevents collection until processed
					// Claim the right to post from inside the lock. Enqueue runs on the GC finalizer thread, so the
					// old unsynchronized check-then-set let two threads both observe false and post twice. Worse, a
					// _pending stuck at true -- because its post targeted a scheduler that died with the previous
					// script -- permanently silenced every later Enqueue, so __Delete simply stopped running.
					post = !_pending;

					if (post)
						_pending = true;
				}
			}

			if (disposeOnly)
			{
				DisposeNative(obj);
				return;
			}

			if (!post) return;

			try
			{
				owner.MainEventScheduler.DispatchContext.Post(_ => RunPendingDestructors(), null);
			}
			catch
			{
				// The post never happened, so release the claim; otherwise nothing would ever post again.
				lock (_lock) _pending = false;
			}
		}

		// Called on the script's logical main thread via its dispatch context, but also directly from
		// ExitAppInternal, so runGate serializes the two: overlapping drains would otherwise split one batch
		// between them and double-invoke (or lose) __Delete. Returns whether there was anything to run.
		public bool RunPendingDestructors(bool exiting = false)
		{
			// A collection hands over what it found through many finalizers. Letting them all finish first keeps an object
			// with __Delete in one batch with what it holds, none of which may be disposed before that __Delete ran.
			GC.WaitForPendingFinalizers();

			lock (runGate)
			{
				var batch = Drain();

				if (batch.Count == 0) return false;

				if (stopped || owner.IsDisposed)
				{
					foreach (var (any, _) in batch)
						DisposeNative(any);

					return true;
				}

				var deleted = false;

				// Outside-in (parents before children), and each object's __Delete only the first time it is collected.
				foreach (var any in OrderOutsideIn([.. batch.Where(entry => !entry.Again).Select(entry => entry.Owner)]))
				{
					if (!any.HasDeleteCall)
						continue;

					deleted = true;

					try
					{
						// A drain can run inside a pumping call made in a try, whose catch must not swallow this error.
						using (Keysharp.Runtime.Flow.EnterUnguarded())
							InvokeMeta(any, "__Delete");
					}
					catch { /* swallow per destructor semantics */ }
				}

				// Only then is anything disposed, as a __Delete may use what another object of the batch holds. A __Delete
				// may also have stored its object or another of the batch, as AutoHotkey allows, so after one ran the
				// batch is disposed once it is collected again, unless the script is exiting.
				foreach (var (any, again) in batch)
				{
					if (deleted && !again && !exiting && any is IDisposable)
						any.ArmAgain();
					else
						DisposeNative(any);
				}

				return true;
			}
		}

		internal void Stop()
		{
			lock (runGate)
			{
				List<(Any Owner, bool Again)> batch;

				lock (_lock)
				{
					stopped = true;
					batch = DrainUnsafe();
				}

				foreach (var (any, _) in batch)
					DisposeNative(any);
			}
		}

		private List<(Any Owner, bool Again)> Drain()
		{
			lock (_lock)
				return DrainUnsafe();
		}

		private List<(Any Owner, bool Again)> DrainUnsafe()
		{
			var batch = new List<(Any Owner, bool Again)>(_q.Count);

			while (_q.Count > 0)
				batch.Add(_q.Dequeue());

			_pending = false;
			return batch;
		}

		private static void DisposeNative(Any obj)
		{
			try
			{
				if (obj is IDisposable disposable)
					disposable.Dispose();
			}
			catch { }
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
