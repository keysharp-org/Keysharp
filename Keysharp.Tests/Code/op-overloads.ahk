#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#Include <assert>
#Import Ks { Boolean }

class Operators {
    Value := 100
    +(Right) => this.Value + Right
    -(Right) => this.Value + 1 + Right
    *(Right) => this.Value + 2 + Right
    /(Right) => this.Value + 3 + Right
    //(Right) => this.Value + 4 + Right
    **(Right) => this.Value + 5 + Right
    &(Right) => this.Value + 6 + Right
    |(Right) => this.Value + 7 + Right
    ^(Right) => this.Value + 8 + Right
    <<(Right) => this.Value + 9 + Right
    >>(Right) => this.Value + 10 + Right
    >>>(Right) => this.Value + 11 + Right
    <(Right) => this.Value + 12 + Right
    <=(Right) => this.Value + 13 + Right
    >(Right) => this.Value + 14 + Right
    >=(Right) => this.Value + 15 + Right
    =(Right) => this.Value + 16 + Right
    ==(Right) => this.Value + 17 + Right
    !=(Right) => this.Value + 18 + Right
    !==(Right) => this.Value + 19 + Right
    .(Right) => this.Value + 20 + Right
    ~=(Right) => this.Value + 21 + Right
    !~=(Right) => this.Value + 22 + Right
    +() => this.Value + 23
    -() => this.Value + 24
    ~() => this.Value + 25
    !() => this.Value + 26
}

