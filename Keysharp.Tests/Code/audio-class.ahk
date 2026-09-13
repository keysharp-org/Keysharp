#ErrorStdOut
#Warn All, StdOut
#Warn Experimental, Off
#NoTrayIcon
#import KS { Audio }
#Include <assert>

; The Audio class through real dynamic dispatch — which the C# tests bypass, so this is what proves the members
; and the nested types are reachable under the names a script types. Everything here works on a host with no
; sound card; anything that needs a real endpoint is guarded and skipped.

; ---- nested type identity ----------------------------------------------------------------------

; Qualified lookup resolves each nested type.
Assert(Audio.Clip is Class, A_LineNumber)
Assert(Audio.Device is Class, A_LineNumber)
Assert(Audio.Output is Class, A_LineNumber)
Assert(Audio.Playback is Class, A_LineNumber)

; ---- construction rules ------------------------------------------------------------------------

; Audio itself wraps the one audio system and has no instances.
Throws(() => Audio(), A_LineNumber)

; Factory-owned results cannot be constructed directly.
Throws(() => Audio.Clip(), A_LineNumber)
Throws(() => Audio.Device(), A_LineNumber)
Throws(() => Audio.Playback(), A_LineNumber)

; An Output is constructible and starts closed, touching nothing native.
out := Audio.Output()
; Type() of a nested built-in is the subject of a separate fix, so identity is asserted with `is`.
Assert(out is Audio.Output, A_LineNumber)
AssertEq(out.Status, "Closed", A_LineNumber)
AssertEq(out.VoiceLimit, 16, A_LineNumber)
AssertEq(out.VoicePolicy, "Oldest", A_LineNumber)
AssertEq(out.RequestedLatencyMilliseconds, 20, A_LineNumber)
Assert(out.IsFollowingDefault, A_LineNumber)
; A closed output reports blank for facts it does not yet have.
Assert(out.SampleRate == "" && out.Channels == "" && out.LatencyMilliseconds == "", A_LineNumber)
AssertEq(out.ActiveVoiceCount, 0, A_LineNumber)
AssertEq(out.DroppedPlayCount, 0, A_LineNumber)

; Bus gain and mute are settable before the output is ever opened, and survive a close, so a setter and its
; getter never disagree about what was asked for.
out.Volume := 40
AssertEq(out.Volume, 40, A_LineNumber)
out.Mute := true
AssertEq(out.Mute, true, A_LineNumber)
out.Mute := false
Throws(() => out.Volume := 150, A_LineNumber)

; Options are validated, not clamped.
Throws(() => Audio.Output("", 0), A_LineNumber)
Throws(() => Audio.Output("", 257), A_LineNumber)
Throws(() => Audio.Output("", 8, "Loudest"), A_LineNumber)
Throws(() => Audio.Output("", 8, "Oldest", 1), A_LineNumber)
Throws(() => Audio.Output("", 8, "Oldest", 5000), A_LineNumber)

; Policy tokens are accepted case-insensitively and reported in canonical casing.
AssertEq(Audio.Output("", 4, "roundrobin").VoicePolicy, "RoundRobin", A_LineNumber)
AssertEq(Audio.Output("", 4, "REJECT").VoicePolicy, "Reject", A_LineNumber)


; ---- clips from memory -------------------------------------------------------------------------

; 100 frames of 16-bit mono silence at 8 kHz.
buf := Buffer(200, 0)
clip := Audio.FromPcm(buf, 8000)
Assert(clip is Audio.Clip, A_LineNumber)
AssertEq(clip.FrameCount, 100, A_LineNumber)
AssertEq(clip.SampleRate, 8000, A_LineNumber)
AssertEq(clip.Channels, 1, A_LineNumber)
AssertEq(clip.SampleFormat, "Signed16", A_LineNumber)
AssertEq(Round(clip.DurationMilliseconds, 3), 12.5, A_LineNumber)

