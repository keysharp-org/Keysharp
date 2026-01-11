using Timer = Keysharp.Core.Common.Threading.TimerWithTag;

namespace Keysharp.Core.Common.Threading
{
	public class ThreadConfigData
	{
		public ThreadConfigData() { }

		internal long controlDelay = 20L;
		internal CoordModeType coordModeCaret = CoordModeType.Client;
		internal CoordModeType coordModeMenu = CoordModeType.Client;
		internal CoordModeType coordModeMouse = CoordModeType.Client;
		internal CoordModeType coordModePixel = CoordModeType.Client;
		internal CoordModeType coordModeToolTip = CoordModeType.Client;
		internal long defaultMouseSpeed = 2L;
		internal bool detectHiddenText = true;
		internal bool detectHiddenWindows;
		internal Encoding fileEncoding = Encoding.Default;
		internal long keyDelay = 10L;
		internal long keyDelayPlay = -1L;
		internal long keyDuration = -1L;
		internal long keyDurationPlay = -1L;
		internal long mouseDelay = 10L;
		internal long mouseDelayPlay = -1L;
		internal long peekFrequency = 5L;
#if WINDOWS
		internal long regView = 64L;
#endif
		internal long sendLevel;
		internal SendModes sendMode = SendModes.Input;
		internal bool storeCapsLockMode = true;
		internal long titleMatchMode = 2L;
		internal bool titleMatchModeSpeed = true;
		internal long winDelay = 100L;

		public ThreadConfigData Clone() => (ThreadConfigData)MemberwiseClone();

		internal void CopyFromPrototypeConfigData()
		{
			var protoConfigData = Script.TheScript.AccessorData.threadConfigDataPrototype;
			controlDelay = protoConfigData.controlDelay;
			coordModeCaret = protoConfigData.coordModeCaret;
			coordModeMenu = protoConfigData.coordModeMenu;
			coordModeMouse = protoConfigData.coordModeMouse;
			coordModePixel = protoConfigData.coordModePixel;
			coordModeToolTip = protoConfigData.coordModeToolTip;
			defaultMouseSpeed = protoConfigData.defaultMouseSpeed;
			detectHiddenText = protoConfigData.detectHiddenText;
			detectHiddenWindows = protoConfigData.detectHiddenWindows;
			fileEncoding = protoConfigData.fileEncoding;
			keyDelay = protoConfigData.keyDelay;
			keyDelayPlay = protoConfigData.keyDelayPlay;
			keyDuration = protoConfigData.keyDuration;
			keyDurationPlay = protoConfigData.keyDurationPlay;
			mouseDelay = protoConfigData.mouseDelay;
			mouseDelayPlay = protoConfigData.mouseDelayPlay;
			peekFrequency = protoConfigData.peekFrequency;
#if WINDOWS
			regView = protoConfigData.regView;
#endif
			sendLevel = protoConfigData.sendLevel;
			sendMode = protoConfigData.sendMode;
			storeCapsLockMode = protoConfigData.storeCapsLockMode;
			titleMatchMode = protoConfigData.titleMatchMode;
			titleMatchModeSpeed = protoConfigData.titleMatchModeSpeed;
			winDelay = protoConfigData.winDelay;
		}
	}
	public class ThreadVariables
	{
		internal static readonly long DefaultPeekFrequency = 5L;
		internal static readonly long DefaultUninterruptiblePeekFrequency = 16L;

		// These describe the runtime state of the pseudo-thread
		//internal Task<object> task = null;
		internal bool task = false;
		internal bool isCritical = false;
		internal bool isPaused = false;
		internal bool allowThreadToBeInterrupted = true;
		internal int UninterruptibleDuration = 17;
		internal DateTime threadStartTime = DateTime.MinValue;
		internal Timer currentTimer;
		internal string defaultGui;
		internal Form dialogOwner;
		internal object eventInfo;
		internal IFuncObj hotCriterion;
		internal long hwndLastUsed = 0;
		internal long lastFoundForm;
		private Random randomGenerator;
		private StringBuilder regsb = null;
		internal long priority;
		internal int threadId;
		internal int lastError = 0;

		// These describe the configuration defaults of the pseudo-thread,
		// inherited from (and set by) the auto-execute section thread
		internal ThreadConfigData configData = new ();

		internal Random RandomGenerator
		{
			get => randomGenerator != null ? randomGenerator : randomGenerator = new Random((int)(DateTime.UtcNow.Ticks & 0xFFFFFFFF));
			set => randomGenerator = value;
		}

		internal StringBuilder RegSb => regsb != null ? regsb : regsb = new StringBuilder(1024);

		internal bool IsCriticalAndRunning()
		{
			var tempTask = task;
			//return tempTask != null && !tempTask.IsCompleted && !allowThreadToBeInterrupted && isCritical;
			return tempTask && !allowThreadToBeInterrupted && isCritical;
		}

		internal void WaitForCriticalToFinish()
		{
			//if (IsCriticalAndRunning())
			while (IsCriticalAndRunning())
			{
				//var tempTask = task;
				//tempTask?.Wait();
				Thread.Sleep(50);
			}
		}

		/// <summary>
		/// The fields in this function must be kept in sync with the fields declared above.
		/// </summary>
		public void Clear()
		{
			task = false;// null;
			isCritical = false;
			allowThreadToBeInterrupted = true;
			UninterruptibleDuration = 17;
			threadStartTime = DateTime.MinValue;
			currentTimer = null;
			defaultGui = null;
			dialogOwner = null;
			eventInfo = null;
			hotCriterion = null;
			hwndLastUsed = 0;
			lastFoundForm = 0L;
			randomGenerator = null;
			_ = (regsb?.Clear());
			priority = 0L;
			threadId = 0;
			lastError = 0;
		}

		public void Init()
		{
			task = false;// null;
			isCritical = false;
			isPaused = false;
			allowThreadToBeInterrupted = true;
			UninterruptibleDuration = Script.TheScript.uninterruptibleTime;
			threadStartTime = DateTime.MinValue;
			currentTimer = null;
			defaultGui = null;
			dialogOwner = null;
			eventInfo = null;
			hotCriterion = null;
			hwndLastUsed = 0;
			lastFoundForm = 0;
			randomGenerator = null;
			_ = (regsb?.Clear());
			priority = (long)A_Priority;
			threadId = 0;
			lastError = 0;
			// Instead of cloning the instance, copy the data because
			// allocating the memory for new instances is expensive
			configData.CopyFromPrototypeConfigData();
		}
	}
}