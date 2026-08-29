#NoTrayIcon
; Named arguments: `f(Name: value)`. The script deliberately misspells names in the error cases below, so the
; build-time check is turned off here -- those cases assert the RUNTIME binder rejects them.
#Warn NamedArg, Off
; The carrier type itself, needed only to build a named call dynamically or inspect a collected one.
#import KS { NamedArgs, Clr }
#Include <assert>

; ---------------------------------------------------------------- user functions
f3(a, b := "B", c := "C") => a . "/" . b . "/" . c

AssertEq(f3("A", c: "Z"), "A/B/Z", A_LineNumber)  ; fills a gap, leaving b at its default
AssertEq(f3("A", c: "Z", b: "Y"), "A/Y/Z", A_LineNumber)  ; named arguments out of parameter order
AssertEq(f3(a: "A"), "A/B/C", A_LineNumber)  ; every argument named
AssertEq(f3("A", "B", "C"), "A/B/C", A_LineNumber)  ; unaffected when none are named

; Evaluation order is SOURCE order even when the names are out of parameter order: the values enter the
; argument array left to right and are permuted only at bind time.
order := ""
tag(s) {
	global order
	order .= s
	return s
}
AssertEq(f3("A", c: tag("1"), b: tag("2")), "A/2/1", A_LineNumber)
AssertEq(order, "12", A_LineNumber)

; ---------------------------------------------------------------- built-ins
AssertEq(SubStr("abcdef", startingPos: 2), "bcdef", A_LineNumber)
AssertEq(SubStr("abcdef", startingPos: 2, length: 3), "bcd", A_LineNumber)
AssertEq(InStr("hello world", "O", caseSense: true), 0, A_LineNumber)  ; case-sensitive: no match
AssertEq(InStr("hello world", "O", caseSense: false), 5, A_LineNumber)

Clr.Load("System")
AssertEq(Clr.System.Convert.ToString(value: 255, toBase: 16), "ff", A_LineNumber)
AssertEq(Clr.System.Convert.ToString(value: 255), "255", A_LineNumber)
AssertEq(Clr.System.Math.Round(value: 2.567, digits: 2), 2.57, A_LineNumber)

; StartsWith/EndsWith follow InStr's CaseSense convention: insensitive by default, and both On and Off
; are culture-invariant.
AssertEq("Hello".StartsWith("he"), 1, A_LineNumber)  ; default is case-INsensitive
AssertEq("Hello".StartsWith(token: "he"), 1, A_LineNumber)
AssertEq("Hello".StartsWith("he", caseSense: true), 0, A_LineNumber)  ; On -> case-sensitive
AssertEq("Hello".StartsWith("He", caseSense: true), 1, A_LineNumber)
AssertEq("Hello".EndsWith("LO"), 1, A_LineNumber)
AssertEq("Hello".EndsWith("LO", caseSense: true), 0, A_LineNumber)
AssertEq("Hello".EndsWith(token: "lo", caseSense: false), 1, A_LineNumber)

; A parameter whose C# name is an escaped keyword (`@default`) still binds by the name scripts see.
arr := [10, 20, 30]
AssertEq(arr.Get(index: 2), 20, A_LineNumber)

sparse := Array()
sparse.Length := 3                             ; all three elements unset, so Get falls back to the default
AssertEq(sparse.Get(2, "fallback"), "fallback", A_LineNumber)
AssertEq(sparse.Get(index: 2, default: "fallback"), "fallback", A_LineNumber)

m := Map("a", 1)
AssertEq(m.Get(key: "a"), 1, A_LineNumber)
AssertEq(m.Get(key: "zz", default: 7), 7, A_LineNumber)

; OwnProps takes no arguments: how many values an iteration yields is decided by the for-loop's
; variable count, not by a parameter.
ownPropsProbe := {alpha: 1}
Throws(() => ownPropsProbe.OwnProps(true), A_LineNumber)

; Array.Filter's StartIndex is a documented Keysharp extension.
AssertEq([1, 2, 3, 4].Filter((v, i) => v > 1, startIndex: 3).Length, 2, A_LineNumber)