value := Operators()
Assert(!Operators.HasOwnProp("+") && !Operators.Prototype.HasOwnProp("+"), A_LineNumber)
Assert(!Operators.HasOwnProp("KS_StaticOperatorAdd") && !Operators.Prototype.HasOwnProp("KS_OperatorAdd"), A_LineNumber)
AssertEq(value + 2, 102, A_LineNumber)
AssertEq(value - 2, 103, A_LineNumber)
AssertEq(value * 2, 104, A_LineNumber)
AssertEq(value / 2, 105, A_LineNumber)
AssertEq(value // 2, 106, A_LineNumber)
AssertEq(value ** 2, 107, A_LineNumber)
AssertEq(value & 2, 108, A_LineNumber)
AssertEq(value | 2, 109, A_LineNumber)
AssertEq(value ^ 2, 110, A_LineNumber)
AssertEq(value << 2, 111, A_LineNumber)
AssertEq(value >> 2, 112, A_LineNumber)
AssertEq(value >>> 2, 113, A_LineNumber)
AssertEq(value < 2, 114, A_LineNumber)
AssertEq(value <= 2, 115, A_LineNumber)
AssertEq(value > 2, 116, A_LineNumber)
AssertEq(value >= 2, 117, A_LineNumber)
AssertEq(value = 2, 118, A_LineNumber)
AssertEq(value == 2, 119, A_LineNumber)
AssertEq(value != 2, 120, A_LineNumber)
AssertEq(value !== 2, 121, A_LineNumber)
AssertEq(value . 2, 122, A_LineNumber)
AssertEq(value ~= 2, 123, A_LineNumber)
AssertEq(value !~= 2, 124, A_LineNumber)
AssertEq(+value, 123, A_LineNumber)
AssertEq(-value, 124, A_LineNumber)
AssertEq(~value, 125, A_LineNumber)
AssertEq(!value, 126, A_LineNumber)

class Inherited extends Operators {
    -() => 900
    *(Right) => Right * 10
}
derived := Inherited()
AssertEq(-derived, 900, A_LineNumber)
AssertEq(derived - 2, 103, A_LineNumber)
AssertEq(derived * 2, 20, A_LineNumber)
AssertEq(+derived, 123, A_LineNumber)
AssertEq(~derived, 125, A_LineNumber)
AssertEq(derived . 2, 122, A_LineNumber)

class BinaryOverride extends Inherited {
    -(Right) => 800 + Right
}
AssertEq(-BinaryOverride(), 900, A_LineNumber)
AssertEq(BinaryOverride() - 2, 802, A_LineNumber)

class UnaryOnly {
    -() => 42
}
AssertEq(-UnaryOnly(), 42, A_LineNumber)
Throws(() => UnaryOnly() - 1, A_LineNumber, TypeError)
Throws(() => 1 - Operators(), A_LineNumber, TypeError)

copy := Operators()
copy += 2
AssertEq(copy, 102, A_LineNumber)
copy := Operators()
copy -= 2
AssertEq(copy, 103, A_LineNumber)
copy := Operators()
copy *= 2
AssertEq(copy, 104, A_LineNumber)
copy := Operators()
copy /= 2
AssertEq(copy, 105, A_LineNumber)
copy := Operators()
copy //= 2
AssertEq(copy, 106, A_LineNumber)
copy := Operators()
copy **= 2
AssertEq(copy, 107, A_LineNumber)
copy := Operators()
copy &= 2
AssertEq(copy, 108, A_LineNumber)
copy := Operators()
copy |= 2
AssertEq(copy, 109, A_LineNumber)
copy := Operators()
copy ^= 2
AssertEq(copy, 110, A_LineNumber)
copy := Operators()
copy <<= 2
AssertEq(copy, 111, A_LineNumber)
copy := Operators()
copy >>= 2
AssertEq(copy, 112, A_LineNumber)
copy := Operators()
copy >>>= 2
AssertEq(copy, 113, A_LineNumber)
copy := Operators()
copy .= 2
AssertEq(copy, 122, A_LineNumber)

copy := Operators()
AssertEq(++copy, 101, A_LineNumber)
copy := Operators()
old := copy--
Assert(old is Operators, A_LineNumber)
AssertEq(copy, 102, A_LineNumber)
values := [Operators()]
values[1] -= 2
AssertEq(values[1], 103, A_LineNumber)
holder := {Value: Operators()}
holder.Value *= 2
AssertEq(holder.Value, 104, A_LineNumber)

value.DefineProp("-", {Call: (Self, Right) => 999})
Operators.Prototype.DefineProp("!", {Call: (Self) => 999})
AssertEq(-value, 124, A_LineNumber)
AssertEq(value - 2, 103, A_LineNumber)
AssertEq(!value, 126, A_LineNumber)
AssertEq(not value, 126, A_LineNumber)
value.Base := Inherited.Prototype
AssertEq(-value, 124, A_LineNumber)

class BrokenOperators {
    -() {
        throw Error("unary operator")
    }
    *(Right) {
        return unset
    }
}
Throws(() => -BrokenOperators(), A_LineNumber)
Throws(() => BrokenOperators() * 2, A_LineNumber, UnsetError)

operatorCalls := 0
OperatorTouch() {
    global operatorCalls
    operatorCalls += 1
    return 2
}
AssertEq(Operators() - OperatorTouch(), 103, A_LineNumber)
AssertEq(operatorCalls, 1, A_LineNumber)

class Accumulator {
    Value := 0
    +(Right) => this.Value += Right
}
runningTotal := Accumulator()
AssertEq(runningTotal + 1, 1, A_LineNumber)
AssertEq(runningTotal + 2, 3, A_LineNumber)

class Continued {
    Value := 1
        + (2)
    +(Right) => this.Value + Right
}
AssertEq(Continued() + 4, 7, A_LineNumber)

numbers := [-2, 0, 1, 1.5, true, false, "3", "4.5", "0x10", " -2 ", "1.0e2", "-0x10"]
for Left in numbers {
    for Right in numbers {
        expected := Left - (0 - Right)
        actual := Left + Right
        AssertEq(actual, expected, A_LineNumber)
        AssertEq(Type(actual), Type(expected), A_LineNumber)
    }
}
AssertEq(true + false, 1, A_LineNumber)
AssertEq(Type(true + false), "Integer", A_LineNumber)
AssertEq("4.5" + 1, 5.5, A_LineNumber)
AssertEq(Type("4.5" + 1), "Float", A_LineNumber)
AssertEq("0x10" + 1, 17, A_LineNumber)
AssertEq(Type("0x10" + 1), "Integer", A_LineNumber)
AssertEq("1.0e2" + 1, 101.0, A_LineNumber)
AssertEq(Type("1.0e2" + 1), "Float", A_LineNumber)
Throws(() => numbers[3] + "1e3", A_LineNumber, TypeError)
Throws(() => numbers[3] + "0x1.0", A_LineNumber, TypeError)

class Counter {
    __New(Amount := 10) {
        this.Amount := Amount
    }
    ++() => Counter(this.Amount + 10)
    --() => Counter(this.Amount - 10)
    +(Right) => -999
    -(Right) => -999
}
c := Counter()
old := c++
AssertEq(old.Amount, 10, A_LineNumber)
AssertEq(c.Amount, 20, A_LineNumber)
AssertEq((++c).Amount, 30, A_LineNumber)
old := c--
AssertEq(old.Amount, 30, A_LineNumber)
AssertEq(c.Amount, 20, A_LineNumber)
AssertEq((--c).Amount, 10, A_LineNumber)

class ChildCounter extends Counter {
    ++() => Counter(100)
}
c := ChildCounter()
AssertEq((++c).Amount, 100, A_LineNumber)
c := ChildCounter()
AssertEq((--c).Amount, 0, A_LineNumber)

class MutableCounter {
    Amount := 0
    ++() {
        this.Amount += 1
        return this
    }
}
mutable := MutableCounter()
old := mutable++
AssertEq(old, mutable, A_LineNumber)
AssertEq(old.Amount, 1, A_LineNumber)

class StorageHolder {
    Stored := Counter()
    Reads := 0
    Writes := 0
    Current {
        get {
            this.Reads += 1
            return this.Stored
        }
        set {
            this.Writes += 1
            this.Stored := value
        }
    }
    __Item[Index] {
        get {
            AssertEq(Index, 1, A_LineNumber)
            this.Reads += 1
            return this.Stored
        }
        set {
            AssertEq(Index, 1, A_LineNumber)
            this.Writes += 1
            this.Stored := value
        }
    }
}
holder := StorageHolder()
targetCalls := 0
indexCalls := 0
Target() {
    global holder, targetCalls
    targetCalls += 1
    return holder
}
Index() {
    global indexCalls
    indexCalls += 1
    return 1
}
old := Target().Current++
AssertEq(old.Amount, 10, A_LineNumber)
AssertEq(holder.Stored.Amount, 20, A_LineNumber)
AssertEq((--Target()[Index()]).Amount, 10, A_LineNumber)
AssertEq(holder.Reads, 2, A_LineNumber)
AssertEq(holder.Writes, 2, A_LineNumber)
AssertEq(targetCalls, 2, A_LineNumber)
AssertEq(indexCalls, 1, A_LineNumber)

c := Counter()
name := "c"
old := %name%++
AssertEq(old.Amount, 10, A_LineNumber)
AssertEq(c.Amount, 20, A_LineNumber)
reference := &c
AssertEq((--%reference%).Amount, 10, A_LineNumber)
Bump(&Operand) => Operand++
AssertEq(Bump(&c).Amount, 10, A_LineNumber)
AssertEq(c.Amount, 20, A_LineNumber)

class BrokenCounter {
    ++() {
        return unset
    }
    --() {
        throw ValueError("decrement")
    }
}
IncrementOperand(Operand) => ++Operand
DecrementOperand(Operand) => --Operand
Throws(() => IncrementOperand(BrokenCounter()), A_LineNumber, UnsetError)
Throws(() => DecrementOperand(BrokenCounter()), A_LineNumber, ValueError)
Throws(() => IncrementOperand({}), A_LineNumber, TypeError)

maximum := 0x7FFFFFFFFFFFFFFF
minimum := -maximum - 1
for initial in [0, -1, 1.5, true, false, "2", "2.5", maximum, minimum] {
    operand := initial
    AssertEq(operand++, initial, A_LineNumber)
    AssertEq(operand, initial + 1, A_LineNumber)
    AssertEq(Type(operand), Type(initial + 1), A_LineNumber)
    Assert(!(operand is Boolean), A_LineNumber)
    operand := initial
    AssertEq(--operand, initial - 1, A_LineNumber)
    AssertEq(Type(operand), Type(initial - 1), A_LineNumber)
    Assert(!(operand is Boolean), A_LineNumber)
}

class StartupIncrement {
    static Operand := Counter()
    static Result := ++this.Operand
}
AssertEq(StartupIncrement.Result.Amount, 20, A_LineNumber)

class Predicate {
    Value := false
    Calls := 0
    ?() {
        this.Calls += 1
        return this.Value
    }
    True() => "ordinary true method"
    False() => "ordinary false method"
}
p := Predicate()
AssertEq(p.True(), "ordinary true method", A_LineNumber)
AssertEq(p.False(), "ordinary false method", A_LineNumber)
branch := 0
if p
    branch := 1
else
    branch := 2
AssertEq(branch, 2, A_LineNumber)
AssertEq(p ? 10 : 20, 20, A_LineNumber)
AssertEq(!p, true, A_LineNumber)
AssertEq(Boolean(p), false, A_LineNumber)
AssertEq(p.Calls, 4, A_LineNumber)

p.Value := true
p.Calls := 0
iterations := 0
while p {
    iterations += 1
    p.Value := false
}
AssertEq(iterations, 1, A_LineNumber)
AssertEq(p.Calls, 2, A_LineNumber)
p.Calls := 0
loop {
    p.Value := true
} until p
AssertEq(p.Calls, 1, A_LineNumber)
p.Value := false

truthCalls := 0
TruthTouch() {
    global truthCalls
    truthCalls += 1
    return 42
}
p.Calls := 0
AssertEq(p && TruthTouch(), p, A_LineNumber)
AssertEq(truthCalls, 0, A_LineNumber)
AssertEq(p || TruthTouch(), 42, A_LineNumber)
AssertEq(truthCalls, 1, A_LineNumber)
AssertEq(p.Calls, 2, A_LineNumber)
p.Value := true
AssertEq(p && TruthTouch(), 42, A_LineNumber)
AssertEq(p || TruthTouch(), p, A_LineNumber)
AssertEq(truthCalls, 2, A_LineNumber)
AssertEq(p.Calls, 4, A_LineNumber)

class SeparateNegation extends Predicate {
    !() => "negated"
}
separate := SeparateNegation()
AssertEq(separate ? 1 : 0, 0, A_LineNumber)
AssertEq(!separate, "negated", A_LineNumber)
AssertEq({} ? 1 : 0, 1, A_LineNumber)

class DelegatingPredicate {
    Result := 0
    ?() => this.Result
}
delegating := DelegatingPredicate()
AssertEq(Boolean(delegating), false, A_LineNumber)
delegating.Result := "text"
AssertEq(!delegating, false, A_LineNumber)
inner := Predicate()
delegating.Result := inner
AssertEq(Boolean(delegating), false, A_LineNumber)
AssertEq(inner.Calls, 1, A_LineNumber)

class FiniteRecursion {
    Calls := 0
    ?() {
        this.Calls += 1
        return this.Calls < 3 ? this : 0
    }
}
recursive := FiniteRecursion()
AssertEq(Boolean(recursive), false, A_LineNumber)
AssertEq(recursive.Calls, 3, A_LineNumber)

class UnsetPredicate {
    ?() {
        return unset
    }
}
class ThrowingPredicate {
    ?() {
        throw ValueError("truth test")
    }
}
delegating.Result := UnsetPredicate()
Throws(() => Boolean(delegating), A_LineNumber, UnsetError)
delegating.Result := ThrowingPredicate()
Throws(() => Boolean(delegating), A_LineNumber, ValueError)

FileAppend "pass", "*"
