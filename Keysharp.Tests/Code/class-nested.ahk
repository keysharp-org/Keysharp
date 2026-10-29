#NoTrayIcon
#Include <assert>

class c1 {
    class c2 {
        test() {
            global a := 5
        }
        static test() {
            global b := 6
        }
        statictest() {
            global c := 7
        }
        static statictest() {
            global d := 8
        }
    }
    test() {
        global a := 1
    }
    static test() {
        global b := 2
    }
    statictest() {
        global c := 3
    }
    static statictest() {
        global d := 4
    }
}

a := 0, b := 0, c := 0, d := 0

c1().test()
Assert(a == 1 && b == 0 && c == 0 && d == 0, A_LineNumber)

c1.test()
Assert(a == 1 && b == 2 && c == 0 && d == 0, A_LineNumber)
c1().statictest()
Assert(a == 1 && b == 2 && c == 3 && d == 0, A_LineNumber)
c1.statictest()
Assert(a == 1 && b == 2 && c == 3 && d == 4, A_LineNumber)

a := 0, b := 0, c := 0, d := 0

c1.c2().test()
Assert(a == 5 && b == 0 && c == 0 && d == 0, A_LineNumber)
c1.c2.test()
Assert(a == 5 && b == 6 && c == 0 && d == 0, A_LineNumber)

c1.c2().statictest()
Assert(a == 5 && b == 6 && c == 7 && d == 0, A_LineNumber)

c1.c2.statictest()
Assert(a == 5 && b == 6 && c == 7 && d == 8, A_LineNumber)

; Type() names an instance by the __Class its class declares, so a nested class reports its full
; dotted path whether or not the instance carries own properties, and reassigning a Prototype's
; __Class renames every instance of it.
class outer {
    class empty {
    }
    class field {
        x := 1
    }
    class deep {
        class deeper {
        }
    }
}

class subofnested extends c1.c2 {
}

AssertEq(Type(outer.empty()), "outer.empty", A_LineNumber)
AssertEq(outer.empty().__Class, "outer.empty", A_LineNumber)
AssertEq(Type(outer.field()), "outer.field", A_LineNumber)
AssertEq(Type(outer.deep.deeper()), "outer.deep.deeper", A_LineNumber)
AssertEq(Type(c1.c2()), "c1.c2", A_LineNumber)
AssertEq(Type(subofnested()), "subofnested", A_LineNumber)
AssertEq(Type(outer.empty), "Class", A_LineNumber)
AssertEq(Type(outer.empty.Prototype), "Prototype", A_LineNumber)

renamed := outer.empty()
outer.empty.Prototype.__Class := "Override"
AssertEq(Type(renamed), "Override", A_LineNumber)
AssertEq(Type(outer.empty()), "Override", A_LineNumber)

; A built-in nested class is named the same way (AutoHotkey reports Gui.Text for a text control), and the
; short name of a nested class resolves nowhere: dynamically it is an ordinary undefined name, and the
; declaring class is what reaches it.
AssertEq(Gui.Control.Prototype.__Class, "Gui.Control", A_LineNumber)
AssertEq(Gui.Text.Prototype.__Class, "Gui.Text", A_LineNumber)
AssertEq(Type(Gui.Text.Prototype), "Prototype", A_LineNumber)

_undefinedTag := ""
try
    %"NoSuchNameAtAll"%
catch as _e
    _undefinedTag := Type(_e)

_nestedTag := ""
try
    %"empty"%
catch as _e
    _nestedTag := Type(_e)
AssertEq(_nestedTag, _undefinedTag, A_LineNumber)

_builtinNestedTag := ""
try
    %"Control"%
catch as _e
    _builtinNestedTag := Type(_e)
AssertEq(_builtinNestedTag, _undefinedTag, A_LineNumber)

; A top-level class still resolves dynamically, and a nested one through its declaring class.
AssertEq(Type(%"outer"%), "Class", A_LineNumber)
AssertEq(Type(%"Gui"%), "Class", A_LineNumber)
AssertEq(Type(outer.%"field"%), "Class", A_LineNumber)
AssertEq(Type(Gui.%"Control"%), "Class", A_LineNumber)

; `is` names a class through the class itself: a string is a type error, so a dynamic reference has to
; resolve the class first.
AssertEq(outer.empty() is outer.empty, 1, A_LineNumber)
AssertEq(outer.empty() is %"outer"%.empty, 1, A_LineNumber)
AssertEq(c1.c2() is c1.c2, 1, A_LineNumber)
AssertEq(c1.c2() is c1, 0, A_LineNumber)
Throws(() => outer.empty() is "outer.empty", A_LineNumber, TypeError)
Throws(() => [] is "Array", A_LineNumber, TypeError)

; A global a nested class reads is not hidden by its enclosing class's method of the same C# name.
_4 := "global _4"
class outerwithshared {
    _4() => "method _4"
    class inner {
        static Read() => _4
    }
}
AssertEq(outerwithshared.inner.Read(), "global _4", A_LineNumber)

; A class declared in a block outside any function is global, as in AHK.
if (true) {
    class blockclass {
        static Value := 7
        class inner {
            static Value := 8
        }
    }
}
{
    class blockderived extends blockclass {
    }
}
AssertEq(blockclass.Value, 7, A_LineNumber)
AssertEq(blockclass.inner.Value, 8, A_LineNumber)
AssertEq(blockderived.Value, 7, A_LineNumber)
AssertEq(Type(blockderived()), "blockderived", A_LineNumber)
switch 1 {
case 1:
    class caseclass {
        static Value := 9
    }
}
AssertEq(caseclass.Value, 9, A_LineNumber)
; It initializes where it is declared, when execution reaches it.
blockinitorder := ""
{
    blockinitorder .= "a"
    class orderedclass {
        static __New() {
            global blockinitorder .= "i"
        }
    }
    blockinitorder .= "b"
}
AssertEq(blockinitorder, "aib", A_LineNumber)
; A nested class is a property whose getter takes the outer class alone, as in AHK.
nestedgetter := outerwithshared.GetOwnPropDesc("inner").Get
AssertEq(nestedgetter.MinParams "," nestedgetter.MaxParams "," nestedgetter.IsVariadic, "1,1,0", A_LineNumber)

FileAppend "pass", "*"
