#NoTrayIcon
; Named arguments: `f(Name: value)`. The script deliberately misspells names in the error cases below, so the
; build-time check is turned off here -- those cases assert the RUNTIME binder rejects them.
#Warn NamedArg, Off
; The carrier type itself, needed only to build a named call dynamically or inspect a collected one.
#import KS { NamedArgs, Clr }

Check(cond) => FileAppend(cond ? "pass" : "fail", "*")

Throws(fn) {
	try
		fn()
	catch
		return true
	return false
}

; ---------------------------------------------------------------- user functions
f3(a, b := "B", c := "C") => a . "/" . b . "/" . c

Check(f3("A", c: "Z") == "A/B/Z")            ; fills a gap, leaving b at its default
Check(f3("A", c: "Z", b: "Y") == "A/Y/Z")    ; named arguments out of parameter order
Check(f3(a: "A") == "A/B/C")                 ; every argument named
Check(f3("A", "B", "C") == "A/B/C")          ; unaffected when none are named

; Evaluation order is SOURCE order even when the names are out of parameter order: the values enter the
; argument array left to right and are permuted only at bind time.
order := ""
tag(s) {
	global order
	order .= s
	return s
}
Check(f3("A", c: tag("1"), b: tag("2")) == "A/2/1")
Check(order == "12")

; ---------------------------------------------------------------- built-ins
Check(SubStr("abcdef", startingPos: 2) == "bcdef")
Check(SubStr("abcdef", startingPos: 2, length: 3) == "bcd")
Check(InStr("hello world", "O", caseSense: true) == 0)         ; case-sensitive: no match
Check(InStr("hello world", "O", caseSense: false) == 5)

Clr.Load("System")
Check(Clr.System.Convert.ToString(value: 255, toBase: 16) == "ff")
Check(Clr.System.Convert.ToString(value: 255) == "255")
Check(Clr.System.Math.Round(value: 2.567, digits: 2) == 2.57)

; StartsWith/EndsWith follow InStr's CaseSense convention: insensitive by default, and both On and Off
; are culture-invariant.
Check("Hello".StartsWith("he") == 1)                           ; default is case-INsensitive
Check("Hello".StartsWith(token: "he") == 1)
Check("Hello".StartsWith("he", caseSense: true) == 0)          ; On -> case-sensitive
Check("Hello".StartsWith("He", caseSense: true) == 1)
Check("Hello".EndsWith("LO") == 1)
Check("Hello".EndsWith("LO", caseSense: true) == 0)
Check("Hello".EndsWith(token: "lo", caseSense: false) == 1)

; A parameter whose C# name is an escaped keyword (`@default`) still binds by the name scripts see.
arr := [10, 20, 30]
Check(arr.Get(index: 2) == 20)

sparse := Array()
sparse.Length := 3                             ; all three elements unset, so Get falls back to the default
Check(sparse.Get(2, "fallback") == "fallback")
Check(sparse.Get(index: 2, default: "fallback") == "fallback")

m := Map("a", 1)
Check(m.Get(key: "a") == 1)
Check(m.Get(key: "zz", default: 7) == 7)

; OwnProps takes no arguments: how many values an iteration yields is decided by the for-loop's
; variable count, not by a parameter.
ownPropsProbe := {alpha: 1}
Check(Throws(() => ownPropsProbe.OwnProps(true)))

; Array.Filter's StartIndex is a documented Keysharp extension.
Check([1, 2, 3, 4].Filter((v, i) => v > 1, startIndex: 3).Length == 2)

; A collection callback may declare only the parameters it needs, whatever shape it is. The arity has to come
; from the member that will actually run: an ObjBindMethod carries a placeholder signature that claims to be
; variadic, and a callable object has no signature of its own.
Check([1, 2, 3].Filter((v) => v > 1).Length == 2)
Check([1, 2, 3].Filter((v, i) => v > 1).Length == 2)
Check([1, 2, 3].Filter(() => true).Length == 3)
Check([1, 2, 3].Filter((a*) => a[1] > 1).Length == 2)
Check([1, 2, 3].MapTo((v) => v * 2).Join(",") == "2,4,6")
Check([1, 2, 3].FindIndex((v) => v > 1) == 2)
class NPPred {
    Pred(v) => v > 1
    static SPred(v) => v > 1
    Call(v) => v > 1
}
npp := NPPred()
Check([1, 2, 3].Filter(ObjBindMethod(npp, "Pred")).Length == 2)
Check([1, 2, 3].Filter(npp.Pred).Length == 2)
Check([1, 2, 3].Filter(NPPred.SPred).Length == 2)
Check([1, 2, 3].Filter(npp).Length == 2)           ; callable object
np2(a, b) => b > a
Check([1, 2, 3].Filter(np2.Bind(1)).Length == 2)   ; bound: one slot already filled

