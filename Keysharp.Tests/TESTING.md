# Keysharp test conventions

Tests exercise the highest stable boundary that can deterministically prove the behavior.

## Script-visible behavior

AHK syntax, built-ins, classes, directives, and runtime behavior belong in `Code/*.ahk`. A small NUnit method should invoke the script through `TestScript(...)`. This path covers lexing, parsing, lowering, compilation, dynamic dispatch, and runtime behavior together.

New test scripts must include both directives below so failures cannot open a modal dialog:

```ahk
#ErrorStdOut
#Warn All, StdOut
```

### Assertions

A script is silent while it succeeds and writes a single `pass` once it reaches its end. The runner accepts
nothing else: a non-zero exit code, empty output, a `fail` tag, or an error trace all fail the test, and so does
a run that stopped partway and never reached the final `pass`.

```ahk
#Include <assert>

AssertEq(arr[1], 10, A_LineNumber)               ; writes "fail line 12 (got <11> want <10>)"
Assert(x > 0 && y < 10, A_LineNumber)            ; writes "fail line 13"
Throws(() => arr[99], A_LineNumber)              ; writes "fail line 14 (no throw)"
Throws(() => Foo(), A_LineNumber, TypeError)     ; ... and names the wrong class when one is given
...
FileAppend "pass", "*"                           ; last thing the script runs
```

Reach for `AssertEq` whenever the check is an equality: it names both values, so a failure needs no second
run to find out what was actually produced. `Assert` is for everything else — compound conditions, `is`
tests, inequalities — where there is no single pair of values to show. `AssertEq` compares with `==`, so a
check that genuinely depends on `=`'s case-insensitive string match must stay an `Assert`.

`A_LineNumber` folds to a literal at compile time, so the tag costs nothing and can never go stale. Omit it and
the failure is tagged with the assertion's ordinal instead. For "this must not throw", use a plain `try`/`catch`
with `Assert(false, A_LineNumber)` in the catch.

Put the final `pass` where the script really ends — ahead of a top-level `ExitApp`, or ahead of the first
`#Module` block — not merely on the last line. A script that another one `#include`s never writes its own.

Keep the branch structure when the branch structure is what is under test (`flow-if.ahk`): record which branch
ran in a variable and assert on that afterwards, rather than collapsing the construct into one `Assert`.

Use `RunScript(...)` directly only when a test needs generated source, separate compilations, diagnostic text, an exit code, or another result that `TestScript(...)` cannot expose.

## Internal contracts

A direct C# test is appropriate when the behavior cannot be tested safely or deterministically from a script. Examples include compiler diagnostics, protocol framing, injected clocks, concurrent state machines, native event construction, and platform adapters that would otherwise need hardware or permissions.

Mark implementation-facing contracts or their fixture with `Category("Internal")`; public compiler and command-line boundary tests do not need that label. Add `Category("Curated")` only when the entire selected scope is deterministic, non-interactive, and safe on every platform where it compiles.

Do not assert token sequences, AST printer output, generated C# text, reflected field layout, or private helper behavior when an executable script can prove the same contract. Keep a representation-level assertion only when that representation is itself the boundary between components.

## Names and coverage

- Name tests after the feature or compact scenario, such as `PowerPrecedence`, `NamedArgErrors`, or `RetryLimit`.
- Put detailed inputs and expectations in the test body, assertion message, or `[TestCase]` data rather than encoding a sentence in the method name.
- Prefer one behavior script with related checks over many one-assertion C# methods.
- Before keeping an internal test, check whether an existing behavior script already covers its observable result. Remove redundant coverage.
- When fixing a regression, add the script-level test first. Add an internal test only when it contributes deterministic coverage the script cannot provide.

## Execution safety

The full suite includes interactive and permission-sensitive fixtures. Run the curated filter from the repository `AGENTS.md`, a narrower category, or a specific test. Never add `[Parallelizable]`; the suite shares `Script.TheScript` state.