; A collection callback may declare only the parameters it needs, whatever shape it is. The arity has to come
; from the member that will actually run: an ObjBindMethod carries a placeholder signature that claims to be
; variadic, and a callable object has no signature of its own.
AssertEq([1, 2, 3].Filter((v) => v > 1).Length, 2, A_LineNumber)
AssertEq([1, 2, 3].Filter((v, i) => v > 1).Length, 2, A_LineNumber)
AssertEq([1, 2, 3].Filter(() => true).Length, 3, A_LineNumber)
AssertEq([1, 2, 3].Filter((a*) => a[1] > 1).Length, 2, A_LineNumber)
AssertEq([1, 2, 3].MapTo((v) => v * 2).Join(","), "2,4,6", A_LineNumber)
AssertEq([1, 2, 3].FindIndex((v) => v > 1), 2, A_LineNumber)
class NPPred {
    Pred(v) => v > 1
    static SPred(v) => v > 1
    Call(v) => v > 1
}
npp := NPPred()
AssertEq([1, 2, 3].Filter(ObjBindMethod(npp, "Pred")).Length, 2, A_LineNumber)
AssertEq([1, 2, 3].Filter(npp.Pred).Length, 2, A_LineNumber)
AssertEq([1, 2, 3].Filter(NPPred.SPred).Length, 2, A_LineNumber)
AssertEq([1, 2, 3].Filter(npp).Length, 2, A_LineNumber)  ; callable object
np2(a, b) => b > a
AssertEq([1, 2, 3].Filter(np2.Bind(1)).Length, 2, A_LineNumber)  ; bound: one slot already filled

; A transform may yield nothing for an element: that is how a sparse array is built. A predicate, by contrast,
; still must return something.
sparseM := [1, 2, 3].MapTo((v) => v = 2 ? unset : v)
Assert(sparseM.Length == 3 && !sparseM.Has(2) && sparseM[1] == 1, A_LineNumber)

; The `object @this` convention: the receiver is not an argument and must not be bindable.
o := {alpha: 1}
AssertEq(o.HasProp(name: "alpha"), 1, A_LineNumber)

; ---------------------------------------------------------------- classes
class NPWidget {
	__New(x := 1, y := 2) {
		this.x := x, this.y := y
	}
	Sum(m := 1, n := 0) => this.x * m + this.y + n
	static Make(a := 5, b := 6) => a * 100 + b
}

w := NPWidget(y: 40)                          ; construction relays through Class.Call to __New
Assert(w.x == 1 && w.y == 40, A_LineNumber)
AssertEq(NPWidget(x: 7, y: 8).y, 8, A_LineNumber)
AssertEq(w.Sum(n: 100), 141, A_LineNumber)  ; instance method
AssertEq(NPWidget.Make(b: 9), 509, A_LineNumber)  ; static method

; ---------------------------------------------------------------- closures
mk(p) => (q := 5, (r := 0) => p . "-" . q . "-" . r)
AssertEq(mk("P")(r: 9), "P-5-9", A_LineNumber)

; ---------------------------------------------------------------- by-ref and spread
Assert(RegExMatch("abc123", "\d+", outputVar: &mm) > 0 && mm[0] == "123", A_LineNumber)

rest := ["A", "B"]
AssertEq(f3(rest*, c: "Z"), "A/B/Z", A_LineNumber)  ; spread contributes positionals, named still trails

; ---------------------------------------------------------------- bound functions
bf := f3.Bind("A")
AssertEq(bf(c: "Q"), "A/B/Q", A_LineNumber)  ; named argument supplied at CALL time

bn := f3.Bind(c: "CC")                        ; bound BY NAME
AssertEq(bn("A", "BB"), "A/BB/CC", A_LineNumber)
Assert(bn.MinParams == 1 && bn.MaxParams == 2, A_LineNumber) ; binding optional `c` drops Max but not Min

bn2 := f3.Bind(a: "A")                        ; binding a REQUIRED parameter drops both
Assert(bn2.MinParams == 0 && bn2.MaxParams == 2, A_LineNumber)
; A name binds a SLOT, so later positional arguments must fill the next FREE one rather than colliding
; with it. Binding an early parameter by name is the case that gets this wrong if slots aren't tracked.
AssertEq(bn2("BB"), "A/BB/C", A_LineNumber)
AssertEq(bn2("BB", "CC"), "A/BB/CC", A_LineNumber)
AssertEq(f3.Bind(a: "A").Bind("BB")(), "A/BB/C", A_LineNumber)
AssertEq(bn2.Params.Length, 2, A_LineNumber)  ; and Params agrees with MinParams/MaxParams
Assert(bn2.Params[1].Name == "b" && bn2.Params[2].Name == "c", A_LineNumber)
vb(head, rest*) => head . "|" . rest.Length
AssertEq(vb.Bind(head: 1)(2, 3), "1|2", A_LineNumber)
Throws(() => f3.Bind("A").Bind(a: "X"), A_LineNumber)  ; already supplied positionally