; A transform may yield nothing for an element: that is how a sparse array is built. A predicate, by contrast,
; still must return something.
sparseM := [1, 2, 3].MapTo((v) => v = 2 ? unset : v)
Check(sparseM.Length == 3 && !sparseM.Has(2) && sparseM[1] == 1)

; The `object @this` convention: the receiver is not an argument and must not be bindable.
o := {alpha: 1}
Check(o.HasProp(name: "alpha") == 1)

; ---------------------------------------------------------------- classes
class NPWidget {
	__New(x := 1, y := 2) {
		this.x := x, this.y := y
	}
	Sum(m := 1, n := 0) => this.x * m + this.y + n
	static Make(a := 5, b := 6) => a * 100 + b
}

w := NPWidget(y: 40)                          ; construction relays through Class.Call to __New
Check(w.x == 1 && w.y == 40)
Check(NPWidget(x: 7, y: 8).y == 8)
Check(w.Sum(n: 100) == 141)                   ; instance method
Check(NPWidget.Make(b: 9) == 509)             ; static method

; ---------------------------------------------------------------- closures
mk(p) => (q := 5, (r := 0) => p . "-" . q . "-" . r)
Check(mk("P")(r: 9) == "P-5-9")

; ---------------------------------------------------------------- by-ref and spread
Check(RegExMatch("abc123", "\d+", outputVar: &mm) > 0 && mm[0] == "123")

rest := ["A", "B"]
Check(f3(rest*, c: "Z") == "A/B/Z")           ; spread contributes positionals, named still trails

; ---------------------------------------------------------------- bound functions
bf := f3.Bind("A")
Check(bf(c: "Q") == "A/B/Q")                  ; named argument supplied at CALL time

bn := f3.Bind(c: "CC")                        ; bound BY NAME
Check(bn("A", "BB") == "A/BB/CC")
Check(bn.MinParams == 1 && bn.MaxParams == 2) ; binding optional `c` drops Max but not Min

bn2 := f3.Bind(a: "A")                        ; binding a REQUIRED parameter drops both
Check(bn2.MinParams == 0 && bn2.MaxParams == 2)
; A name binds a SLOT, so later positional arguments must fill the next FREE one rather than colliding
; with it. Binding an early parameter by name is the case that gets this wrong if slots aren't tracked.
Check(bn2("BB") == "A/BB/C")
Check(bn2("BB", "CC") == "A/BB/CC")
Check(f3.Bind(a: "A").Bind("BB")() == "A/BB/C")
Check(bn2.Params.Length == 2)              ; and Params agrees with MinParams/MaxParams
Check(bn2.Params[1].Name == "b" && bn2.Params[2].Name == "c")
vb(head, rest*) => head . "|" . rest.Length
Check(vb.Bind(head: 1)(2, 3) == "1|2")
Check(Throws(() => f3.Bind("A").Bind(a: "X")))  ; already supplied positionally

bn3 := f3.Bind(c: "CC").Bind("A")             ; positional bind after a named one
Check(bn3("BB") == "A/BB/CC")
Check(Throws(() => f3.Bind(c: "1").Bind(c: "2")))   ; already bound by name

; An omitted slot -- in Bind or in a call -- holds nothing, so a name may fill it; only a slot holding a real
; value collides.
hole := f3.Bind(, , "ZZ")
Check(hole("AA", "BB") == "AA/BB/ZZ")         ; the holes still fill positionally
Check(hole(a: "AA") == "AA/B/ZZ")             ; ... and by name
Check(hole("AA", b: "BB") == "AA/BB/ZZ")      ; ... and by both at once
Check(f3.Bind(, , "ZZ").Bind(a: "AA")() == "AA/B/ZZ")
Check(Throws(() => f3.Bind("XX")(a: "AA")))   ; a slot Bind actually FILLED is still a collision

; ObjBindMethod has no signature at bind time, so the name is resolved at CALL time instead.
obm := ObjBindMethod(w, "Sum")
Check(obm(n: 100) == 141)
Check(Throws(() => ObjBindMethod(w, "Sum")(nope: 1)))