; Stereo and the other widths resolve, with canonical casing preserved.
AssertEq(Audio.FromPcm(Buffer(200, 0), 8000, 2).Channels, 2, A_LineNumber)
AssertEq(Audio.FromPcm(Buffer(100, 0), 8000, 1, "unsigned8").SampleFormat, "Unsigned8", A_LineNumber)
AssertEq(Audio.FromPcm(Buffer(300, 0), 8000, 1, "Signed24").FrameCount, 100, A_LineNumber)

; Every bound is checked before anything is allocated.
Throws(() => Audio.FromPcm(Buffer(200, 0), 4000), A_LineNumber)              ; rate too low
Throws(() => Audio.FromPcm(Buffer(200, 0), 300000), A_LineNumber)            ; rate too high
Throws(() => Audio.FromPcm(Buffer(200, 0), 8000, 3), A_LineNumber)           ; three channels
Throws(() => Audio.FromPcm(Buffer(200, 0), 8000, 1, "Signed12"), A_LineNumber)  ; unknown format
Throws(() => Audio.FromPcm(Buffer(0, 0), 8000), A_LineNumber)                ; empty
Throws(() => Audio.FromPcm(Buffer(201, 0), 8000), A_LineNumber)              ; partial frame
Throws(() => Audio.FromPcm("not a buffer", 8000), A_LineNumber)              ; wrong type

; ---- files -------------------------------------------------------------------------------------

; The managed WAV decoder is unconditional. Everything else depends on a codec this host already has, so the
; answer is whatever its decoder actually initialized with rather than a fixed list.
Assert(Audio.IsFormatSupported("wav"), A_LineNumber)
Assert(Audio.IsFormatSupported(".WAV"), A_LineNumber)
Assert(Audio.IsFormatSupported("mp3") == true || Audio.IsFormatSupported("mp3") == false, A_LineNumber)
; A container no platform decoder in this engine claims stays unsupported everywhere.
Assert(!Audio.IsFormatSupported("xyzzy"), A_LineNumber)

Throws(() => Audio.Load(""), A_LineNumber)
Throws(() => Audio.Load(A_Temp "/keysharp-audio-does-not-exist.wav"), A_LineNumber)

; A real round trip through a file: write a WAV by hand, load it, and check what came back.
wavPath := A_Temp "/keysharp-audio-test.wav"
WriteTestWav(wavPath, 8000, 100)
loaded := Audio.Load(wavPath)
AssertEq(loaded.SampleRate, 8000, A_LineNumber)
AssertEq(loaded.Channels, 1, A_LineNumber)
AssertEq(loaded.FrameCount, 100, A_LineNumber)

; A file that is not a WAV is refused by name rather than played as noise.
FileAppend("this is not audio", A_Temp "/keysharp-audio-bogus.wav")
Throws(() => Audio.Load(A_Temp "/keysharp-audio-bogus.wav"), A_LineNumber)

; ---- devices -----------------------------------------------------------------------------------

; Enumeration always answers with an Array, even on a host with no audio at all.
devices := Audio.Devices()
Assert(devices is Array, A_LineNumber)
Assert(Audio.Devices("Output") is Array, A_LineNumber)
Assert(Audio.Devices("Input") is Array, A_LineNumber)
Throws(() => Audio.Devices("Sideways"), A_LineNumber)
Throws(() => Audio.DefaultDevice("All"), A_LineNumber)      ; a role default is one device, not a set

; Reading IsRunning on every device exercises the session-enumeration path on Windows. A wrong COM vtable slot
; there is an access violation rather than an exception, so this is deliberately run against every endpoint
; instead of just the first.
for d in devices {
    running := d.IsRunning
    Assert(running == true || running == false, A_LineNumber)
    status := d.Status
    Assert(status == "Running" || status == "Idle" || status == "Unknown" || status == "Missing", A_LineNumber)
}

