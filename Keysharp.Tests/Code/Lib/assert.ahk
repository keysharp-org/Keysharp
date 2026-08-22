;
; Shared assertion helpers for the test scripts in Code/. Include with `#Include <assert>`.
;
; A test script stays silent while it succeeds and writes a single "pass" when it reaches its end, so the
; runner can tell a completed run from one that died halfway. A failing assertion writes "fail <tag>"
; instead, where the tag locates it: pass A_LineNumber (folded to a literal at compile time) for the
; source line, or omit it to be tagged with the assertion's ordinal within the run.
;
; Every name below is underscore-prefixed because these helpers are included into ~270 scripts and a plain
; name here would shadow a same-named global in some of them, which #Warn LocalSameAsGlobal reports.
;

; Writes "fail <tag>" unless _cond is truthy. A callable _cond is invoked first, so a condition that has to
; be evaluated lazily can be passed as a closure.
Assert(_cond, _line?)
{
	static _n := 0
	_n += 1

	if HasMethod(_cond)
		_cond := _cond()

	if !_cond
		FileAppend("fail " . (IsSet(_line) ? "line " . _line : "N" . _n) . "`n", "*")
}

; Writes "fail <tag> (got <x> want <y>)" unless the two values are equal. Prefer this to Assert for an
; equality check: the failure names both values, which a bare condition cannot. Comparison is `==`, so a
; check that deliberately relies on `=`'s case-insensitive string match belongs in Assert instead.
; Both operands are optional so that `AssertEq(x, unset, A_LineNumber)` works: passing `unset` omits the
; parameter rather than handing over a value, which a required parameter rejects outright.
AssertEq(_actual?, _expected?, _line?)
{
	static _n := 0
	_n += 1

	; unset counts as a value: two unsets match each other and never match anything else.
	if (IsSet(_actual) && IsSet(_expected))
	{
		if (_actual == _expected)
			return
	}
	else if (!IsSet(_actual) && !IsSet(_expected))
		return

	FileAppend("fail " . (IsSet(_line) ? "line " . _line : "E" . _n)
		. " (got <" . _AssertShow(_actual?) . "> want <" . _AssertShow(_expected?) . ">)`n", "*")
}

; An object has no useful text form and concatenating one can throw, so it is shown by type.
_AssertShow(_v?) => !IsSet(_v) ? "unset" : IsObject(_v) ? "<" . Type(_v) . ">" : _v

; Writes "fail <tag>" unless calling _cb throws — and, when _expected is given, throws that error class. The
; inverse — "this must not throw" — is a plain try/catch around the statement with Assert(false, A_LineNumber)
; in the catch.
Throws(_cb, _line?, _expected?)
{
	static _n := 0
	_n += 1
	_tag := IsSet(_line) ? "line " . _line : "T" . _n

	try
		_cb()
	catch Any as _err
	{
		if !IsSet(_expected) || _err is _expected
			return

		FileAppend("fail " . _tag . " (threw " . Type(_err) . ")`n", "*")
		return
	}

	FileAppend("fail " . _tag . " (no throw)`n", "*")
}