; ---------------------------------------------------------------- rejections
Check(Throws(() => f3("A", nosuch: 1)))            ; unknown name
Check(Throws(() => f3("A", "B", a: "X")))          ; already supplied positionally
Check(Throws(() => f3("A", , a: "X")))             ; ... including with an unrelated omitted slot in between

Check(Throws(() => w.Sum(this: 1)))                ; the receiver is not an argument

; An omitted slot supplied nothing, so a name may fill it -- the call-site twin of Bind's holes.
Check(f3(, a: "X") == "X/B/C")
Check(f3("A", , c: "Z") == "A/B/Z")

; ---------------------------------------------------------------- forwarding through a variadic
; A call's names travel as one ordinary value, so a variadic parameter collects it like any other element and a
; spread re-emits it -- that IS the relay mechanism. The tail itself is not bindable; naming it just puts a name
; called "rest" in the container.
variadic(head, rest*) => head "|" rest.Length "|" (rest.Length && rest[1] is NamedArgs ? rest[1]["rest"] : "-")
Check(variadic(1, rest: 2) == "1|1|2")

NPInner(text, title := "T", opts := "O") => text "/" title "/" opts
NPWrap(args*) => NPInner(args*)
Check(NPWrap("hi") == "hi/T/O")                    ; the pattern this exists for
Check(NPWrap("hi", title: "X") == "hi/X/O")
Check(NPWrap(text: "hi", opts: "Z") == "hi/T/Z")   ; nothing positional at all
Check(NPWrap("hi", TITLE: "X") == "hi/X/O")        ; names are case-insensitive

NPWrap2(first, rest*) => first "|" NPInner(rest*)  ; a wrapper with its own leading parameter
Check(NPWrap2("A", "hi", opts: "Q") == "A|hi/T/Q")
NPWrap3(a := "d", rest*) => a "|" NPInner("hi", rest*)
Check(NPWrap3(title: "N") == "d|hi/N/O")           ; the name skips the declared parameter into the tail

; The collected container is an ordinary trailing element: countable, indexable, and inspectable. Inspection
; goes through its own properties and methods, never by passing it TO something -- as the last argument of any
; call it would name that callee's parameters instead, which is exactly the contract.
NPPeek(args*) => args.Length "," (args[3] is NamedArgs) "," args[3].Has("title") "," args[3]["title"]
Check(NPPeek("hi", "there", title: "X") == "3,1,1,X")

; A misspelling is caught by the function that actually declares the parameters, not by the relay.
Check(Throws(() => NPWrap("hi", nosuch: 1)))

; A container built by hand is a named argument wherever it lands last -- in a direct call, or riding an
; array into a spread. This is how a named call is made dynamically.
Check(NPInner("hi", NamedArgs("title", "D")) == "hi/D/O")
manual := ["hi", NamedArgs("title", "M")]
Check(NPInner(manual*) == "hi/M/O")

; A name whose spelling is itself computed is an ordinary item assignment.
dyn := NamedArgs()
key := "title"
dyn[key] := "DYN"
Check(NPInner("hi", dyn) == "hi/DYN/O")

; Names are Map KEYS, so they share no namespace with the type's own members: a parameter called `base` or
; `clone` is supplied like any other, by every route. This is what an Object-based carrier could not do.
collide(base := "-", clone := "-") => base clone
Check(collide(base: "B", clone: "C") == "BC")             ; the literal syntax
Check(collide(NamedArgs("base", "B", "clone", "C")) == "BC")
Check(collide(NamedArgs(["base", "B", "clone", "C"]*)) == "BC")
item := NamedArgs()
item["base"] := "B", item["clone"] := "C"
Check(collide(item) == "BC")                              ; ... and by item assignment

; Names are matched case-insensitively, like parameter names, whichever way the container was built -- a
; plain Map is COPIED, not adopted, so neither its case mode nor its storage leaks in.
ci := NamedArgs("TITLE", "T")
Check(ci["title"] == "T" && NPInner("hi", ci) == "hi/T/O")
fromMap := NamedArgs(Map("TITLE", "T"))            ; source Map is case-SENSITIVE
Check(fromMap.CaseSense = "Off" && fromMap["title"] == "T")
mapSrc := Map("title", "T")
copied := NamedArgs(mapSrc)
mapSrc["opts"] := "LEAKED"                          ; mutating the source must not reach the container
Check(NPInner("hi", copied) == "hi/T/O")