bn3 := f3.Bind(c: "CC").Bind("A")             ; positional bind after a named one
AssertEq(bn3("BB"), "A/BB/CC", A_LineNumber)
Throws(() => f3.Bind(c: "1").Bind(c: "2"), A_LineNumber)   ; already bound by name

; An omitted slot -- in Bind or in a call -- holds nothing, so a name may fill it; only a slot holding a real
; value collides.
hole := f3.Bind(, , "ZZ")
AssertEq(hole("AA", "BB"), "AA/BB/ZZ", A_LineNumber)  ; the holes still fill positionally
AssertEq(hole(a: "AA"), "AA/B/ZZ", A_LineNumber)  ; ... and by name
AssertEq(hole("AA", b: "BB"), "AA/BB/ZZ", A_LineNumber)  ; ... and by both at once
AssertEq(f3.Bind(, , "ZZ").Bind(a: "AA")(), "AA/B/ZZ", A_LineNumber)
Throws(() => f3.Bind("XX")(a: "AA"), A_LineNumber)   ; a slot Bind actually FILLED is still a collision

; ObjBindMethod has no signature at bind time, so the name is resolved at CALL time instead.
obm := ObjBindMethod(w, "Sum")
AssertEq(obm(n: 100), 141, A_LineNumber)
Throws(() => ObjBindMethod(w, "Sum")(nope: 1), A_LineNumber)

; ---------------------------------------------------------------- rejections
Throws(() => f3("A", nosuch: 1), A_LineNumber)            ; unknown name
Throws(() => f3("A", "B", a: "X"), A_LineNumber)          ; already supplied positionally
Throws(() => f3("A", , a: "X"), A_LineNumber)             ; ... including with an unrelated omitted slot in between

Throws(() => w.Sum(this: 1), A_LineNumber)                ; the receiver is not an argument

; An omitted slot supplied nothing, so a name may fill it -- the call-site twin of Bind's holes.
AssertEq(f3(, a: "X"), "X/B/C", A_LineNumber)
AssertEq(f3("A", , c: "Z"), "A/B/Z", A_LineNumber)

; ---------------------------------------------------------------- forwarding through a variadic
; A call's names travel as one ordinary value, so a variadic parameter collects it like any other element and a
; spread re-emits it -- that IS the relay mechanism. The tail itself is not bindable; naming it just puts a name
; called "rest" in the container.
variadic(head, rest*) => head "|" rest.Length "|" (rest.Length && rest[1] is NamedArgs ? rest[1]["rest"] : "-")
AssertEq(variadic(1, rest: 2), "1|1|2", A_LineNumber)

NPInner(text, title := "T", opts := "O") => text "/" title "/" opts
NPWrap(args*) => NPInner(args*)
AssertEq(NPWrap("hi"), "hi/T/O", A_LineNumber)  ; the pattern this exists for
AssertEq(NPWrap("hi", title: "X"), "hi/X/O", A_LineNumber)
AssertEq(NPWrap(text: "hi", opts: "Z"), "hi/T/Z", A_LineNumber)  ; nothing positional at all
AssertEq(NPWrap("hi", TITLE: "X"), "hi/X/O", A_LineNumber)  ; names are case-insensitive

NPWrap2(first, rest*) => first "|" NPInner(rest*)  ; a wrapper with its own leading parameter
AssertEq(NPWrap2("A", "hi", opts: "Q"), "A|hi/T/Q", A_LineNumber)
NPWrap3(a := "d", rest*) => a "|" NPInner("hi", rest*)
AssertEq(NPWrap3(title: "N"), "d|hi/N/O", A_LineNumber)  ; the name skips the declared parameter into the tail

; The collected container is an ordinary trailing element: countable, indexable, and inspectable. Inspection
; goes through its own properties and methods, never by passing it TO something -- as the last argument of any
; call it would name that callee's parameters instead, which is exactly the contract.
NPPeek(args*) => args.Length "," (args[3] is NamedArgs) "," args[3].Has("title") "," args[3]["title"]
AssertEq(NPPeek("hi", "there", title: "X"), "3,1,1,X", A_LineNumber)

