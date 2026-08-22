#NoTrayIcon
#Include <assert>

;
; Integers
;

x := 10 ; Positive int minus positive int.
y := x - 100

Assert(y = -90, A_LineNumber)
	
Assert(Type(y) = "Integer", A_LineNumber)

x := 10 ; Positive int minus negative int.
y := x - -100

Assert(y = 110, A_LineNumber)
	
Assert(Type(y) = "Integer", A_LineNumber)

x := -10 ; Negative int minus positive int.
y := x - 100

Assert(y = -110, A_LineNumber)
	
Assert(Type(y) = "Integer", A_LineNumber)

x := -10 ; Negative int minus negative int.
y := x - -100

Assert(y = 90, A_LineNumber)
	
Assert(Type(y) = "Integer", A_LineNumber)

x := 10 ; Negated positive int minus positive int.
y := -x - 100

Assert(y = -110, A_LineNumber)
	
Assert(Type(y) = "Integer", A_LineNumber)

x := 10 ; Positive int minus negated positive int.
y := 100 - -x

Assert(y = 110, A_LineNumber)
	
Assert(Type(y) = "Integer", A_LineNumber)

x := 0 ; Zero int minus positive non-zero int.
y := x - 100

Assert(y = -100, A_LineNumber)
	
Assert(Type(y) = "Integer", A_LineNumber)

x := 100 ; Non-zero positive int minus zero int.
y := x - 0

Assert(y = 100, A_LineNumber)
	
Assert(Type(y) = "Integer", A_LineNumber)

x := 0 ; Zero int minus negative non-zero int.
y := x - -100

Assert(y = 100, A_LineNumber)
	
Assert(Type(y) = "Integer", A_LineNumber)

x := -100 ; Non-zero negative int minus zero int.
y := x - 0

Assert(y = -100, A_LineNumber)
	
Assert(Type(y) = "Integer", A_LineNumber)

x := 0 ; Zero int minus zero int.
y := x - 0

Assert(y = 0, A_LineNumber)
	
Assert(Type(y) = "Integer", A_LineNumber)

;
; Double
;

x := 10 ; Positive int minus positive double.
y := x - 100.0

Assert(y = -90.0, A_LineNumber)
	
Assert(Type(y) = "Float", A_LineNumber)

x := 10 ; Positive int minus negative double.
y := x - -100.0

Assert(y = 110.0, A_LineNumber)
	
Assert(Type(y) = "Float", A_LineNumber)

x := -10.0 ; Negative double minus positive int.
y := x - 100

Assert(y = -110, A_LineNumber)
	
Assert(Type(y) = "Float", A_LineNumber)

x := -10.0 ; Negative double minus negative int.
y := x - -100

Assert(y = 90.0, A_LineNumber)
	
Assert(Type(y) = "Float", A_LineNumber)

x := 10 ; Negated positive int minus positive double.
y := -x - 100.0

Assert(y = -110.0, A_LineNumber)
	
Assert(Type(y) = "Float", A_LineNumber)

x := 10.0 ; Positive int minus negated positive double.
y := 100 - -x

Assert(y = 110, A_LineNumber)
	
Assert(Type(y) = "Float", A_LineNumber)

x := 0 ; Zero int minus positive non-zero double.
y := x - 100.0

Assert(y = -100.0, A_LineNumber)
	
Assert(Type(y) = "Float", A_LineNumber)

x := 100.0 ; Non-zero positive double minus zero int.
y := x - 0

Assert(y = 100.0, A_LineNumber)
	
Assert(Type(y) = "Float", A_LineNumber)

x := 0 ; Zero int minus negative non-zero double.
y := x - -100.0

Assert(y = 100.0, A_LineNumber)
	
Assert(Type(y) = "Float", A_LineNumber)

x := -100.0 ; Non-zero negative double minus zero int.
y := x - 0

Assert(y = -100.0, A_LineNumber)
	
Assert(Type(y) = "Float", A_LineNumber)

x := 0.0 ; Zero double minus zero double.
y := x - 0.0

Assert(y = 0.0, A_LineNumber)
	
Assert(Type(y) = "Float", A_LineNumber)

;
; Integer strings
;

x := "10" ; Positive int string minus positive int.
y := x - 100

Assert(y = -90, A_LineNumber)
	
Assert(Type(y) = "Integer", A_LineNumber)

x := 10 ; Positive int minus negative int string.
y := x - "-100"

Assert(y = 110, A_LineNumber)
	
Assert(Type(y) = "Integer", A_LineNumber)

x := "-10" ; Negative int string minus positive int.
y := x - 100

Assert(y = -110, A_LineNumber)
	
Assert(Type(y) = "Integer", A_LineNumber)

x := "-10" ; Negative int string minus negative int string.
y := x - "-100"

Assert(y = 90, A_LineNumber)
	
Assert(Type(y) = "Integer", A_LineNumber)