; A name added or removed while the container is being read cannot corrupt the walk -- what binding reads is
; a snapshot, not the live store.
mutating := NamedArgs("title", "M")
NPMutate(text, title := "T", opts := "O") {
	mutating["opts"] := "LATE"
	return text "/" title "/" opts
}
Check(NPMutate("hi", mutating) == "hi/M/O")

; A subclass that declares its own __Enum reshapes what binds, exactly as it reshapes what a `for` loop
; yields: the names it enumerates are the ones supplied, whatever it actually stores.
class NPReshaped extends NamedArgs {
	__Enum(n) => Map("opts", "FROM_ENUM").__Enum(n)
}
reshaped := NPReshaped()
reshaped["title"] := "stored-but-not-enumerated"
Check(NPInner("hi", reshaped) == "hi/T/FROM_ENUM")

; An empty container is not an argument, so a variadic collects nothing from it -- whether or not the callee
; declares a bindable parameter of its own (the two took different paths before).
emptyRelay(rest*) => rest.Length
emptyMixed(a := 0, rest*) => rest.Length
Check(emptyRelay(NamedArgs()) == 0 && emptyMixed(NamedArgs()) == 0)

; A BoundFunc takes a SNAPSHOT of the names bound to it, exactly as it does of the values bound
; positionally: a container the caller keeps a reference to is not the one the BoundFunc re-emits.
bsrc := NamedArgs("t", 1)
bsnap(head, rest*) => rest[1]["t"]
bsnapf := bsnap.Bind(bsrc)
bsrc["t"] := 99
Check(bsnapf(0) == 1)

; A variadic built-in absorbs an undeclared name the same way a script variadic does: as DATA. Format prints
; the container's string form, Push stores it, Max fails converting it to a number -- there is no special
; rejection, because there is no special type of tail.
Check(Format("{1}", bogus: 5) == "bogus: 5")
pushProbe := [1, 2]
pushProbe.Push(x: 3)
Check(pushProbe.Length == 3 && pushProbe[3] is NamedArgs && pushProbe[3]["x"] == 3)
Check(Throws(() => Max(1, 2, nosuch: 9)))

; Whether a container is a named argument is decided purely by POSITION: named arguments come after positional
; ones, so only the LAST argument binds by name. A container followed by a positional argument -- written
; directly or deposited by a spread -- is itself an ordinary positional VALUE.
posNA(x, y := "-", z := "-") => (x is NamedArgs ? "N:" x.ToString() : x) "/" (y is NamedArgs ? "N:" y.ToString() : y) "/" z
Check(posNA("a", NamedArgs("b", 1), "c") == "a/N:b: 1/c")   ; mid-list container is positional data
Check(posNA("a", NamedArgs("z", "Q")) == "a/-/Q")           ; ... while a trailing one binds by name
Check(posNA("a", NamedArgs("y", 1, "z", 2)) == "a/1/2")     ; one container carries every name of the call
Check(posNA(manual*, "T2") == "hi/N:title: M/T2")           ; a spread's container before a later positional: data
mEmpty := [NamedArgs("opts", "B")]
Check(posNA(mEmpty*, "hi") == "N:opts: B/hi/-")

; A function with an attached receiver reports the CALLER-visible signature: attaching consumes the
; receiver slot (the same accounting Bind does for bound arguments), so Filter passes one argument to a
; one-parameter method instead of failing on every element.
hasMap := Map(2, "x")
hasFn := Func("Has", hasMap)
Check(hasFn.MinParams == 1 && hasFn.MaxParams == 1)
Check([1, 2, 3].Filter(hasFn).Length == 1)

; Binding into a variadic function must not swallow the tail from Params: the tail is never used up.
vfp(rest*) => rest.Length
bvfp := vfp.Bind(1)
Check(bvfp.IsVariadic && bvfp.Params.Length == 1 && bvfp.Params[1].Variadic == 1)

; Supplying the same parameter by name twice across Bind and the call reports "more than once" -- at that
; point the first supply is indistinguishable from a positional one, so the message must not claim either.
dupErr := ""
try
	vb.Bind(head: 1)(head: 2)
catch as e
	dupErr := e.Message
Check(InStr(dupErr, "'head'") && InStr(dupErr, "more than once"))

; In an ARRAY LITERAL a spread's container is plain data, like everywhere outside binding.
manlit := [manual*]
Check(manlit.Length == 2 && manlit[1] == "hi" && manlit[2] is NamedArgs)

