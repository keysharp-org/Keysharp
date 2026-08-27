using Keysharp.Builtins;
using ThreadMonitor = System.Threading.Monitor;

namespace Keysharp.Internals.Input.Keyboard
{
	/// <summary>
	/// Runs hook-originated #HotIf criteria on reusable background threads. Threads are added one at a time
	/// only when every existing worker is occupied, and no work is queued once the configured limit is reached.
	/// </summary>
	internal sealed class HotCriterionExecutor : IDisposable
	{
		private readonly Script owner;
		private readonly object growthGate = new();
		private readonly Worker[] workers;
		private int disposed;
		private int rejectionCount;
		private int workerCount;

		internal HotCriterionExecutor(Script owner, int maxWorkers)
		{
			this.owner = owner ?? throw new ArgumentNullException(nameof(owner));

			if (maxWorkers <= 0)
				throw new ArgumentOutOfRangeException(nameof(maxWorkers));

			workers = new Worker[maxWorkers];
		}

		internal int WorkerCount => Volatile.Read(ref workerCount);

		internal CriterionExecutionStatus Execute(KeysharpFunc criterion, HotCriterionEnum criterionType,
			string hotkeyName, object eventInfo, long deadlineTimestamp, out long value, out Exception error)
		{
			ArgumentNullException.ThrowIfNull(criterion);
			value = 0L;
			error = null;

			if (Volatile.Read(ref disposed) != 0)
				return CriterionExecutionStatus.Rejected;

			if (Stopwatch.GetTimestamp() >= deadlineTimestamp)
				return CriterionExecutionStatus.TimedOut;

			var worker = TryAcquireExisting(criterion, criterionType, hotkeyName, eventInfo);

			if (worker == null)
			{
				lock (growthGate)
				{
					if (Volatile.Read(ref disposed) != 0)
						return CriterionExecutionStatus.Rejected;

					if (Stopwatch.GetTimestamp() >= deadlineTimestamp)
						return CriterionExecutionStatus.TimedOut;

					// Another caller may have released or created a worker while this caller
					// was waiting for the growth lock.
					worker = TryAcquireExisting(criterion, criterionType, hotkeyName, eventInfo);

					if (worker == null)
					{
						var count = Volatile.Read(ref workerCount);

						if (count >= workers.Length)
							return CriterionExecutionStatus.Rejected;

						worker = new Worker(owner, count + 1);
						_ = worker.TryBegin(criterion, criterionType, hotkeyName, eventInfo);

						try
						{
							worker.Start();
						}
						catch (Exception ex)
						{
							error = ex;
							return CriterionExecutionStatus.Failed;
						}

						workers[count] = worker;
						Volatile.Write(ref workerCount, count + 1);
					}
				}
			}

			return worker.WaitForCompletion(deadlineTimestamp, out value, out error);
		}

		internal int RecordRejection()
			=> Volatile.Read(ref disposed) != 0 ? 0 : Interlocked.Increment(ref rejectionCount);

		private Worker TryAcquireExisting(KeysharpFunc criterion, HotCriterionEnum criterionType,
			string hotkeyName, object eventInfo)
		{
			var count = Volatile.Read(ref workerCount);

			for (var i = 0; i < count; i++)
				if (workers[i].TryBegin(criterion, criterionType, hotkeyName, eventInfo))
					return workers[i];

			return null;
		}

		public void Dispose()
		{
			if (Interlocked.Exchange(ref disposed, 1) != 0)
				return;

			lock (growthGate)
			{
				var count = Volatile.Read(ref workerCount);

				for (var i = 0; i < count; i++)
					workers[i].RequestStop();
			}
		}

		private sealed class Worker
		{
			private readonly Script owner;
			private readonly object gate = new();
			private readonly Thread thread;
			private Exception completedError;
			private long completedValue;
			private KeysharpFunc criterion;
			private HotCriterionEnum criterionType;
			private object eventInfo;
			private string hotkeyName;
			private WorkerState state;
			private bool stopRequested;

			internal Worker(Script owner, int index)
			{
				this.owner = owner;
				thread = new Thread(Run)
				{
					IsBackground = true,
					Name = $"Keysharp HotIf worker {index}"
				};
			}

			internal void Start() => thread.Start();