x := "10" ; Negated positive int string minus positive int.
y := -x - 100

Assert(y = -110, A_LineNumber)
	
Assert(Type(y) = "Integer", A_LineNumber)

x := "10" ; Positive int minus negated positive int string.
y := 100 - -x

Assert(y = 110, A_LineNumber)
	
Assert(Type(y) = "Integer", A_LineNumber)

x := "0" ; Zero int string minus positive non-zero int.
y := x - 100

Assert(y = -100, A_LineNumber)
	
Assert(Type(y) = "Integer", A_LineNumber)

x := "100" ; Non-zero positive int string minus zero int.
y := x - 0

Assert(y = 100, A_LineNumber)
	
Assert(Type(y) = "Integer", A_LineNumber)

x := "0" ; Zero int string minus negative non-zero int.
y := x - -100

Assert(y = 100, A_LineNumber)
	
Assert(Type(y) = "Integer", A_LineNumber)

x := "-100" ; Non-zero negative int string minus zero int.
y := x - 0

Assert(y = -100, A_LineNumber)
	
Assert(Type(y) = "Integer", A_LineNumber)

x := "0" ; Zero int string minus zero int string.
y := x - "0"

Assert(y = 0, A_LineNumber)
	
Assert(Type(y) = "Integer", A_LineNumber)

;
; Double strings
;

x := "10.0" ; Positive double string minus positive int.
y := x - 100

Assert(y = -90.0, A_LineNumber)
	
Assert(Type(y) = "Float", A_LineNumber)

x := 10 ; Positive int minus negative double string.
y := x - "-100.0"

Assert(y = 110.0, A_LineNumber)
	
Assert(Type(y) = "Float", A_LineNumber)

x := "-10.0" ; Negative double string minus positive int.
y := x - 100

Assert(y = -110.0, A_LineNumber)
	
Assert(Type(y) = "Float", A_LineNumber)

x := "-10.0" ; Negative double string minus negative double string.
y := x - "-100.0"

Assert(y = 90.0, A_LineNumber)
	
Assert(Type(y) = "Float", A_LineNumber)

x := "10.0" ; Negated positive double string minus positive int.
y := -x - 100

Assert(y = -110.0, A_LineNumber)
	
Assert(Type(y) = "Float", A_LineNumber)

x := "10.0" ; Positive int minus negated positive double string.
y := 100 - -x

Assert(y = 110.0, A_LineNumber)
	
Assert(Type(y) = "Float", A_LineNumber)

x := "0.0" ; Zero double string minus positive non-zero int.
y := x - 100

Assert(y = -100.0, A_LineNumber)
	
Assert(Type(y) = "Float", A_LineNumber)

x := "100.0" ; Non-zero positive double string minus zero int.
y := x - 0

Assert(y = 100, A_LineNumber)
	
Assert(Type(y) = "Float", A_LineNumber)

x := "0.0" ; Zero double string minus negative non-zero int.
y := x - -100

Assert(y = 100, A_LineNumber)
	
Assert(Type(y) = "Float", A_LineNumber)

x := "-100.0" ; Non-zero negative double string minus zero int.
y := x - 0

Assert(y = -100.0, A_LineNumber)
	
Assert(Type(y) = "Float", A_LineNumber)

x := "0.0" ; Zero double string minus zero double string.
y := x - "0.0"

Assert(y = 0.0, A_LineNumber)
	
Assert(Type(y) = "Float", A_LineNumber)

;
; Hex strings minus int and double
;

x := "0x0A" ; Positive hex string minus positive int.
y := x - 100

Assert(y = -90, A_LineNumber)
	
Assert(Type(y) = "Integer", A_LineNumber)

x := "0x0A" ; Positive hex string minus positive double.
y := x - 100.0

Assert(y = -90.0, A_LineNumber)
	
Assert(Type(y) = "Float", A_LineNumber)

x := 10 ; Positive int minus negative hex string.
y := x - "-0x64"

Assert(y = 110, A_LineNumber)
	
Assert(Type(y) = "Integer", A_LineNumber)

x := 10.0 ; Positive double minus negative hex string.
y := x - "-0x64"

Assert(y = 110, A_LineNumber)
	
Assert(Type(y) = "Float", A_LineNumber)

x := "-0x0A" ; Negative hex string minus positive int.
y := x - 100

Assert(y = -110, A_LineNumber)
	
Assert(Type(y) = "Integer", A_LineNumber)

x := "-0x0A" ; Negative hex string minus positive double.
y := x - 100.0

Assert(y = -110.0, A_LineNumber)
	
Assert(Type(y) = "Float", A_LineNumber)

x := "-0x0A" ; Negative hex string minus negative int string.
y := x - "-100"

Assert(y = 90, A_LineNumber)
	
Assert(Type(y) = "Integer", A_LineNumber)
	
x := "-0x0A" ; Negative hex string minus negative double string.
y := x - "-100.0"

