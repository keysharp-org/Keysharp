using Keysharp.Builtins;
namespace Keysharp.Internals.Scripting
{
	/// <summary>
	/// The finalizer of an <see cref="Any"/> which needs cleanup when it is collected (see <see cref="Any.HasFinalizer"/>).
	/// It and its owner refer to each other, so they become unreachable together, and its finalizer brings the owner back
	/// for the pump.
	/// </summary>
	internal sealed class GCCleanupSentinel(Any owner)
	{
		internal Any Owner = owner;
		private bool cleaning;
		private bool deleted;

		// The owner was given its __Delete while it was still in use, so its collection only disposes it.
		internal bool Deleted => Volatile.Read(ref deleted);

		// Whether this call is the one to give the owner its __Delete: only the first is.
		internal bool ClaimDelete() => !Interlocked.Exchange(ref deleted, true);

		// Whether this call is the one to clean the owner up, until EndCleanup: a second one, as from the owner's own
		// __Delete or another thread, would dispose of what the first is still using.
		internal bool BeginCleanup() => !Interlocked.Exchange(ref cleaning, true);
		internal void EndCleanup() => Volatile.Write(ref cleaning, false);

		~GCCleanupSentinel() => Owner?.Collected(this);
	}

	internal sealed class DestructorPump
	{
		private readonly Script owner;
		private readonly Lock runGate = new();
		private readonly Lock _lock = new();
		private readonly Queue<GCCleanupSentinel> _q = new();       // enqueued by finalizers (strong refs -> resurrection)
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

		/// <summary>
		/// Cleans <paramref name="obj"/> up for the using statement being left, as its collection would: its __Delete and
		/// those of a struct's nested structs, then its disposal, whatever they threw. An object which takes hold of
		/// something again is disposed again, by a later statement or its collection, without another __Delete. Each call
		/// is this thread's, so the first error is thrown from here once the rest has run.
		/// </summary>
		internal static void RunDestructor(Any obj)
		{
			// Without a sentinel the object has nothing to clean up.
			if (obj?.sentinel is not { } sentinel || !sentinel.BeginCleanup())
				return;

			ExceptionDispatchInfo failure = null;

			try
			{
				if (sentinel.ClaimDelete())
				{
					foreach (var any in WithInitializedFields(obj))
					{
						try
						{
							// As at a collection, an exiting script calls no __Delete, also when an earlier one exited it.
							if (any.HasDeleteCall && TheScript is { IsDisposed: false })
								InvokeMeta(any, "__Delete");
						}
						catch (Exception ex) { failure ??= ExceptionDispatchInfo.Capture(ex); }
					}
				}

				try { (obj as IDisposable)?.Dispose(); }
				catch (Exception ex) { failure ??= ExceptionDispatchInfo.Capture(ex); }
			}
			finally
			{
				sentinel.EndCleanup();
			}

			failure?.Throw();
		}

		public void Enqueue(GCCleanupSentinel collected)
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
					_q.Enqueue(collected);     // keep strong ref: prevents collection until processed
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
				DisposeNative(collected.Owner);
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
		public bool RunPendingDestructors()
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
					foreach (var entry in batch)
						DisposeNative(entry.Owner);

					return true;
				}

				// A batch of Buffers and Files, the usual one, has nothing to order.
				var anyDelete = batch.Exists(static entry => !entry.Deleted
					&& (entry.Owner.HasDeleteCall || entry.Owner is Struct { initializedFields: not null }
						&& WithInitializedFields(entry.Owner).Any(static any => any.HasDeleteCall)));

				// Outside-in (parents before children).
				foreach (var any in anyDelete ? OrderOutsideIn([.. batch.Where(static entry => !entry.Deleted).Select(static entry => entry.Owner)])
					.SelectMany(WithInitializedFields) : [])
				{
					// A __Delete may have exited the script, after which none is called.
					if (!any.HasDeleteCall || owner.IsDisposed)
						continue;

					try
					{
						// A drain can run inside a pumping call made in a try, whose catch must not swallow this error.
						using (Keysharp.Runtime.Flow.EnterUnguarded())
							InvokeMeta(any, "__Delete");
					}
					catch { /* swallow per destructor semantics */ }
				}

				// Only then is anything disposed, as a __Delete may use what another object of the batch holds. An object
				// one of them kept is disposed with the rest, as waiting to see which were kept would hold every batch
				// with a __Delete in it until a later collection.
				foreach (var entry in batch)
					DisposeNative(entry.Owner);

				return true;
			}
		}

		internal void Stop()
		{
			lock (runGate)
			{
				List<GCCleanupSentinel> batch;

				lock (_lock)
				{
					stopped = true;
					batch = DrainUnsafe();
				}

				foreach (var entry in batch)
					DisposeNative(entry.Owner);
			}
		}

		private List<GCCleanupSentinel> Drain()
		{
			lock (_lock)
				return DrainUnsafe();
		}

		private List<GCCleanupSentinel> DrainUnsafe()
		{
			var batch = new List<GCCleanupSentinel>(_q.Count);

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

		// A struct's own __Delete runs before those of the nested structs its construction initialized, the last first.
		private static IEnumerable<Any> WithInitializedFields(Any owner)
		{
			yield return owner;

			if (owner is Struct { initializedFields: { } fields })
				for (var i = fields.Count - 1; i >= 0; --i)
					foreach (var field in WithInitializedFields(fields[i]))
						yield return field;
		}

		private static List<Any> OrderOutsideIn(List<Any> batch)
		{
			// Heuristic: if A references B (directly) and both are in the batch,
			// A should precede B (outside-in).
			var set = new HashSet<Any>(batch, ReferenceEqualityComparer.Instance);
			var edges = new Dictionary<Any, HashSet<Any>>(ReferenceEqualityComparer.Instance);
			foreach (var a in batch)
				AddReferenceEdges(a, a, set, edges);

			// Topological order with parent->child edges means parents first.
			return TopoSort(batch, edges);
		}

		// What a struct's embedded structs hold, it holds itself, so they are looked through, and only they.
		private static void AddReferenceEdges(Any from, Any holder, HashSet<Any> set, Dictionary<Any, HashSet<Any>> edges)
		{
			foreach (var child in TryEnumerateChildren(holder))
			{
				if (ReferenceEquals(child, from))
					continue;

				if (set.Contains(child))
				{
					if (!edges.TryGetValue(from, out var to)) edges[from] = to = new(ReferenceEqualityComparer.Instance);
					to.Add(child);
				}
				else if (child is Struct nested && holder is Struct owner && nested.IsEmbeddedIn(owner))
					AddReferenceEdges(from, nested, set, edges);
			}
		}

		private static IEnumerable<Any> TryEnumerateChildren(Any a)
		{
			try { return a.GetEnumerableMembersOrEmpty(); } catch { return []; }
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

			// Fallback for cycles: append the rest, which still have incoming edges, in stable order.
			if (result.Count != nodes.Count)
				foreach (var n in nodes)
					if (incoming[n] > 0) result.Add(n);

			return result;
		}
	}

}
