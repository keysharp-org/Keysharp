#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#import KS { RealThread, Await }
#Include <assert>

; Critical parks a timer launch on the worker's queue. Removing its registration leaves an entry
; only another pump can discard. The worker must retry that pump so it can finish and publish its result.

worker := RealThread(WorkerBody)

Assert(worker.Task.Wait(15000) && Await(worker.Task) == 42, A_LineNumber)

WorkerBody()
{
	SetTimer(WorkerTick, 50)
	Sleep(150)              ; fires normally at least once
	Critical                ; from here its launches are refused and the queue is marked blocked
	Sleep(400)
	Critical "Off"
	SetTimer(WorkerTick, 0) ; the parked entry now names a registration that no longer exists
	return 42
}

WorkerTick()
{
}

FileAppend "pass", "*"