; Two spreads: values interleave in source order, and both containers end up trailing, so their names are
; unioned and all bind. Adding a name of one's own to a forwarded call is the same shape.
Check(NPInner(manual*, mEmpty*) == "hi/M/B")
Check(NPInner(manual*, opts: "Z") == "hi/M/Z")

; A spread call evaluated INSIDE another spread call's argument list cannot disturb the outer one.
NPNest() {
    t := ["q1", NamedArgs("title", "n")]
    return NPInner(t*)
}
Check(NPInner(NPNest(), mEmpty*) == "q1/n/O/T/B")

; The same name arriving from two different spreads is the ordinary specified-twice error.
mDup := [NamedArgs("title", "DUP")]
Check(Throws(() => NPInner(manual*, mDup*)))

; ---------------------------------------------------------------- built-in constructors
; A built-in __New declares its real, documented signature (Buffer.__New(ByteCount, FillByte)), so its names
; bind through the same binder as any script function's -- same rules, same errors.
Check(Buffer(ByteCount: 16).Size == 16)
bfill := Buffer(8, FillByte: 5)
Check(NumGet(bfill, 0, "UChar") == 5)             ; mixed positional + named
Check(Throws(() => Buffer(16, ByteCount: 8)))     ; supplied twice is the usual collision
Check(Throws(() => Buffer(NoSuch: 1)))
Check(Error(Message: "m", Extra: "x").Extra == "x")
Check(TypeError(Message: "t").Message == "t")     ; subclasses inherit the names through Error.__New
Check(InputHook(Options: "V").VisibleText == 1)
avar := Array(x: 1)                               ; a truly variadic constructor absorbs, like any variadic
Check(avar.Length == 1 && avar[1] is NamedArgs)

; ---------------------------------------------------------------- classes with no __New to bind against
; Object() reads the names as name/value pairs, so the call form agrees with the literal.
Check(Object(x: 1).x == 1)
o2 := Object("k", 1, v: 2)
Check(o2.k == 1 && o2.v == 2)
; A dangling name errors rather than being dropped -- which is also what happens when a container was meant as
; a pair's VALUE: a trailing one reads as a named argument, leaving its would-be name unpaired. The literal
; routes through the same construction, so it errors the same way (loudly, never a silent drop).
Check(Throws(() => Object("a")))
Check(Throws(() => Object("a", NamedArgs("x", 5))))
Check(Throws(() => ({a: NamedArgs("x", 5)})))
class NPNoNew {
}
npn := NPNoNew(a: 1)                              ; no __New: the default variadic one absorbs and ignores
Check(Type(npn) == "NPNoNew")

; Binding a name a variadic target does not declare defers it, exactly as supplying it at the call would.
vfn2(head, rest*) => head "|" (rest.Length && rest[1] is NamedArgs ? rest[1]["t"] : "-")
Check(vfn2(1, t: 2) == "1|2")
Check(vfn2.Bind(1)(t: 2) == "1|2")
Check(vfn2.Bind(t: 2)(1) == "1|2")

; Naming a later parameter while leaving a REQUIRED earlier one unfilled is an ordinary catchable error, not
; a crash: the gap the name skipped over is exactly what `f3(, "B")` leaves, and it reports the same way.
reqErr := ""
try
	f3(c: "Z")
catch as e
	reqErr := e.Message
Check(InStr(reqErr, "'a'") && InStr(reqErr, "required"))

; ---------------------------------------------------------------- non-ASCII identifiers
; The emitted C# parameter is lower-cased and matched case-insensitively, which works for identifiers
; whose case folds symmetrically. U+1E9E folds to U+00DF, which OrdinalIgnoreCase does not match back --
; and which characters behave this way depends on the runtime's Unicode tables. The declared spelling is
; carried on the parameter so the name written in the script always binds.
sharpS(ẞ := "-") => ẞ
Check(sharpS(ẞ: "v") == "v")
uni(Ω := "-", naïve := "-", 日本語 := "-") => Ω . naïve . 日本語
Check(uni(Ω: "a", 日本語: "c") == "a-c")
Check(uni(ω: "a", NAÏVE: "b") == "ab-")            ; case-insensitive where the fold is symmetric
kw(class := "-", params := "-") => class . params  ; C# keywords: the '@' escape must not leak into the name
Check(kw(class: "x", params: "y") == "xy")

