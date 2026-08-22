#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#import KS { RealThread }

; A RealThread worker which leaves a permanently unrunnable entry on its own queue must still finish.
;
; Refusing a pseudo-thread launch -- here a timer refused because the worker is Critical -- parks the
; entry at the FRONT of the worker's queue and marks the queue blocked. Only pumping clears that mark:
; TryBeginPump and EndPump are its only writers. So a worker event loop which waits for the mark to
; clear instead of retrying the pump is waiting for a condition only it can produce.
;
; Clearing the timer while such an entry is parked is what makes it permanent: the entry survives, but
; the registration it names is gone, so nothing will ever re-serve it and only a pump can discover that
; and drop it. Without the retry the worker spins forever AFTER its body has already returned -- the
; completion is published in RunWorkerLoop's finally, which is past the loop -- so Wait() times out on
; work that finished long ago and Result stays empty. Both assertions below fail (by timeout) then.
;
; The main thread was never exposed to this: its pump is driven from outside by PostToUIThread, which
; does not consult the blocked gate.

worker := RealThread(WorkerBody)

if worker.Wait(15000) && worker.Result == 42
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

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