; The probes are Booleans and never prompt or open anything.
Assert(Audio.IsPlaybackSupported == true || Audio.IsPlaybackSupported == false, A_LineNumber)
Assert(Audio.IsOutputAvailable == true || Audio.IsOutputAvailable == false, A_LineNumber)
Assert(Audio.IsInputAvailable == true || Audio.IsInputAvailable == false, A_LineNumber)
Assert(Audio.IsDeviceChangeSupported == true || Audio.IsDeviceChangeSupported == false, A_LineNumber)

; Everything below needs a real endpoint, so it is skipped where there is none.
if (devices.Length > 0) {
    d := devices[1]
    Assert(d is Audio.Device, A_LineNumber)
    Assert(d.Id != "" && d.Name != "", A_LineNumber)
    Assert(d.Kind == "Output" || d.Kind == "Input", A_LineNumber)
    ; Refresh returns the receiver for a device that is still present.
    ; Refresh is the existence test: the receiver while the device is there, blank once it is gone.
    AssertEq(d.Refresh(), d, A_LineNumber)

    ; Volume round-trips through the 0-100 boundary, and is restored.
    before := d.Volume
    Assert(before >= 0 && before <= 100, A_LineNumber)
    Throws(() => d.Volume := 101, A_LineNumber)
    Throws(() => d.Volume := -1, A_LineNumber)
    d.Volume := before

    ; An exact Id resolves; an unknown one does not.
    Assert(Audio.Devices(d.Kind).Length > 0, A_LineNumber)
    Throws(() => Audio.Output("no-such-device-id-12345"), A_LineNumber)
}

; ---- playback ----------------------------------------------------------------------------------

if (Audio.IsPlaybackSupported && Audio.IsOutputAvailable) {
    o := Audio.Output("", 4, "RoundRobin")
    if (o.TryOpen()) {
        AssertEq(o.Status, "Open", A_LineNumber)
        Assert(o.SampleRate > 0 && (o.Channels == 1 || o.Channels == 2), A_LineNumber)
        Assert(o.Device is Audio.Device, A_LineNumber)

        ; Prepare is idempotent and observable.
        AssertEq(o.IsPrepared(clip), false, A_LineNumber)
        o.Prepare(clip)
        Assert(o.IsPrepared(clip), A_LineNumber)

        ; Thirty seconds of silence, long enough that the sound is still live for the control assertions
        ; below. The 12.5 ms clip above ends naturally within one render quantum, which would make every
        ; control refuse for the right reason and fail these checks for the wrong one.
        longClip := Audio.FromPcm(Buffer(8000 * 2 * 30, 0), 8000)
        o.Prepare(longClip)

        ; A play returns a stable handle whose identity does not rebind.
        p := o.Play(longClip)
        Assert(p is Audio.Playback, A_LineNumber)
        Assert(p.Status == "Queued" || p.Status == "Playing", A_LineNumber)
        Assert(p.IsPlaying, A_LineNumber)
        AssertEq(p.Volume, 100, A_LineNumber)
        AssertEq(p.Loop, false, A_LineNumber)
        Assert(p.Device is Audio.Device, A_LineNumber)
        Assert(p.DurationMilliseconds > 0, A_LineNumber)

        ; Controls are validated the same way everywhere.
        Throws(() => p.Volume := 200, A_LineNumber)
        Throws(() => p.Pan := -500, A_LineNumber)
        p.Volume := 50
        AssertEq(p.Volume, 50, A_LineNumber)
        p.Pan := -100
        AssertEq(p.Pan, -100, A_LineNumber)

        ; Stop is terminal and idempotent.
        p.Stop()
        p.Stop()
        AssertEq(p.Status, "Stopped", A_LineNumber)
        AssertEq(p.IsPlaying, false, A_LineNumber)
        ; A control write after the end is refused rather than silently ignored.
        Throws(() => p.PositionMilliseconds := 1, A_LineNumber)

        ; A short clip reaches its own end, and the natural reason wins over a later Stop.
        short := o.Play(clip)
        Sleep(120)
        AssertEq(short.Status, "Ended", A_LineNumber)
        short.Stop()
        AssertEq(short.Status, "Ended", A_LineNumber)

        ; Output-wide silence is valid whether or not anything is playing.
        o.StopAll()
        o.StopAll()

        ; A wrong argument type is a TypeError, not a blank.
        Throws(() => o.Play("a path, which explicit outputs do not take"), A_LineNumber)

        ; Binding to an explicit Device object rather than the default is what the manual GUI probe does,
        ; so it is exercised here too: the output must report that device rather than following the default.
        outDevices := Audio.Devices("Output")
        if (outDevices.Length > 0) {
            bound := Audio.Output(outDevices[1], 2)
            AssertEq(bound.IsFollowingDefault, false, A_LineNumber)
            if (bound.TryOpen()) {
                AssertEq(bound.Status, "Open", A_LineNumber)
                Assert(bound.Device is Audio.Device, A_LineNumber)
                AssertEq(bound.Device.Id, outDevices[1].Id, A_LineNumber)
            }
            bound.Dispose()
        }

        ; Close is reversible and keeps the prepared set; Dispose is terminal.
        o.Close()
        AssertEq(o.Status, "Closed", A_LineNumber)
        o.StopAll()                                  ; still valid while closed
        AssertEq(o.Play(clip), "", A_LineNumber)     ; a closed output refuses in bounds, not by raising
        o.Dispose()
        AssertEq(o.Status, "Disposed", A_LineNumber)
        Throws(() => o.Open(), A_LineNumber)
    }
}