; ---------------------------------------------------------------- NamedArgs as a value
; The carrier type is script-visible, like VarRef: a container passed LAST is a named argument, everywhere, so
; the position is reserved for that role. Inspection goes through properties and `is`, never through a call --
; passing one to Type() would name one of Type's parameters, which is the contract working as stated.
naProbe := NamedArgs("k", 5)
Check((naProbe is NamedArgs) && (naProbe is Object))
Check(naProbe["k"] == 5 && naProbe.Has("k"))
Check(f3("A", NamedArgs()) == "A/B/C")             ; an empty container is legal and binds nothing
Check(Throws(() => Type(naProbe)))                 ; a trailing container names Type's parameter -- by design
Check(Throws(() => NamedArgs("a")))                ; a dangling name, like Object()

; NamedArgs is extensible: a subclass instance IS a NamedArgs, so it binds like one.
class NPTagged extends NamedArgs {
	Tag => "t"
}
tagged := NPTagged("c", "Q")
Check((tagged is NamedArgs) && tagged.Tag == "t")
Check(f3("A", tagged) == "A/B/Q")

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
Check(NASink.got is NamedArgs && NASink.got["x"] == 1)
for nv in [NamedArgs("y", 2)]
	Check(nv is NamedArgs && nv["y"] == 2)

; ---------------------------------------------------------------- dynamic names
; `%x%: v` and the name-building `a%b%c: v` compute the parameter name at run time, exactly as the same text
; computes an object-literal key. Only the NAME is dynamic -- everything else about the argument is unchanged.
dname(alpha, beta := "B", gamma := "G") => alpha . "/" . beta . "/" . gamma

nm := "gamma"
Check(dname("A", %nm%: "Z") == "A/B/Z")            ; whole name from a variable
Check(dname("A", b%"et"%a: "Y") == "A/Y/G")        ; built from literal text around a deref
mid := "amm"
Check(dname("A", g%mid%a: "Z") == "A/B/Z")         ; ... and from a variable in the middle
Check(dname("A", beta: "Y", %nm%: "Z") == "A/Y/Z") ; mixed with a literal name
Check([1, 2, 3].Get(%"index"%: 2) == 2)            ; on a method call
Check(dname("A", %nm%: "Z", ) == "A/B/Z")          ; a trailing comma still adds no slot

; The name is evaluated where it is written -- before its own value, and after the positional arguments.
order := ""
Check(dname(tag("A"), %tag("gamma")%: tag("V")) == "A/B/V")
Check(order == "AgammaV")

; A name that matches no parameter is the ordinary binder error; it simply cannot be caught at build time.
bad := "nosuch"
Check(Throws(() => dname("A", %bad%: 1)))

; Two derefs yielding one name collapse last-wins, as two object-literal keys would. The parser rejects that
; when both names are written literally, but it cannot see it here, and neither can the binder: what reaches it
; is a single entry.
n1 := "gamma", n2 := "gamma"
Check(dname("A", %n1%: "X", %n2%: "Y") == "A/B/Y")
Check(dname("A", gamma: "X", %n2%: "Y") == "A/B/Y")

; ---------------------------------------------------------------- Func.Params
pinfo(alpha, beta := 5, &out?, rest*) => alpha
pi := pinfo.Params
Check(pi.Length == 4)
Check(pi[1].Name == "alpha" && pi[1].Optional == 0 && !pi[1].HasOwnProp("Default"))
Check(pi[2].Name == "beta" && pi[2].Optional == 1 && pi[2].Default == 5)
Check(pi[3].Name == "out" && pi[3].ByRef == 1)
Check(pi[4].Name == "rest" && pi[4].Variadic == 1)
Check(SubStr.Params[1].Name == "string")       ; built-ins report their documented names
Check([].Get.Params[1].Name == "index")        ; the receiver is not reported

; ---------------------------------------------------------------- Func.Name
; A nested function or closure is lowered to a local function, which Roslyn renames to `<Outer>g__Name|n_m`.
; Func.Name reports the name the script wrote, which is also what a binder error naming it prints. Compared
; case-insensitively, like every other name in the language: a nested function is lowered under a lower-cased
; identifier, so only a top-level one round-trips its exact casing.
outerFn() {
	nestedFn(a) => a
	return nestedFn
}
Check(outerFn().Name = "nestedFn")
Check(((x) => x).Name == "")                       ; an anonymous lambda has no name a script could write
