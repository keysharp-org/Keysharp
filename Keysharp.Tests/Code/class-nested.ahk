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

FileAppend "pass", "*"