; A misspelling is caught by the function that actually declares the parameters, not by the relay.
Throws(() => NPWrap("hi", nosuch: 1), A_LineNumber)

; A container built by hand is a named argument wherever it lands last -- in a direct call, or riding an
; array into a spread. This is how a named call is made dynamically.
AssertEq(NPInner("hi", NamedArgs("title", "D")), "hi/D/O", A_LineNumber)
manual := ["hi", NamedArgs("title", "M")]
AssertEq(NPInner(manual*), "hi/M/O", A_LineNumber)

; A name whose spelling is itself computed is an ordinary item assignment.
dyn := NamedArgs()
key := "title"
dyn[key] := "DYN"
AssertEq(NPInner("hi", dyn), "hi/DYN/O", A_LineNumber)

; Names are Map KEYS, so they share no namespace with the type's own members: a parameter called `base` or
; `clone` is supplied like any other, by every route. This is what an Object-based carrier could not do.
collide(base := "-", clone := "-") => base clone
AssertEq(collide(base: "B", clone: "C"), "BC", A_LineNumber)  ; the literal syntax
AssertEq(collide(NamedArgs("base", "B", "clone", "C")), "BC", A_LineNumber)
AssertEq(collide(NamedArgs(["base", "B", "clone", "C"]*)), "BC", A_LineNumber)
item := NamedArgs()
item["base"] := "B", item["clone"] := "C"
AssertEq(collide(item), "BC", A_LineNumber)  ; ... and by item assignment

; Names are matched case-insensitively, like parameter names, whichever way the container was built -- a
; plain Map is COPIED, not adopted, so neither its case mode nor its storage leaks in.
ci := NamedArgs("TITLE", "T")
Assert(ci["title"] == "T" && NPInner("hi", ci) == "hi/T/O", A_LineNumber)
fromMap := NamedArgs(Map("TITLE", "T"))            ; source Map is case-SENSITIVE
Assert(fromMap.CaseSense = "Off" && fromMap["title"] == "T", A_LineNumber)
mapSrc := Map("title", "T")
copied := NamedArgs(mapSrc)
mapSrc["opts"] := "LEAKED"                          ; mutating the source must not reach the container
AssertEq(NPInner("hi", copied), "hi/T/O", A_LineNumber)

; A name added or removed while the container is being read cannot corrupt the walk -- what binding reads is
; a snapshot, not the live store.
mutating := NamedArgs("title", "M")
NPMutate(text, title := "T", opts := "O") {
	mutating["opts"] := "LATE"
	return text "/" title "/" opts
}
AssertEq(NPMutate("hi", mutating), "hi/M/O", A_LineNumber)

; A subclass that declares its own __Enum reshapes what binds, exactly as it reshapes what a `for` loop
; yields: the names it enumerates are the ones supplied, whatever it actually stores.
class NPReshaped extends NamedArgs {
	__Enum(n) => Map("opts", "FROM_ENUM").__Enum(n)
}
reshaped := NPReshaped()
reshaped["title"] := "stored-but-not-enumerated"
AssertEq(NPInner("hi", reshaped), "hi/T/FROM_ENUM", A_LineNumber)

; An empty container is not an argument, so a variadic collects nothing from it -- whether or not the callee
; declares a bindable parameter of its own (the two took different paths before).
emptyRelay(rest*) => rest.Length
emptyMixed(a := 0, rest*) => rest.Length
Assert(emptyRelay(NamedArgs()) == 0 && emptyMixed(NamedArgs()) == 0, A_LineNumber)

; A BoundFunc takes a SNAPSHOT of the names bound to it, exactly as it does of the values bound
; positionally: a container the caller keeps a reference to is not the one the BoundFunc re-emits.
bsrc := NamedArgs("t", 1)
bsnap(head, rest*) => rest[1]["t"]
bsnapf := bsnap.Bind(bsrc)
bsrc["t"] := 99
AssertEq(bsnapf(0), 1, A_LineNumber)

; A variadic built-in absorbs an undeclared name the same way a script variadic does: as DATA. Format prints
; the container's string form, Push stores it, Max fails converting it to a number -- there is no special
; rejection, because there is no special type of tail.
AssertEq(Format("{1}", bogus: 5), "bogus: 5", A_LineNumber)
pushProbe := [1, 2]
pushProbe.Push(x: 3)
Assert(pushProbe.Length == 3 && pushProbe[3] is NamedArgs && pushProbe[3]["x"] == 3, A_LineNumber)
Throws(() => Max(1, 2, nosuch: 9), A_LineNumber)

