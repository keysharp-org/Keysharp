#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#Import Ks { Base32, Base64, StringBuffer }
#Include <assert>

; RFC 4648 section 10 covers every possible final block length.
vectors := [["", ""], ["f", "MY======"], ["fo", "MZXQ===="], ["foo", "MZXW6==="],
    ["foob", "MZXW6YQ="], ["fooba", "MZXW6YTB"], ["foobar", "MZXW6YTBOI======"]]
for vector in vectors {
    AssertEq(Base32.Encode(vector[1]), vector[2], A_LineNumber)
    decoded := Base32.Decode(vector[2])
    Assert(decoded is Buffer, A_LineNumber)
    AssertEq(decoded.Size, StrLen(vector[1]), A_LineNumber)
    AssertEq(Base32.Encode(decoded), vector[2], A_LineNumber)
    if decoded.Size
        AssertEq(StrGet(decoded, decoded.Size, "UTF-8"), vector[1], A_LineNumber)
    AssertEq(Base32.Encode(Base32.Decode(StrLower(vector[2]))), vector[2], A_LineNumber)
    AssertEq(Base32.Encode(Base32.Decode(RTrim(StrLower(vector[2]), "="))), vector[2], A_LineNumber)
}

AssertEq(Base32.Encode(StringBuffer("foo")), "MZXW6===", A_LineNumber)
AssertEq(Base32.Encode(StringBuffer("abc"), "UTF-16"), Base32.Encode("abc", "UTF-16"), A_LineNumber)
AssertEq(Base32.Encode(Buffer(0)), "", A_LineNumber)
AssertEq(Base64.Encode(Buffer(0)), "", A_LineNumber)
Throws(() => Base32.Encode([]), A_LineNumber, TypeError)

bytes := Buffer(256)
Loop 256
    NumPut("UChar", A_Index - 1, bytes, A_Index - 1)
decoded := Base32.Decode(Base32.Encode(bytes))
AssertEq(decoded.Size, bytes.Size, A_LineNumber)
Loop 256
    AssertEq(NumGet(decoded, A_Index - 1, "UChar"), A_Index - 1, A_LineNumber)

for malformed in ["A", "AAA", "AAAAAA", "MY=", "MY=====", "MY=======", "========",
    "M=Y=====", "MZXW6YTB=", "M0======", "M1======", "M8======", "M/======", "M+======",
    "MY ======", "MY======`n", "MZ======", "MZ", "MZXR", "MZXW7", "MZXW6YR"]
    Throws(() => Base32.Decode(malformed), A_LineNumber, ValueError)
Throws(() => Base32.Encode("foo", "no-such-encoding"), A_LineNumber, ValueError)
Throws(() => Base32.Encode(bytes, "no-such-encoding"), A_LineNumber, ValueError)
Throws(() => Base32.Encode({}), A_LineNumber, TypeError)

FileAppend "pass", "*"
