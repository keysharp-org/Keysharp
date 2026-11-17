using Keysharp.Builtins;
namespace Keysharp.Internals.Scripting
{
	internal interface IFinalizerSafeDisposable : IDisposable { }

	/// <summary>Transfers an unreachable owner to its script's cleanup pump.</summary>
	internal sealed class GCCleanupSentinel(Any owner, DestructorPump pump)
	{
		internal Any Owner = owner;

		// The creating script's pump, which a later script does not take over.
		internal readonly DestructorPump Pump = pump;

		~GCCleanupSentinel() => Owner?.Collected(this);
	}

	internal sealed class DestructorPump
	{
		private readonly Script owner;
		private readonly Lock runGate = new();
		private readonly Lock _lock = new();
		private readonly Queue<Any> _q = new();
		private bool _pending = false;
		private int ready;
		private bool running;
		private bool stopped;

		// Objects of this script which have a sentinel that was neither collected nor detached.
		private int armed;

		internal DestructorPump(Script owner) => this.owner = owner;

		internal void Armed() => Interlocked.Increment(ref armed);

		internal void Disarmed() => Interlocked.Decrement(ref armed);

		/// <summary>Runs eligible __Delete callbacks once, then disposes resources, preserving the first error.
		/// Reacquired resources may be disposed again.</summary>
		internal static void RunDestructor(Any obj)
		{
			if (obj == null || Interlocked.Exchange(ref obj.cleaning, true))
				return;

			ExceptionDispatchInfo failure = null;

			try
			{
				if (obj.HasFinalizer && HasDeleteCalls(obj) && obj.ClaimDelete())
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

				failure = DisposeResources(obj, failure);
			}
			finally
			{
				Volatile.Write(ref obj.cleaning, false);
			}

			failure?.Throw();
		}

		public void Enqueue(Any obj)
		{
			var post = false;
			bool disposeOnly;

			lock (_lock)
			{
				disposeOnly = stopped || owner.IsDisposed;

				if (!disposeOnly)
				{
					_q.Enqueue(obj);
					post = !_pending;
					_pending = true;
				}
			}

			// After shutdown, only resources safe on this thread can be disposed.
			if (disposeOnly)
			{
				if (CanDisposeOnFinalizerThread(obj))
					DisposeResources(obj);

				return;
			}

			if (!post) return;

			// A finalizer may need the script's thread to finish, so wait on a pool thread.
			_ = ThreadPool.UnsafeQueueUserWorkItem(static pump =>
			{
				if (pump.PreparePending())
					pump.PostBatch();
			}, this, preferLocal: false);
		}

		// Only a complete finalizer pass becomes ready; a newer collection cannot append to this prefix under the lock.
		private bool PreparePending()
		{
			while (true)
			{
				var collection = GC.CollectionCount(0);
				GC.WaitForPendingFinalizers();
				lock (_lock)
				{
					if (collection != GC.CollectionCount(0)) continue;
					ready = _q.Count;
					_pending = false;
					return ready != 0 && !stopped;
				}
			}
		}

		private void PostBatch()
		{
			try { owner.MainEventScheduler.DispatchContext.Post(static state => ((DestructorPump)state).RunBatch(), this); }
			catch { }
		}

		/// <summary>Runs what collections handed over, once their finalizers finished. Returns whether there was any.</summary>
		public bool RunPendingDestructors()
		{
			_ = PreparePending();
			return RunBatch();
		}

		/// <summary>Collects released variables and resources abandoned by __Delete until no batch remains.</summary>
		internal void RunAtExit()
		{
			for (var pass = 0; pass < 4; pass++)
			{
				if (Volatile.Read(ref armed) > 0)
					GC.Collect();

				if (!RunPendingDestructors())
					return;
			}
		}