; Whether a container is a named argument is decided purely by POSITION: named arguments come after positional
; ones, so only the LAST argument binds by name. A container followed by a positional argument -- written
; directly or deposited by a spread -- is itself an ordinary positional VALUE.
posNA(x, y := "-", z := "-") => (x is NamedArgs ? "N:" x.ToString() : x) "/" (y is NamedArgs ? "N:" y.ToString() : y) "/" z
AssertEq(posNA("a", NamedArgs("b", 1), "c"), "a/N:b: 1/c", A_LineNumber)  ; mid-list container is positional data
AssertEq(posNA("a", NamedArgs("z", "Q")), "a/-/Q", A_LineNumber)  ; ... while a trailing one binds by name
AssertEq(posNA("a", NamedArgs("y", 1, "z", 2)), "a/1/2", A_LineNumber)  ; one container carries every name of the call
AssertEq(posNA(manual*, "T2"), "hi/N:title: M/T2", A_LineNumber)  ; a spread's container before a later positional: data
mEmpty := [NamedArgs("opts", "B")]
AssertEq(posNA(mEmpty*, "hi"), "N:opts: B/hi/-", A_LineNumber)

; A bound function reports the CALLER-visible signature: a bound argument consumes its parameter slot, so
; Filter passes one argument to a one-parameter method instead of failing on every element.
hasMap := Map(2, "x")
hasFn := Map.Prototype.Has.Bind(hasMap)
Assert(hasFn.MinParams == 1 && hasFn.MaxParams == 1, A_LineNumber)
AssertEq([1, 2, 3].Filter(hasFn).Length, 1, A_LineNumber)

; Binding into a variadic function must not swallow the tail from Params: the tail is never used up.
vfp(rest*) => rest.Length
bvfp := vfp.Bind(1)
Assert(bvfp.IsVariadic && bvfp.Params.Length == 1 && bvfp.Params[1].Variadic == 1, A_LineNumber)

; Supplying the same parameter by name twice across Bind and the call reports "more than once" -- at that
; point the first supply is indistinguishable from a positional one, so the message must not claim either.
dupErr := ""
try
	vb.Bind(head: 1)(head: 2)
catch as e
	dupErr := e.Message
Assert(InStr(dupErr, "'head'") && InStr(dupErr, "more than once"), A_LineNumber)

; In an ARRAY LITERAL a spread's container is plain data, like everywhere outside binding.
manlit := [manual*]
Assert(manlit.Length == 2 && manlit[1] == "hi" && manlit[2] is NamedArgs, A_LineNumber)

; Two spreads: values interleave in source order, and both containers end up trailing, so their names are
; unioned and all bind. Adding a name of one's own to a forwarded call is the same shape.
AssertEq(NPInner(manual*, mEmpty*), "hi/M/B", A_LineNumber)
AssertEq(NPInner(manual*, opts: "Z"), "hi/M/Z", A_LineNumber)

; A spread call evaluated INSIDE another spread call's argument list cannot disturb the outer one.
NPNest() {
    t := ["q1", NamedArgs("title", "n")]
    return NPInner(t*)
}
AssertEq(NPInner(NPNest(), mEmpty*), "q1/n/O/T/B", A_LineNumber)

; The same name arriving from two different spreads is the ordinary specified-twice error.
mDup := [NamedArgs("title", "DUP")]
Throws(() => NPInner(manual*, mDup*), A_LineNumber)

; ---------------------------------------------------------------- built-in constructors
; A built-in __New declares its real, documented signature (Buffer.__New(ByteCount, FillByte)), so its names
; bind through the same binder as any script function's -- same rules, same errors.
AssertEq(Buffer(ByteCount: 16).Size, 16, A_LineNumber)
bfill := Buffer(8, FillByte: 5)
AssertEq(NumGet(bfill, 0, "UChar"), 5, A_LineNumber)  ; mixed positional + named
Throws(() => Buffer(16, ByteCount: 8), A_LineNumber)     ; supplied twice is the usual collision
Throws(() => Buffer(NoSuch: 1), A_LineNumber)
AssertEq(Error(Message: "m", Extra: "x").Extra, "x", A_LineNumber)
AssertEq(TypeError(Message: "t").Message, "t", A_LineNumber)  ; subclasses inherit the names through Error.__New
AssertEq(InputHook(Options: "V").VisibleText, 1, A_LineNumber)
avar := Array(x: 1)                               ; a truly variadic constructor absorbs, like any variadic
Assert(avar.Length == 1 && avar[1] is NamedArgs, A_LineNumber)

