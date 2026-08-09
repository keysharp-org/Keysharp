# Design: `Ks.Clipboard` class

Survey + proposal for folding the Keysharp-only clipboard functions into a class. Surveyed
2026-08-08 against `Keysharp.Core/Builtins/KeysharpEnhancements.cs`, `Builtins/Env.cs`,
`Builtins/Accessors.cs`, `Builtins/Images/Image.cs`,
`Internals/Platform/Services/ClipboardService.cs` and `Internals/Platform/IPlatformServices.cs`,
with `Keysharp/Scripts/FindText.ks` as the reference consumer and AHK v2's `A_Clipboard` /
`ClipboardAll` / `ClipWait` / `OnClipboardChange` as the compatibility oracle. Pre-0.1
contract: breaking changes are allowed, so the goal is a shape worth freezing.

Guiding principle inherited from the Image/Overlay review and the Monitor design:
*class APIs return result objects; ByRef out-parameters are reserved for the AHK-compatibility
globals.*

## 1. Current surface

**AHK-compatible** (unchanged by this proposal):

| Name | Where | Behavior |
|---|---|---|
| `A_Clipboard` | [Accessors.cs:105](../Keysharp.Core/Builtins/Accessors.cs#L105) | get/set text; assigning a `ClipboardAll` restores every format |
| `A_ClipboardTimeout` | [Accessors.cs:1815](../Keysharp.Core/Builtins/Accessors.cs#L1815) | Windows raw-Win32 open timeout |
| `ClipboardAll(Data?, Size?)` | [Env.cs:560](../Keysharp.Core/Builtins/Env.cs#L560) | `Buffer` subclass holding a serialized all-format blob |
| `ClipWait(timeout?, waitFor?)` | [Env.cs:58](../Keysharp.Core/Builtins/Env.cs#L58) | poll until text/files (0) or anything (1) |
| `OnClipboardChange(cb, addRemove?)` | [Env.cs:271](../Keysharp.Core/Builtins/Env.cs#L271) | callback receives 0 = empty, 1 = text, 2 = other |

**Keysharp-only**, scattered across three homes:

| Name | Where |
|---|---|
| `Ks.CopyImageToClipboard(filename, options?)` | [KeysharpEnhancements.cs:33](../Keysharp.Core/Builtins/KeysharpEnhancements.cs#L33) |
| `Ks.IsClipboardEmpty()` | [KeysharpEnhancements.cs:77](../Keysharp.Core/Builtins/KeysharpEnhancements.cs#L77) |
| `Image.FromClipboard()` | [Image.cs:310](../Keysharp.Core/Builtins/Images/Image.cs#L310) |

Everything funnels through one internal service, resolved once per process
([IPlatformServices.cs:226](../Keysharp.Core/Internals/Platform/IPlatformServices.cs#L226)):

```
IClipboard { GetText, SetText, IsEmpty, ChangeType, GetImage, SetImage,
             CaptureAll, RestoreAll, Subscribe }
```

with four implementations: `WindowsClipboard` (raw Win32 + WinForms), `EtoClipboard`
(macOS, and Linux X11 / data-control Wayland), `WaylandBackendClipboard` (Cinnamon/Muffin
shell extension), and `RecoveringLinuxClipboard` (the router between the last two).
**This layering is right and the class must not disturb it** — the class is a script-facing
facade over `Platform.Clipboard`, exactly as `Monitor` is over `IScreen`.

## 2. Findings

**F1 — Clipboard access is inconsistently marshalled to the UI thread.** `A_Clipboard`
get/set ([Accessors.cs:112](../Keysharp.Core/Builtins/Accessors.cs#L112),
[:117](../Keysharp.Core/Builtins/Accessors.cs#L117)) and `ClipWait`
([Env.cs:90](../Keysharp.Core/Builtins/Env.cs#L90)) go through `Script.InvokeOnUIThread`.
Four other entry points call `Platform.Clipboard.*` **directly**:

| Call site | Operation |
|---|---|
| [KeysharpEnhancements.cs:60,65](../Keysharp.Core/Builtins/KeysharpEnhancements.cs#L60) | `SetImage` |
| [KeysharpEnhancements.cs:77](../Keysharp.Core/Builtins/KeysharpEnhancements.cs#L77) | `IsEmpty` |
| [Image.cs:315](../Keysharp.Core/Builtins/Images/Image.cs#L315) | `GetImage` |
| [Env.cs:576](../Keysharp.Core/Builtins/Env.cs#L576) | `CaptureAll` (the `ClipboardAll()` ctor) |

The WinForms clipboard requires an STA thread and the GTK clipboard is UI-thread-only, so
any of these called from a real thread (`Ks.RealThread`) is a latent failure. `InvokeOnUIThread`
already short-circuits when the caller is on the main thread
([Script.cs:885](../Keysharp.Core/Runtime/Script/Script.cs#L885)), so marshalling costs
nothing in the common case. **The marshalling belongs in one place, not at four of six call
sites.**

**F2 — Windows `IsEmpty` reflects over `DataFormats` *field names*, not format values.**

```csharp
typeof(DataFormats).GetFields(...).Select(f => f.Name)   // ClipboardService.cs:762
...
public bool IsEmpty => !dataFormats.Any(Clipboard.ContainsData);   // :841
```

`DataFormats.Html` is a field *named* `Html` whose *value* is `"HTML Format"`; likewise
`Rtf` → `"Rich Text Format"`, `Dib` → `"DeviceIndependentBitmap"`, `CommaSeparatedValue` →
`"CSV"`, `OemText` → `"OEMText"`. The probe therefore asks for formats that do not exist and
never sees the ones that do. It is also blind to every registered/custom format by
construction (the list is a fixed set of ~20 names). Consequences beyond
`IsClipboardEmpty()` itself:

- `ChangeType()` ([:843](../Keysharp.Core/Internals/Platform/Services/ClipboardService.cs#L843))
  returns `0` for such a clipboard, so **`OnClipboardChange` reports "clipboard is now empty"
  when it is not**.
- `ClipWait(, 1)` ("wait for data of any kind") waits forever for a clipboard holding only a
  custom format.

`EnumClipboardFormats` + `GetClipboardFormatName` answers exactly, in one pass, and is the
same enumeration the new `Formats` member needs anyway.

**F3 — `CopyImageToClipboard` re-implements a subset of `Image`'s loading.** It carries its
own `w`/`h`/`Icon` option-string parser and a `.cur` special case
([KeysharpEnhancements.cs:33-69](../Keysharp.Core/Builtins/KeysharpEnhancements.cs#L33-L69)),
duplicating `Image.FromFile(path, w?, h?, iconNumber?)`, which expresses the same three
options as real parameters. This is another font-spec-style mini-language of exactly the kind
D4 of the Image/Overlay review removed.

**F4 — It cannot take an image the script already holds.** It takes a *filename*. The shipped
`FindText.ks` therefore writes a temp PNG, copies *that*, and schedules a deferred delete —
26 lines of workaround with a comment explaining itself
([FindText.ks:1620-1662](../Keysharp/Scripts/FindText.ks#L1620-L1662)):

> `; CopyImageToClipboard takes a file, so the picture goes out through a temp PNG.`

The engine has had `Platform.Clipboard.SetImage(Bitmap)` the whole time; only the script-facing
signature forced the round trip through disk.

**F5 — Structured content is only readable by accident.** Windows `GetText` falls through
UnicodeText → Text → Html → Rtf → SymbolicLink → OemText → CSV → FileDrop
([ClipboardService.cs:783-811](../Keysharp.Core/Internals/Platform/Services/ClipboardService.cs#L783-L811))
and returns whichever hits, as a string. So copying files in Explorer makes `A_Clipboard` a
newline-joined path list with nothing to distinguish it from text that happens to contain
paths, and HTML arrives with its raw `Version:0.9\r\nStartHTML:…` CF_HTML header attached.
There is no `Files`, no `Html`, no format list, and no way to read a private format —
this is the entire reason WinClip exists
([library-survey.md:51](library-survey.md)).

**F6 — Only one format can be written per operation, and only one at a time.** Every backend
write replaces the whole clipboard (`EmptyClipboard` on Windows,
[ClipboardService.cs:824](../Keysharp.Core/Internals/Platform/Services/ClipboardService.cs#L824)).
So "copy as HTML with a plain-text fallback" — the single most-requested rich-clipboard
operation — is not expressible, and two successive writes fire two `OnClipboardChange`
notifications for one logical copy.

**F7 — `ClipboardAll` save/restore is undiscoverable.** `saved := ClipboardAll()` … then
`A_Clipboard := saved`, which works because the `A_Clipboard` setter runtime-type-tests for it
([Accessors.cs:119](../Keysharp.Core/Builtins/Accessors.cs#L119)). AHK-compatible and correct,
but nothing about `A_Clipboard :=` suggests it also accepts a blob.

## 3. What users actually want

Ranked by observed demand in the AHK ecosystem (WinClip, ClipboardAll wrappers, clipboard
managers — the ROADMAP's own M4 script corpus lists "clipboard manager" as a candidate):

| # | Want | Why | Covered today |
|---|---|---|---|
| 1 | **Files get/set** | file-manager scripts, batch rename, drop-target replacements | read-only and by accident (F5) |
| 2 | **HTML / RTF get+set** | paste formatted text into Word/Outlook; scrape a copied link's markup | no (F5) |
| 3 | **Multi-format atomic write** | rich text *with* a plain fallback; one change notification | no (F6) |
| 4 | **A named home + `Clear`/`IsEmpty`/`Has`** | discoverability; `IsClipboardEmpty()` is unguessable | partly |
| 5 | **Raw named-format get/set** | app interop: Excel `Biff12`, VS `MSDEVColumnSelect` column paste | no |
| 6 | **Image get/set from an object** | screenshot tools, FindText (F4) | file-only write |
| 7 | Clipboard history / cloud clipboard | "paste the item before last" | no — see §7 |
| 8 | X11 PRIMARY selection | middle-click paste on Linux | no — see §7 |

## 4. Support matrix (defined first, as the roadmap requires)

`full` = implemented and expected to work; `partial` = works on a subset; `none` = an honest
`OSError` naming the limitation; `unknown` = cannot be verified from this host.
Columns: **Win** = Windows; **X11** = Linux X11 (Eto); **Wl-dc** = Wayland with a
data-control handler (KWin/wlroots, Eto); **Wl-ext** = Wayland via the Cinnamon/Muffin shell
extension; **mac** = macOS (Eto — never compiled from this host, see §8).

| Member | Win | X11 | Wl-dc | Wl-ext | mac |
|---|---|---|---|---|---|
| `Text` get/set | full | full | full | full | full |
| `IsEmpty`, `Clear` | full *(after F2)* | full | full | full | full |
| `Image` get/set | full | full | full | full | unknown |
| `All` get/set | full | full | full | **partial** — one MIME restorable | unknown |
| `Formats` | full — `EnumClipboardFormats` | full — `clip.Types` | full | full — `GetClipboardMimetypes` | unknown |
| `GetData(fmt)` | full — `GlobalLock` (already in `CaptureAll`) | full — `clip.GetData` | full | full — `GetClipboardContent` | unknown |
| `Files` get | full — `CF_HDROP` | full — `clip.Uris` | full | full — `text/uri-list` | unknown |
| `Files` set | full — `SetFileDropList` | full — `clip.Uris =` | full | full | unknown |
| `Html` get/set | full — needs a CF_HTML header codec | full — `clip.Html` | full | full — `text/html` | unknown |
| `Rtf` get/set | full — `DataFormats.Rtf` | full — raw `text/rtf` | full | full | unknown |
| `Has(kind)` | full | full | full | full | unknown |
| `Set(bag)` multi-format | full — one `IDataObject` | **unknown** — Eto accumulate-vs-replace unverified | unknown | **none** — single-MIME source | unknown |
| `Wait`, `OnChange` | full | full | full | full | unknown |

Two cells decide the honest story. **`Set(bag)` is `none` on the Wayland extension backend**
for the same reason `ClipboardAll` is already `partial` there: `MetaSelectionSourceMemory`
advertises exactly one MIME type
([ClipboardService.cs:701](../Keysharp.Core/Internals/Platform/Services/ClipboardService.cs#L701)).
It must degrade to "write the most useful single representation", reusing `RestoreAll`'s
existing preference order, and say so. And **the whole macOS column is `unknown`**, not
because the code is hard but because `OSX` is only defined on a macOS build host — the same
debt `Monitor` and `WinEvent` already carry.

## 5. Proposed API

### D1 — One `Ks.Clipboard` class; the AHK globals stay exactly as they are

`A_Clipboard`, `A_ClipboardTimeout`, `ClipboardAll`, `ClipWait` and `OnClipboardChange` are
the compat surface and do not move, change or gain parameters. The class is additive and
Keysharp-only, matching how `Monitor`/`MonitorGet*` and `Image`/`ImageSearch` already coexist.

C# type `KeysharpClipboard` with `[UserDeclaredName("Clipboard")]`, nested in `Ks` — the
`KeysharpImage` precedent, needed for the same reason (`System.Windows.Forms.Clipboard` and
`Eto.Forms.Clipboard` are both in scope inside that file).

### D2 — It is a static-only class, not an instantiable one

There is exactly one clipboard per session, so every member is `[Static]` (static properties
use the `staticget_X`/`staticset_X` convention already used by `Ks.WinEvent.Paused`,
[WinEvents.cs:96](../Keysharp.Core/Builtins/WinEvents.cs#L96)). `Clipboard()` raises an error
rather than returning a useless instance. This is the one structural difference from
`Image`/`Overlay`/`Monitor`, and it is the right one: those wrap *a* thing, this wraps *the*
thing. See D12 for the one future that would want instances.

```ahk
#import Ks { Clipboard }
```

### D3 — Surface

```ahk
; ---- typed content (the portable surface) ----------------------------------
Clipboard.Text          ; get/set String   — identical to A_Clipboard
Clipboard.Image         ; get/set Image    — see D5
Clipboard.Files         ; get/set Array of paths
Clipboard.Html          ; get/set String   — CF_HTML header handled for you (D7)
Clipboard.Rtf           ; get/set String

; ---- state -----------------------------------------------------------------
Clipboard.IsEmpty       ; get Boolean      — replaces Ks.IsClipboardEmpty()
Clipboard.Formats       ; get Array of platform-native format names
Clipboard.Has(kind)     ; Boolean; kind = "Text"|"Image"|"Files"|"Html"|"Rtf"
                        ;                 or a platform-native format name
Clipboard.Clear()

; ---- raw / escape hatch ----------------------------------------------------
Clipboard.GetData(format)   ; => Buffer, by platform-native format name
Clipboard.Set(bag)          ; atomic multi-format write; see D6

; ---- save & restore --------------------------------------------------------
Clipboard.All           ; get/set ClipboardAll  — the ClipboardAll()/A_Clipboard:= pair

; ---- waiting & events ------------------------------------------------------
Clipboard.Wait(timeout?, waitFor?)     ; as ClipWait, plus named kinds (D9)
Clipboard.OnChange(callback, count?)   ; => hook object (D10)
```

### D4 — Absent means `""`, uniformly

Every typed getter and `GetData` returns `""` when the clipboard does not hold that content,
never a null object and never an empty `Array`/`Buffer`. `""` is falsy, so
`if (files := Clipboard.Files)` and `if (img := Clipboard.Image)` are the idiom, and it matches
what `Image.FromClipboard()` already returns
([Image.cs:316](../Keysharp.Core/Builtins/Images/Image.cs#L316)). Uniformity is worth more here
than the convenience of iterating an always-present empty array.

`Clipboard.Text := ""` clears the clipboard, exactly as `A_Clipboard := ""` does today.

### D5 — `CopyImageToClipboard` becomes `Clipboard.Image` and is removed

**This is the direct answer to the question that prompted this design.** The operation belongs
on `Clipboard`, not on `Image`, and it should be a property, not a method:

```ahk
Clipboard.Image := Image.FromFile("logo.png")          ; an Image the script holds
Clipboard.Image := "logo.png"                          ; a path
Clipboard.Image := "HBITMAP:" hbm                      ; a native handle
Clipboard.Image := Image.FromRect(0,0,800,600).Scale(0.5)   ; chains
img := Clipboard.Image                                 ; round trip
```

The setter accepts the same union `Image(source)` accepts — `Image` | path string | bitmap
handle | `"HBITMAP:…"` — because `LoadFromSource`
([Image.cs:1702](../Keysharp.Core/Builtins/Images/Image.cs#L1702)) already defines that union
as the Image contract and narrowing it here would be a gratuitous inconsistency. The
`w`/`h`/`Icon` option string does **not** come along: `Image.FromFile(path, w, h, iconNumber)`
already expresses it with real parameters (F3).

Reasoning for *Clipboard* over *Image*, in order of weight:

1. **Direction of ownership.** Every other clipboard write will live on `Clipboard`
   (`Text`, `Files`, `Html`, `Set`). Putting the image write on `Image` splits one concern
   across two classes on the basis of the payload type, which does not generalize — nobody
   wants `String.ToClipboard()`.
2. **It is not an image operation.** `Image`'s methods transform or emit pixels
   (`Scale`, `Save`, `ToBitmap`). "Publish to a system-wide shared resource" is a different
   axis, and the class already refuses that split correctly (`Save(file)` is on Image because
   a file is a pixel *sink*; the clipboard is a *format-negotiating* sink).
3. **It generalizes to the multi-format case.** `Clipboard.Set({Image: img, Files: [...]})`
   has a home; `img.ToClipboard()` does not.
4. **Property, not method**, because it makes the getter and setter one member and reads as
   state rather than an action — the same call the Monitor design makes for `Brightness`.

**`Image.ToClipboard()` is therefore NOT added**, retracting the reservation made in R7 of
`docs/api-review-image-overlay.md`; `Clipboard.Image := img` covers it and a second spelling
would make three ways to do one thing.

**`Image.FromClipboard()` is kept**, as an explicitly documented alias of the
`Clipboard.Image` getter. It is the one asymmetry in the design and it is justified: it belongs
to a factory family (`FromFile`/`FromWindow`/`FromMonitor`/`FromBitmap`/`FromBuffer`) that a
user scans as a set, and removing one member from that set to force a different class is worse
than one labeled alias. Both spellings call the same code path.

If the owner prefers a method to a property, the name is `Clipboard.SetImage(src)` /
`Clipboard.GetImage()` — but the property is the recommendation.

### D6 — One atomic multi-format writer: `Clipboard.Set(bag)`

```ahk
Clipboard.Set({ Text: "Hello", Html: "<b>Hello</b>" })          ; object literal
Clipboard.Set(Map("HTML Format", buf, "Biff12", excelBuf))      ; Map for non-identifier names
```

Keys are canonical kinds (D8) or platform-native format names; values are `String`, `Buffer`,
`Image`, or `Array` (for `Files`). The whole bag is published in **one** clipboard transaction,
so it fires one `OnClipboardChange` and the formats do not overwrite each other (F6).

Both an object and a `Map` are accepted because AHK object-literal keys must be identifiers —
`{"HTML Format": x}` is not writable — and a `Map` is the only way to spell an arbitrary
native format name. That is a real language constraint, not indecision.

A single-format raw setter (`SetData(format, data)`) is deliberately **not** added: it is
`Set` with one entry, and the typed properties cover the common single-format writes.

### D7 — `Html` is the portable form, with the CF_HTML codec on the Windows side

Windows stores HTML as `"HTML Format"` with a byte-offset header
(`Version:0.9 / StartHTML:… / StartFragment:…`). `Clipboard.Html` gets and sets the **fragment**
— the actual markup — and the Windows backend builds and parses the header. Scripts that want
the raw bytes use `Clipboard.GetData("HTML Format")`. This is the difference between "we have
an HTML property" and "we have a working HTML property", and it is the single most common
reason AHK users reach for WinClip.

### D8 — `Formats`/`GetData` are platform-native; the typed properties are the portable layer

`Clipboard.Formats` returns whatever the platform calls its formats — `"HTML Format"`,
`"FileDrop"`, `"Rich Text Format"` on Windows (the names `DataFormats.GetFormat` /
`GetClipboardFormatName` report, not the `CF_*` constants); `"text/html"`, `"text/uri-list"`
on Linux/macOS — with no normalization, and `GetData` takes the same names. **Portability lives in the typed
properties; the raw layer is an escape hatch, documented as non-portable** the way `DllCall`
is. Normalizing format names would invent a third vocabulary that matches no platform's
documentation and would silently drop the private formats that are the whole point.

`Has(kind)` bridges the two: a canonical kind name (`"Text"`, `"Image"`, `"Files"`, `"Html"`,
`"Rtf"`) maps to that platform's format set; anything else is used verbatim. Canonical names
win when they collide with a native name (on Windows `CF_TEXT` is literally named `"Text"` —
benign, since the canonical mapping is a superset).

### D9 — `Wait` accepts named kinds

```ahk
Clipboard.Wait()                 ; text or files, indefinitely — ClipWait's default
Clipboard.Wait(2, "Any")
Clipboard.Wait(2, "Image")
```

`waitFor` accepts `0`/`1` (identical to `ClipWait`) plus `"Text"`, `"Any"`, `"Image"`,
`"Files"`, `"Html"`, `"Rtf"` — the D8 kind vocabulary, reusing `Has`. This is a strict superset
of `ClipWait`, so there is no semantic drift between the two names.

### D10 — `OnChange` returns a hook object

```ahk
hook := Clipboard.OnChange(cb)      ; cb(hook, type)   type: 0 empty | 1 text | 2 other
hook.Stop()
```

with the `Stop`/`Pause`/`Paused`/`IsActive`/`Count` surface `Ks.WinEvent` already defines
([WinEvents.cs:118-145](../Keysharp.Core/Builtins/WinEvents.cs#L118-L145)) and
`Monitor.OnChange` is designed to reuse. Both routes dispatch from the one native monitor
(`Script.ClipFunctions` / `UpdateClipboardMonitoring`), so the backend is unchanged and only
the *registration* differs.

This is the **one place in the design with genuine duplication** — `OnClipboardChange` does
the same job. It earns its place because a closure registered with `OnClipboardChange` cannot
be unregistered without the script having stored the exact same function object, which is the
ergonomic gap the whole hook family exists to close. The cheap alternative — make
`Clipboard.OnChange` a plain alias with `addRemove` and no hook object — is listed as open
decision 2.

### D11 — `Ks.IsClipboardEmpty()` and `Ks.CopyImageToClipboard()` are removed, not aliased

Both are Keysharp-only, both are pre-0.1, and the project has consistently refused two ways to
do one thing (D8 of the Monitor design made the identical call for `Ks.MonitorFromPoint` /
`Ks.MonitorGetScale`). `IsClipboardEmpty()` → `Clipboard.IsEmpty`; `CopyImageToClipboard(f)` →
`Clipboard.Image := f`.

### D12 — Reserved, named now so nobody claims the names later

- `Clipboard.Primary` — the X11/Wayland PRIMARY selection (middle-click paste), as a second
  static sub-object exposing the same members. This is the one future that would justify
  instances (D2); reserving the name costs nothing and keeps that door open.
- `Clipboard.SetFiles(paths, mode?)` with `mode := "Cut"` — cut-vs-copy is desktop-specific
  (`Preferred DropEffect` on Windows, `x-special/gnome-copied-files` on GNOME,
  `application/x-kde-cutselection` on KDE, no pasteboard equivalent on macOS), so it is a
  `partial`-everywhere feature that should not ride into 0.1 on the back of a clean `Files`
  property.
- `Clipboard.SequenceNumber` — poll-for-change without a hook (`GetClipboardSequenceNumber`;
  no portable equivalent).

## 6. Implementation plan

**Phase 0 — fix the base (worth merging regardless of the rest).** ~0.5 day.
- F1: marshal *inside* the `Platform.Clipboard` facade, so every present and future call site
  is correct by construction rather than by remembering. Then delete the four ad-hoc
  `InvokeOnUIThread` wrappers at the `A_Clipboard`/`ClipWait` call sites — the facade covers
  them.
- F2: replace `WindowsClipboard.IsEmpty`'s reflection with `EnumClipboardFormats`, and derive
  `ChangeType()` from the same enumeration. Add tests asserting a custom-format-only clipboard
  reports non-empty and `ChangeType() == 2`.

**Phase 1 — the class over today's `IClipboard`.** ~1 day. `Text`, `IsEmpty`, `Clear`,
`Image`, `All`, `Wait`, `OnChange`, `Has` limited to `Text`/`Image`/`Any`. No new backend
members — every one of these already exists on `IClipboard`. Remove
`Ks.CopyImageToClipboard`/`Ks.IsClipboardEmpty`; update `FindText.ks` (F4: delete the temp-PNG
block) and the two `ControlTests/copy_image_to_clipboard*.ahk` scripts.

**Phase 2 — the format layer.** ~3 days, the bulk. Widen `IClipboard` with
`GetFormats()`, `GetData(format)`, and `SetAll(IReadOnlyList<(string, byte[])>)`; implement
`Formats`, `GetData`, `Files`, `Html`, `Rtf`, full `Has`, and `Set`. Most of the per-platform
work already exists inside `CaptureAll`/`RestoreAll` and needs extracting rather than writing:
Windows already enumerates formats and `GlobalLock`s their bytes
([:897-927](../Keysharp.Core/Internals/Platform/Services/ClipboardService.cs#L897-L927)), Eto
already round-trips `clip.Types`/`GetData`/`SetData`, and the Wayland backend already has
`GetClipboardMimetypes`/`GetClipboardContent`/`SetClipboardContent`. New code is the CF_HTML
codec (~60 lines), `CF_HDROP` ↔ path array, and the kind→format tables.

**Phase 3 — `Set` atomicity.** ~1 day + verification. Windows: build one `DataObject` and
`SetDataObject` once. Eto: **must be verified first** — whether successive
`clip.Text =`/`clip.Html =` accumulate into one data object or replace each other is not
documented and decides whether this is one transaction or needs an explicit `DataObject`.
Wayland extension: degrade to the single most useful representation, reusing `RestoreAll`'s
existing preference order, and document it as `partial` alongside `ClipboardAll`.

Total ≈ 5–6 focused days, of which Phases 0+1 are ~1.5 and deliver the whole question that
prompted this.

## 7. Explicitly out of scope for 0.1

- **Clipboard history / cloud clipboard.** Windows-only WinRT surface (`SetHistoryItemAsPasted`),
  no portable model, and reading history is a privacy-sensitive capability that deserves its own
  decision rather than arriving as a member on a convenience class.
- **Delayed rendering** (advertising a format and producing bytes only when a consumer asks).
  It is how Office avoids serializing megabytes on every copy, but it needs a callback that can
  run while another process blocks on the clipboard — a re-entrancy model Keysharp does not have.
- **Clipboard viewers / chain enumeration** — superseded by `OnClipboardChange` everywhere.
- **`Clipboard.Timeout` as an alias for `A_ClipboardTimeout`.** It applies only to the Windows
  raw-Win32 path; a third spelling of one Windows-only knob is not worth it.

## 8. Risks

- **CF_HTML header handling is fiddly.** The offsets are byte counts into the UTF-8 payload
  including the header itself, so they must be computed after the header length is known.
  Getting this wrong produces HTML that pastes as literal text into Word — silent and
  user-visible. Needs a round-trip test against a real browser copy.
- **Eto multi-format semantics are unverified** (Phase 3 above). If Eto replaces rather than
  accumulates, `Set` needs an explicit `DataObject` on three of five backends.
- **Writing fires `OnClipboardChange`**, so `Clipboard.Set` inside a clipboard-change handler
  re-enters. The existing dispatch already tolerates deferral
  ([Script.cs:1495](../Keysharp.Core/Runtime/Script/Script.cs#L1495)), but the docs page must
  say so.
- **`Files` set on Windows needs `CF_HDROP` with a `DROPFILES` header**; `SetFileDropList`
  handles it, at the cost of going through the OLE path (which fires the change notification
  twice — the exact problem `SetText` was hand-written in raw Win32 to avoid,
  [ClipboardService.cs:821](../Keysharp.Core/Internals/Platform/Services/ClipboardService.cs#L821)).
  Either accept the double notification for file writes or hand-build `DROPFILES`.
- **macOS is unverifiable from this host** — `OSX` is only defined on a macOS build, so every
  macOS cell in §4 ships `unknown`, the same debt `Monitor` and `WinEvent` carry.
- **`Formats` leaks platform vocabulary into scripts**, which is deliberate (D8) but means a
  script using it is Windows-only or Linux-only by construction. The docs must say this next to
  the member, not in a footnote.

## 9. Migration impact

| Consumer | Change |
|---|---|
| `Keysharp/Scripts/FindText.ks` | drop `CopyImageToClipboard` from the `#import` ([:106](../Keysharp/Scripts/FindText.ks#L106)); replace the temp-PNG block ([:1643-1661](../Keysharp/Scripts/FindText.ks#L1643-L1661)) with `Clipboard.Image := img` — a net deletion of ~20 lines including the deferred-delete timer |
| `Keysharp.Tests/Code/ControlTests/copy_image_to_clipboard.ahk`, `…2.ahk` | two call sites → `Clipboard.Image :=` |
| `docs/reference.md:470-474` | the "New clipboard functions" bullet is rewritten around the class |
| `docs/capabilities.json` | `CopyImageToClipboard()` and `IsClipboardEmpty()` rows replaced; new rows per §4, using `unknown` for the macOS column |
| `Keysharp.Tests/EnvTests.cs`, `ClipboardRecoveryTests.cs` | unaffected — they exercise `A_Clipboard`/`ClipboardAll`, which do not change |

## 10. Implementation notes (built 2026-08-09)

All four phases are implemented, and every open decision in §11 was resolved as recommended.
Six things landed differently from the plan, each for a reason worth keeping:

- **`IsEmpty` and `ChangeType` are derived once, in a shared `ClipboardBase`, from
  `GetFormats()`** rather than being implemented per backend. They previously disagreed with each
  other as well as with AHK: `ChangeType` on Eto returned 1 only for text, so **a file copy on
  Linux reported type 2 where Windows reported 1**. Both now use AHK's own rule (text OR files),
  identically everywhere. This deleted more code than it added — `EtoClipboard` and
  `WaylandBackendClipboard` each lost two hand-written overrides, and the Wayland backend's
  `HasTextIn`/`HasMime` helpers went with them, folded into a single `GetFormats` override.

- **The UI-thread marshalling is a decorator (`UiThreadClipboard`) applied once in
  `PlatformHost.Clipboard`**, not edits at the six call sites. F1's fix is therefore structural:
  a future seam cannot forget it, and the backends now *document* that they may assume the UI
  thread. The ad-hoc `InvokeOnUIThread` wrappers at `A_Clipboard` and `ClipWait` were deleted.

- **`IClipboard` grew two kind-level readers (`GetKindText`, `GetFiles`) rather than only the raw
  `GetData`.** Decoding is as platform-specific as encoding — Windows HTML carries a CF_HTML
  envelope, its RTF is ASCII, its file list is a DROPFILES blob — so the backend has to own both
  ends. The alternative was an `if (Windows)` at the call site, which D8 exists to prevent.

- **`SetImage` is deliberately NOT routed through `SetAll`.** The toolkit publishes a richer
  format set for a lone image (CF_BITMAP plus both DIB flavors) than the raw DIB+PNG pair `SetAll`
  builds; `SetAll`'s raw path exists for cross-format atomicity, which a single image does not
  need.

- **`RestoreAll` on Windows now allocates with `GlobalAlloc(GMEM_MOVEABLE)`** through the same
  helper `SetAll` uses. It had been passing `Marshal.AllocHGlobal` memory to `SetClipboardData`,
  which works only because the Win32 local and global heaps are unified — not what the API
  documents. One allocation strategy, and it is the correct one.

- **`Image.LoadFromSource` now accepts a backend `Bitmap`.** It previously fell through to the
  "treat anything else as a handle" branch and read the object as a nonsense pointer, so a bitmap
  obtained through `Clr` could not be put on the clipboard (or turned into an `Image`) at all.

### Resolved unknowns

- **§8's "Eto multi-format semantics are unverified" is resolved: all three Eto backends
  accumulate.** GTK's `ClipboardHandler.SetEntry` appends to a target list and republishes, the
  native Wayland handler keeps a mime→bytes dictionary, and the Mac handler writes into one
  `NSPasteboard` session after a single `ClearContents`. `EtoClipboard.SetAll` therefore clears
  once and applies each entry, producing one multi-format offer. The cost is that each entry
  publishes, so N entries raise up to N change notifications where the Windows raw path raises
  one; GTK debounces them into one on Wayland but not on X11.

### Verified

- Windows: 23 new `Category=Clipboard` tests green, plus the full curated suite (356/356) and
  `Category=Env|Image|Clipboard` (67/67).
- Linux (WSL, X11 under Xvfb): 22/23 clipboard tests green with one documented skip, and the
  curated suite at 373/374 — the single failure, `WarningDirectiveIsNonFatal`, is pre-existing,
  Linux-only, belongs to the in-flight `#Warning`/parser work and passes on Windows.
- The script-facing path is exercised from a real `.ks` (`Keysharp.Tests/Code/clipboard-class.ahk`),
  which is what proves dynamic dispatch reaches the members under the names a script types —
  `Type(hook)` resolves to `ClipboardHook`, and `Clipboard()` throws as intended.

### Not verified, and honest about it

- **macOS has never been compiled**, let alone run: `OSX` is defined only on a macOS build host.
  Every macOS cell in §4 ships `unknown`, the same debt Monitor and WinEvent carry.
- **Reading an image back on Linux does not work under Xvfb.** Eto's GTK handler advertises the
  image targets, but its retrieval callback fails with
  `gtk_selection_data_set_pixbuf: assertion 'GDK_IS_PIXBUF (pixbuf)' failed`, so the clipboard
  offers an image it cannot produce. This is upstream of this API — the same `clip.Image = …` call
  the old `CopyImageToClipboard` made — and needs a real desktop session to confirm or refute.
  `ImageRoundTrip` skips with that message rather than passing quietly; `Clipboard.Image` is
  `partial` on both Linux rows.
- **The Wayland shell-extension backend never executes here** (WSL has no Cinnamon/Muffin session),
  so its `SetAll` degradation is compile-checked only, exactly like `RestoreAll` before it.
- **No `OnChange` callback has been observed firing from a real clipboard change** on Linux or
  macOS. The hook's own bookkeeping (Pause/Count/Stop, and the callback's `(hook, type)` shape) is
  covered by tests that dispatch directly.

### Curated-test policy

The three `ClipboardTests` that touch no clipboard — the CF_HTML codec, the uri-list parser and
the `OnChange` bookkeeping — are named individually in `CURATED_TEST_FILTER` so CI covers §8's
top risk deterministically. The rest of `Category=Clipboard` shares one process-wide resource with
whatever else is running on the machine and stays out of CI, matching the pre-existing treatment
of the `Env` clipboard tests.

## 11. Open decisions

**All six were resolved as recommended when the owner accepted the plan on 2026-08-09, and are
built that way.** They are kept here for the reasoning.

1. **Does `Clipboard.Text` duplicate `A_Clipboard`?** Yes, deliberately. *Recommendation: keep
   it.* A clipboard class where the most common datum is only reachable through a global
   variable is broken, and the Monitor design already accepted the same overlap
   (`MonitorGet` vs `Monitor.Bounds`). The rule the project actually follows is narrower:
   *Keysharp-only* duplicates get folded in and removed (D11), AHK-compat ones stay.
2. **`Clipboard.OnChange`: hook object, plain alias, or omit?** *Recommendation: hook object*
   (D10) — but this is the only real duplication in the proposal, so it is the owner's call.
3. **`Clipboard.All` vs `Snapshot()`/`Restore()`.** *Recommendation: `All`*, because it maps
   1:1 onto the existing `ClipboardAll` type and vocabulary; `Snapshot`/`Restore` reads better
   but introduces a third name for one concept.
4. **Keep `Image.FromClipboard()` as an alias?** *Recommendation: yes* (D5), on factory-family
   grounds. Dropping it is defensible and would make the design perfectly one-way.
5. **Is Phase 2 (the format layer) in 0.1, or is 0.1 just Phases 0+1?** Phases 0+1 answer the
   question that prompted this design and are fully verifiable on two of five backends today.
   Phase 2 is most of the cost and carries the CF_HTML risk. *Recommendation: commit 0+1 to
   0.1; make Phase 2 conditional on the same Linux/macOS verification pass M4 already tracks.*
   **Overridden by the owner: all phases were built at once.** The CF_HTML risk is retired by
   the round-trip and byte-offset tests in §10; the macOS verification debt is unchanged.
6. **`Set` bag accepting both an object and a `Map`.** *Recommendation: both* (D6), forced by
   AHK's object-literal key syntax — but an owner who prefers exactly one type should pick
   `Map`.