; ---- sessions, meters, recording and device events -----------------------------------------------

; Every Phase 2 capability answers its probe with a Boolean and never prompts or opens a device.
Assert(Audio.IsSessionControlSupported == true || Audio.IsSessionControlSupported == false, A_LineNumber)
Assert(Audio.IsMeteringSupported == true || Audio.IsMeteringSupported == false, A_LineNumber)
Assert(Audio.IsMicrophoneCaptureSupported == true || Audio.IsMicrophoneCaptureSupported == false, A_LineNumber)
Assert(Audio.IsSystemAudioCaptureSupported == true || Audio.IsSystemAudioCaptureSupported == false, A_LineNumber)

; Sessions always answer with an Array, empty on a platform with no per-application model.
sessions := Audio.Sessions()
Assert(sessions is Array, A_LineNumber)
Assert(Audio.Sessions("no-such-process-name-here") is Array, A_LineNumber)
; A path is not a process selector.
Throws(() => Audio.Sessions("C:\Windows\System32\notepad.exe"), A_LineNumber)

if (Audio.IsSessionControlSupported && sessions.Length > 0) {
    s := sessions[1]
    Assert(s is Audio.Session, A_LineNumber)
    Assert(s.Id != "", A_LineNumber)
    Assert(s.Status == "Active" || s.Status == "Inactive" || s.Status == "Expired", A_LineNumber)
    Assert(s.IsVolumeSupported == true || s.IsVolumeSupported == false, A_LineNumber)
    ; Bulk control reports how many sessions it changed, and zero when nothing matched.
    AssertEq(Audio.SetApplicationVolume("no-such-process-name-here", 50), 0, A_LineNumber)
}

; A meter is configured without opening anything, and validates its interval.
Throws(() => Audio.Meter("", 5), A_LineNumber)
Throws(() => Audio.Meter("", 5000), A_LineNumber)
m := Audio.Meter()
Assert(m is Audio.Meter, A_LineNumber)
AssertEq(m.Status, "Stopped", A_LineNumber)
AssertEq(m.IntervalMilliseconds, 50, A_LineNumber)
AssertEq(m.Peak, "", A_LineNumber)          ; nothing observed yet
m.Dispose()
AssertEq(m.Status, "Disposed", A_LineNumber)