; ---------------------------------------------------------------- classes with no __New to bind against
; Object() reads the names as name/value pairs, so the call form agrees with the literal.
AssertEq(Object(x: 1).x, 1, A_LineNumber)
o2 := Object("k", 1, v: 2)
Assert(o2.k == 1 && o2.v == 2, A_LineNumber)
; A dangling name errors rather than being dropped -- which is also what happens when a container was meant as
; a pair's VALUE: a trailing one reads as a named argument, leaving its would-be name unpaired. The literal
; routes through the same construction, so it errors the same way (loudly, never a silent drop).
Throws(() => Object("a"), A_LineNumber)
Throws(() => Object("a", NamedArgs("x", 5)), A_LineNumber)
Throws(() => ({a: NamedArgs("x", 5)}), A_LineNumber)
class NPNoNew {
}
npn := NPNoNew(a: 1)                              ; no __New: the default variadic one absorbs and ignores
AssertEq(Type(npn), "NPNoNew", A_LineNumber)

; Binding a name a variadic target does not declare defers it, exactly as supplying it at the call would.
vfn2(head, rest*) => head "|" (rest.Length && rest[1] is NamedArgs ? rest[1]["t"] : "-")
AssertEq(vfn2(1, t: 2), "1|2", A_LineNumber)
AssertEq(vfn2.Bind(1)(t: 2), "1|2", A_LineNumber)
AssertEq(vfn2.Bind(t: 2)(1), "1|2", A_LineNumber)

; Naming a later parameter while leaving a REQUIRED earlier one unfilled is an ordinary catchable error, not
; a crash: the gap the name skipped over is exactly what `f3(, "B")` leaves, and it reports the same way.
reqErr := ""
try
	f3(c: "Z")
catch as e
	reqErr := e.Message
Assert(InStr(reqErr, "'a'") && InStr(reqErr, "required"), A_LineNumber)

; ---------------------------------------------------------------- non-ASCII identifiers
; The emitted C# parameter is lower-cased and matched case-insensitively, which works for identifiers
; whose case folds symmetrically. U+1E9E folds to U+00DF, which OrdinalIgnoreCase does not match back --
; and which characters behave this way depends on the runtime's Unicode tables. The declared spelling is
; carried on the parameter so the name written in the script always binds.
sharpS(ẞ := "-") => ẞ
AssertEq(sharpS(ẞ: "v"), "v", A_LineNumber)
uni(Ω := "-", naïve := "-", 日本語 := "-") => Ω . naïve . 日本語
AssertEq(uni(Ω: "a", 日本語: "c"), "a-c", A_LineNumber)
AssertEq(uni(ω: "a", NAÏVE: "b"), "ab-", A_LineNumber)  ; case-insensitive where the fold is symmetric
kw(class := "-", params := "-") => class . params  ; C# keywords: the '@' escape must not leak into the name
AssertEq(kw(class: "x", params: "y"), "xy", A_LineNumber)

; ---------------------------------------------------------------- NamedArgs as a value
; The carrier type is script-visible, like VarRef: a container passed LAST is a named argument, everywhere, so
; the position is reserved for that role. Inspection goes through properties and `is`, never through a call --
; passing one to Type() would name one of Type's parameters, which is the contract working as stated.
naProbe := NamedArgs("k", 5)
Assert((naProbe is NamedArgs) && (naProbe is Object), A_LineNumber)
Assert(naProbe["k"] == 5 && naProbe.Has("k"), A_LineNumber)
AssertEq(f3("A", NamedArgs()), "A/B/C", A_LineNumber)  ; an empty container is legal and binds nothing
Throws(() => Type(naProbe), A_LineNumber)                 ; a trailing container names Type's parameter -- by design
Throws(() => NamedArgs("a"), A_LineNumber)                ; a dangling name, like Object()