Assert(y = 90.0, A_LineNumber)
	
Assert(Type(y) = "Float", A_LineNumber)

x := "0x0A" ; Negated positive hex string minus positive int.
y := -x - 100

Assert(y = -110, A_LineNumber)
	
Assert(Type(y) = "Integer", A_LineNumber)
	
x := "0x0A" ; Negated positive hex string minus positive double.
y := -x - 100.0

Assert(y = -110, A_LineNumber)
	
Assert(Type(y) = "Float", A_LineNumber)

x := "0x0A" ; Positive int minus negated positive hex string.
y := 100 - -x

Assert(y = 110.0, A_LineNumber)
	
Assert(Type(y) = "Integer", A_LineNumber)
	
x := "0x0A" ; Positive double minus negated positive hex string.
y := 100.0 - -x

Assert(y = 110.0, A_LineNumber)
	
Assert(Type(y) = "Float", A_LineNumber)

x := "0x00" ; Zero hex string minus positive non-zero int.
y := x - 100

Assert(y = -100, A_LineNumber)
	
Assert(Type(y) = "Integer", A_LineNumber)
	
x := "0x00" ; Zero hex string minus positive non-zero double.
y := x - 100.0

Assert(y = -100.0, A_LineNumber)
	
Assert(Type(y) = "Float", A_LineNumber)

x := "0x0A" ; Non-zero positive hex string minus zero int.
y := x - 0

Assert(y = 10, A_LineNumber)
	
Assert(Type(y) = "Integer", A_LineNumber)

x := "0x0A" ; Non-zero positive hex string minus zero double.
y := x - 0.0

Assert(y = 10.0, A_LineNumber)
	
Assert(Type(y) = "Float", A_LineNumber)

x := "0x00" ; Zero hex string minus negative non-zero int.
y := x - -100

Assert(y = 100, A_LineNumber)
	
Assert(Type(y) = "Integer", A_LineNumber)
	
x := "0x00" ; Zero hex string minus negative non-zero double.
y := x - -100.0

Assert(y = 100.0, A_LineNumber)
	
Assert(Type(y) = "Float", A_LineNumber)

x := "-0x64" ; Non-zero negative hex string minus zero int.
y := x - 0

Assert(y = -100, A_LineNumber)
	
Assert(Type(y) = "Integer", A_LineNumber)

x := "-0x64" ; Non-zero negative hex string minus zero double.
y := x - 0.0

Assert(y = -100.0, A_LineNumber)
	
Assert(Type(y) = "Float", A_LineNumber)

;
; Hex strings
;

x := "0x0A" ; Positive hex string minus positive hex string.
y := x - "0x64"

Assert(y = -90, A_LineNumber)
	
Assert(Type(y) = "Integer", A_LineNumber)

x := "0x0A" ; Positive hex string minus negative hex string.
y := x - "-0x64"

Assert(y = 110, A_LineNumber)
	
Assert(Type(y) = "Integer", A_LineNumber)

x := "-0x0A" ; Negative hex string minus positive hex string.
y := x - "0x64"

Assert(y = -110, A_LineNumber)
	
Assert(Type(y) = "Integer", A_LineNumber)

x := "-0x0A" ; Negative hex string minus negative hex string.
y := x - "-0x64"

Assert(y = 90, A_LineNumber)
	
Assert(Type(y) = "Integer", A_LineNumber)
	
x := "0x0A" ; Negated positive hex string minus positive hex string.
y := -x - "0x64"

Assert(y = -110, A_LineNumber)
	
Assert(Type(y) = "Integer", A_LineNumber)

x := "0x0A" ; Positive hex string minus negated positive hex string.
y := "0x64" - -x

Assert(y = 110, A_LineNumber)
	
Assert(Type(y) = "Integer", A_LineNumber)

x := "0x00" ; Zero hex string minus positive non-zero hex string.
y := x - "0x64"

Assert(y = -100, A_LineNumber)
	
Assert(Type(y) = "Integer", A_LineNumber)

x := "0x0A" ; Non-zero positive hex string minus zero hex string.
y := x - "0x00"

Assert(y = 10, A_LineNumber)
	
Assert(Type(y) = "Integer", A_LineNumber)

x := "0x00" ; Zero hex string minus negative non-zero hex string.
y := x - "-0x64"

Assert(y = 100, A_LineNumber)
	
Assert(Type(y) = "Integer", A_LineNumber)

x := "-0x64" ; Non-zero negative hex string minus zero hex string.
y := x - "0x00"

Assert(y = -100, A_LineNumber)
	
Assert(Type(y) = "Integer", A_LineNumber)

x := "0x00" ; Zero hex string minus zero hex string.
y := x - "0x00"

Assert(y = 0, A_LineNumber)
	
Assert(Type(y) = "Integer", A_LineNumber)

FileAppend "pass", "*"