		// Serialize posted, exit and reentrant drains so one batch cannot be split.
		private bool RunBatch()
		{
			lock (runGate)
			{
				if (running) return false;
				running = true;
				List<Any> batch = null;
				try
				{
					lock (_lock) batch = DrainUnsafe(ready);
					// Reacquired state may already be in a direct cleanup on another thread.
					batch.RemoveAll(static obj => Interlocked.Exchange(ref obj.cleaning, true));

					if (batch.Count == 0) return false;

					if (!stopped && !owner.IsDisposed)
					{
						var deleting = batch.FindAll(static obj => HasDeleteCalls(obj) && obj.ClaimDelete());

						foreach (var any in (deleting.Count > 1 ? OrderOutsideIn(deleting) : deleting).SelectMany(WithInitializedFields))
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
					}

					// Dispose even resurrected owners after all callbacks, which can use other objects in the batch.
					foreach (var obj in batch)
						DisposeResources(obj);

					return true;
				}
				finally
				{
					if (batch != null)
						foreach (var obj in batch)
							Volatile.Write(ref obj.cleaning, false);
					running = false;
					bool post;
					lock (_lock) post = ready > 0 && !stopped;
					if (post) PostBatch();
				}
			}
		}

		internal void Stop()
		{
			lock (runGate)
			{
				List<Any> batch;

				lock (_lock)
				{
					stopped = true;
					batch = DrainUnsafe(_q.Count);
				}

				foreach (var obj in batch)
					DisposeResources(obj);
			}
		}

		private List<Any> DrainUnsafe(int count)
		{
			var batch = new List<Any>(count);

			while (count-- > 0)
				batch.Add(_q.Dequeue());

			ready = 0;
			return batch;
		}

		private static bool CanDisposeOnFinalizerThread(Any obj)
		{
			// An external subclass can override disposal without preserving its base's thread safety.
			if (obj.GetType().Assembly != typeof(Any).Assembly || obj is IDisposable and not IFinalizerSafeDisposable)
				return false;
			if (obj.nativeData == null)
				return true;
			var members = new List<Any>();
			obj.nativeData.AddMembers(members);
			foreach (var member in members)
				if (member.nativeData?.IsEmbeddedIn(obj) == true && !CanDisposeOnFinalizerThread(member))
					return false;
			return true;
		}

		// Keep typed storage available to the owner's Dispose; borrowed storage remains with its owner.
		private static ExceptionDispatchInfo DisposeResources(Any obj, ExceptionDispatchInfo failure = null)
		{
			try { (obj as IDisposable)?.Dispose(); }
			catch (Exception ex) { failure ??= ExceptionDispatchInfo.Capture(ex); }

			try
			{
				if (obj.nativeData is { NeedsDispose: true } data)
					data.Dispose();
			}
			catch (Exception ex) { failure ??= ExceptionDispatchInfo.Capture(ex); }
			// A disposer may refuse to release its resource without throwing.
			if (obj is not IDisposable) obj.HasFinalizer = false;
			return failure;
		}

		private static bool HasDeleteCalls(Any owner) => owner.HasDeleteCall
			|| owner.nativeData?.InitializedFields != null && WithInitializedFields(owner).Any(static any => any.HasDeleteCall);

		// An object's own __Delete runs before those of the nested structs its construction initialized, the last first.
		private static IEnumerable<Any> WithInitializedFields(Any owner)
		{
			yield return owner;

			if (owner.nativeData?.InitializedFields is { } fields)
				for (var i = fields.Count - 1; i >= 0; --i)
					foreach (var field in WithInitializedFields(fields[i]))
						yield return field;
		}

		private static List<Any> OrderOutsideIn(List<Any> batch)
		{
			var candidates = new HashSet<Any>(batch, ReferenceEqualityComparer.Instance);
			var visited = new HashSet<Any>(ReferenceEqualityComparer.Instance);
			var pending = new Stack<(Any Item, bool Exit)>();
			var result = new List<Any>(batch.Count);
			// Reverse postorder puts holders first; reversed roots keep unrelated objects in their original order.
			for (var i = batch.Count - 1; i >= 0; --i)
			{
				pending.Push((batch[i], false));
				while (pending.TryPop(out var entry))
				{
					var holder = entry.Item;
					if (entry.Exit)
					{
						if (candidates.Contains(holder)) result.Add(holder);
						continue;
					}
					if (!visited.Add(holder)) continue;
					pending.Push((holder, true));
					// Embedded structs belong to their holder; other non-candidates do not impose destructor ordering.
					foreach (var child in TryEnumerateChildren(holder))
						if (candidates.Contains(child) || child is Struct nested && nested.nativeData?.IsEmbeddedIn(holder) == true)
							pending.Push((child, false));
				}
			}
			result.Reverse();
			return result;
		}

		private static IEnumerable<Any> TryEnumerateChildren(Any a)
		{
			try { return a.GetEnumerableMembersOrEmpty(); } catch { return []; }
		}
	}

}