; NamedArgs is extensible: a subclass instance IS a NamedArgs, so it binds like one.
class NPTagged extends NamedArgs {
	Tag => "t"
}
tagged := NPTagged("c", "Q")
Assert((tagged is NamedArgs) && tagged.Tag == "t", A_LineNumber)
AssertEq(f3("A", tagged), "A/B/Q", A_LineNumber)

; An ASSIGNMENT's value slot is a data channel with no named-argument syntax, so a container being assigned is
; a value -- through a property setter, and through a for-loop writing a forwarded element into its out-var.
class NASink {
	static got := ""
	P {
		set => NASink.got := value
	}
}
nsink := NASink()
nsink.P := NamedArgs("x", 1)
Assert(NASink.got is NamedArgs && NASink.got["x"] == 1, A_LineNumber)
for nv in [NamedArgs("y", 2)]
	Assert(nv is NamedArgs && nv["y"] == 2, A_LineNumber)

; ---------------------------------------------------------------- dynamic names
; `%x%: v` and the name-building `a%b%c: v` compute the parameter name at run time, exactly as the same text
; computes an object-literal key. Only the NAME is dynamic -- everything else about the argument is unchanged.
dname(alpha, beta := "B", gamma := "G") => alpha . "/" . beta . "/" . gamma

nm := "gamma"
AssertEq(dname("A", %nm%: "Z"), "A/B/Z", A_LineNumber)  ; whole name from a variable
AssertEq(dname("A", b%"et"%a: "Y"), "A/Y/G", A_LineNumber)  ; built from literal text around a deref
mid := "amm"
AssertEq(dname("A", g%mid%a: "Z"), "A/B/Z", A_LineNumber)  ; ... and from a variable in the middle
AssertEq(dname("A", beta: "Y", %nm%: "Z"), "A/Y/Z", A_LineNumber)  ; mixed with a literal name
AssertEq([1, 2, 3].Get(%"index"%: 2), 2, A_LineNumber)  ; on a method call
AssertEq(dname("A", %nm%: "Z", ), "A/B/Z", A_LineNumber)  ; a trailing comma still adds no slot

; The name is evaluated where it is written -- before its own value, and after the positional arguments.
order := ""
AssertEq(dname(tag("A"), %tag("gamma")%: tag("V")), "A/B/V", A_LineNumber)
AssertEq(order, "AgammaV", A_LineNumber)

; A name that matches no parameter is the ordinary binder error; it simply cannot be caught at build time.
bad := "nosuch"
Throws(() => dname("A", %bad%: 1), A_LineNumber)

; Two derefs yielding one name collapse last-wins, as two object-literal keys would. The parser rejects that
; when both names are written literally, but it cannot see it here, and neither can the binder: what reaches it
; is a single entry.
n1 := "gamma", n2 := "gamma"
AssertEq(dname("A", %n1%: "X", %n2%: "Y"), "A/B/Y", A_LineNumber)
AssertEq(dname("A", gamma: "X", %n2%: "Y"), "A/B/Y", A_LineNumber)

; ---------------------------------------------------------------- Func.Params
pinfo(alpha, beta := 5, &out?, rest*) => alpha
pi := pinfo.Params
AssertEq(pi.Length, 4, A_LineNumber)
Assert(pi[1].Name == "alpha" && pi[1].Optional == 0 && !pi[1].HasOwnProp("Default"), A_LineNumber)
Assert(pi[2].Name == "beta" && pi[2].Optional == 1 && pi[2].Default == 5, A_LineNumber)
Assert(pi[3].Name == "out" && pi[3].ByRef == 1, A_LineNumber)
Assert(pi[4].Name == "rest" && pi[4].Variadic == 1, A_LineNumber)
AssertEq(SubStr.Params[1].Name, "string", A_LineNumber)  ; built-ins report their documented names
AssertEq([].Get.Params[1].Name, "index", A_LineNumber)  ; the receiver is not reported

; ---------------------------------------------------------------- Func.Name
; A nested function or closure is lowered to a local function, which Roslyn renames to `<Outer>g__Name|n_m`.
; Func.Name reports the exact name the script wrote, which is also what a binder error naming it prints.
outerFn() {
	nestedFn(a) => a
	return nestedFn
}
AssertEq(outerFn().Name, "nestedFn", A_LineNumber)
AssertEq(((x) => x).Name, "", A_LineNumber)  ; an anonymous lambda has no name a script could write

FileAppend "pass", "*"