			internal bool TryBegin(KeysharpFunc newCriterion, HotCriterionEnum newCriterionType,
				string newHotkeyName, object newEventInfo)
			{
				lock (gate)
				{
					if (stopRequested || state != WorkerState.Idle)
						return false;

					criterion = newCriterion;
					criterionType = newCriterionType;
					hotkeyName = newHotkeyName;
					eventInfo = newEventInfo;
					state = WorkerState.Running;
					ThreadMonitor.PulseAll(gate);
					return true;
				}
			}

			internal CriterionExecutionStatus WaitForCompletion(long deadlineTimestamp,
				out long value, out Exception error)
			{
				value = 0L;
				error = null;

				lock (gate)
				{
					while (state == WorkerState.Running && !stopRequested)
					{
						var remainingTicks = deadlineTimestamp - Stopwatch.GetTimestamp();

						if (remainingTicks <= 0L)
						{
							state = WorkerState.Abandoned;
							ThreadMonitor.PulseAll(gate);
							return CriterionExecutionStatus.TimedOut;
						}

						_ = ThreadMonitor.Wait(gate, WaitMilliseconds(remainingTicks));
					}

					if (stopRequested)
						return CriterionExecutionStatus.Rejected;

					if (state != WorkerState.Completed)
						return CriterionExecutionStatus.TimedOut;

					value = completedValue;
					error = completedError;
					ClearRequest();
					state = WorkerState.Idle;
					ThreadMonitor.PulseAll(gate);
					return error == null ? CriterionExecutionStatus.Completed : CriterionExecutionStatus.Failed;
				}
			}

			internal void RequestStop()
			{
				lock (gate)
				{
					stopRequested = true;

					if (state != WorkerState.Idle)
						state = WorkerState.Abandoned;

					ClearRequest();
					ThreadMonitor.PulseAll(gate);
				}
			}

			private static int WaitMilliseconds(long remainingTicks)
			{
				var milliseconds = Math.Ceiling(remainingTicks * 1000.0 / Stopwatch.Frequency);
				return milliseconds >= int.MaxValue ? int.MaxValue : Math.Max(1, (int)milliseconds);
			}

			private void Run()
			{
				while (true)
				{
					KeysharpFunc currentCriterion;
					HotCriterionEnum currentCriterionType;
					string currentHotkeyName;
					object currentEventInfo;

					lock (gate)
					{
						while (state != WorkerState.Running && !stopRequested)
						{
							if (state == WorkerState.Abandoned)
							{
								ClearRequest();
								state = WorkerState.Idle;
								ThreadMonitor.PulseAll(gate);
							}

							_ = ThreadMonitor.Wait(gate);
						}

						if (stopRequested)
							return;

						currentCriterion = criterion;
						currentCriterionType = criterionType;
						currentHotkeyName = hotkeyName;
						currentEventInfo = eventInfo;
					}

					var result = 0L;
					Exception evaluationError = null;

					try
					{
						if (!owner.IsDisposed && !owner.hasExited)
						{
							result = HotkeyDefinition.EvaluateCriterion(
								owner, currentCriterion, currentCriterionType, currentHotkeyName, currentEventInfo);
						}
					}
					catch (Exception ex)
					{
						evaluationError = ex;
					}

					lock (gate)
					{
						if (stopRequested)
							return;

						if (state == WorkerState.Running)
						{
							completedValue = result;
							completedError = evaluationError;
							state = WorkerState.Completed;
							ThreadMonitor.PulseAll(gate);

							while (state == WorkerState.Completed && !stopRequested)
								_ = ThreadMonitor.Wait(gate);

							if (stopRequested)
								return;
						}

						if (state == WorkerState.Abandoned)
						{
							ClearRequest();
							state = WorkerState.Idle;
							ThreadMonitor.PulseAll(gate);
						}
					}
				}
			}

			private void ClearRequest()
			{
				criterion = null;
				hotkeyName = null;
				eventInfo = null;
				completedValue = 0L;
				completedError = null;
			}
		}

		private enum WorkerState : byte
		{
			Idle,
			Running,
			Completed,
			Abandoned
		}
	}

	internal enum CriterionExecutionStatus : byte
	{
		Completed,
		Failed,
		TimedOut,
		Rejected
	}

}