; A recorder validates every option before it touches a device.
Throws(() => Audio.Recorder("Sideways"), A_LineNumber)
Throws(() => Audio.Recorder("Microphone", A_Temp "/x.mp3"), A_LineNumber)     ; only WAV is written
Throws(() => Audio.Recorder("Microphone", "", "", 4000), A_LineNumber)        ; rate too low
Throws(() => Audio.Recorder("Microphone", "", "", 48000, 3), A_LineNumber)    ; three channels
Throws(() => Audio.Recorder("Microphone", "", "", 48000, 1, "Signed12"), A_LineNumber)
Throws(() => Audio.Recorder("Microphone", "", "", 48000, 1, "Signed16", 5), A_LineNumber)      ; chunk too small
Throws(() => Audio.Recorder("Microphone", "", "", 48000, 1, "Signed16", 100, -1), A_LineNumber)

rec := Audio.Recorder("Microphone", "", "", 48000, 1)
Assert(rec is Audio.Recorder, A_LineNumber)
AssertEq(rec.Status, "Ready", A_LineNumber)
AssertEq(rec.Source, "Microphone", A_LineNumber)
AssertEq(rec.Path, "", A_LineNumber)                 ; blank path means an in-memory recording
AssertEq(rec.SampleRate, 48000, A_LineNumber)
AssertEq(rec.SampleFormat, "Signed16", A_LineNumber)
AssertEq(rec.Peak, "", A_LineNumber)                 ; nothing captured yet
AssertEq(rec.Result, "", A_LineNumber)
Throws(() => rec.Stop(), A_LineNumber)               ; never started, so there is nothing to stop
rec.Dispose()

; Device change events use the shared EventHook contract, and the kind is validated.
Throws(() => Audio.OnDeviceChange(DeviceChanged, "Sideways"), A_LineNumber)
Throws(() => Audio.OnDeviceChange("not a function"), A_LineNumber)

if (Audio.IsDeviceChangeSupported) {
    hook := Audio.OnDeviceChange(DeviceChanged, "Output")
    Assert(hook.InProgress, A_LineNumber)
    AssertEq(hook.EndReason, "", A_LineNumber)
    Assert(Audio.Hooks.Length >= 1, A_LineNumber)
    hook.Stop()
    AssertEq(hook.InProgress, false, A_LineNumber)
    AssertEq(hook.EndReason, "Stopped", A_LineNumber)
}

DeviceChanged(ChangedHook, Kind, Device) {
}

; ---- cleanup -----------------------------------------------------------------------------------

try FileDelete(wavPath)
try FileDelete(A_Temp "/keysharp-audio-bogus.wav")

FileAppend "pass", "*"


; Writes a canonical 16-bit mono PCM WAV of silence, the same 44-byte header the engine's tone writer produces.
WriteTestWav(Path, Rate, Frames) {
    data := Frames * 2
    hdr := Buffer(44, 0)
    NumPut("UInt", 0x46464952, hdr, 0)        ; "RIFF"
    NumPut("UInt", 36 + data, hdr, 4)
    NumPut("UInt", 0x45564157, hdr, 8)        ; "WAVE"
    NumPut("UInt", 0x20746D66, hdr, 12)       ; "fmt "
    NumPut("UInt", 16, hdr, 16)
    NumPut("UShort", 1, hdr, 20)              ; PCM
    NumPut("UShort", 1, hdr, 22)              ; mono
    NumPut("UInt", Rate, hdr, 24)
    NumPut("UInt", Rate * 2, hdr, 28)
    NumPut("UShort", 2, hdr, 32)
    NumPut("UShort", 16, hdr, 34)
    NumPut("UInt", 0x61746164, hdr, 36)       ; "data"
    NumPut("UInt", data, hdr, 40)

    try FileDelete(Path)
    f := FileOpen(Path, "w")
    try {
        f.RawWrite(hdr, 44)
        f.RawWrite(Buffer(data, 0), data)
    } finally {
        f.Close()
    }
}
