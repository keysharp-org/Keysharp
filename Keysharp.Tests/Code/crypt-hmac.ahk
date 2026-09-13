#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#Import Ks { Crypt, Hex, StringBuffer, A_DirSeparator }
#Include <assert>

; RFC 2202 section 3 and RFC 4231 sections 4.2, 4.3 and 4.7.
key := Buffer(20, 0x0b)
vectors := [
    ["SHA1", "B617318655057264E28BC0B6FB378C8EF146BE00"],
    ["SHA256", "B0344C61D8DB38535CA8AFCEAF0BF12B881DC200C9833DA726E9376C2E32CFF7"],
    ["SHA384", "AFD03944D84895626B0825F4AB46907F15F9DADBE4101EC682AA034C7CEBC59CFAEA9EA9076EDE7F4AF152E8B2FA9CB6"],
    ["SHA512", "87AA7CDEA5EF619D4FF0B4241A1D6CB02379F4E2CE4EC2787AD0B30545E17CDEDAA833B7D6B8A702038B274EAEA3F4E4BE9D914EEB61F1702E696C203A126854"]]
for vector in vectors {
    AssertEq(Crypt.Hmac("Hi There", key, vector[1]), vector[2], A_LineNumber)
    raw := Hex.Decode(Crypt.Hmac("Hi There", key, vector[1]))
    Assert(raw is Buffer, A_LineNumber)
    AssertEq(raw.Size, StrLen(vector[2]) // 2, A_LineNumber)
    AssertEq(Hex.Encode(raw), vector[2], A_LineNumber)
}

message := "what do ya want for nothing?"
AssertEq(Crypt.Hmac(message, "Jefe", "SHA1"), "EFFCDF6AE5EB2FA2D27416D5F184DF9C259A7C79", A_LineNumber)
AssertEq(Crypt.Hmac(message, "Jefe"), "5BDCC146BF60754E6A042426089575C75A003F089D2739839DEC58B964EC3843", A_LineNumber)
AssertEq(Crypt.Hmac(message, "Jefe", "SHA512"), "164B7A7BFCF819E2E395FBE73B56E0A387BD64222E831FD610270CD7EA2505549758BF75C05A994A6D034F65F8F0E6FDCAEAB1A34D4A6B4B636E070A38BCE737", A_LineNumber)
longMessage := "Test Using Larger Than Block-Size Key - Hash Key First"
AssertEq(Crypt.Hmac(longMessage, Buffer(80, 0xaa), "SHA1"), "AA4AE5E15272D00E95705637CE8A3B55ED402112", A_LineNumber)
AssertEq(Crypt.Hmac(longMessage, Buffer(131, 0xaa)), "60E431591EE0B67F0D8A26AACBF5B77F8E0BC6213728C5140546040F0EE37F54", A_LineNumber)
AssertEq(Crypt.Hmac(longMessage, Buffer(131, 0xaa), "SHA512"), "80B24263C7C1A3EBB71493C1DD7BE8B49B46D1F41B4AEEC1121B013783F8F3526B56D037E05F2598BD0FD2215D6A1E5295E64F73F63F0AEC8B915A985D786598", A_LineNumber)

expected := Crypt.Hmac("Hi There", key)
AssertEq(Type(expected), "String", A_LineNumber)
AssertEq(Crypt.Hmac("Hi There", key, "sha-256"), expected, A_LineNumber)
AssertEq(Crypt.Hmac("Hi There", key, "", ""), expected, A_LineNumber)
AssertEq(Crypt.Hmac(StringBuffer("Hi There"), key), expected, A_LineNumber)
AssertEq(Crypt.Hmac(message, StringBuffer("Jefe")), Crypt.Hmac(message, "Jefe"), A_LineNumber)
AssertEq(Crypt.Hmac("", ""), Crypt.Hmac(Buffer(0), Buffer(0)), A_LineNumber)

path := A_Temp A_DirSeparator "keysharp-hmac-" ProcessExist() "-" A_TickCount ".bin"
inputFile := FileOpen(path, "w", "UTF-8-RAW")
inputFile.Write("Hi There")
inputFile.Close()
try {
    inputFile := FileOpen(path, "r")
    try {
        inputFile.Pos := 2
        AssertEq(Crypt.Hmac(inputFile, key), expected, A_LineNumber)
        AssertEq(inputFile.Pos, 2, A_LineNumber)
    } finally {
        inputFile.Close()
    }
    Throws(() => Crypt.Hmac(inputFile, key), A_LineNumber, ValueError)
} finally {
    FileDelete path
}

for algorithm in ["MD5", "CRC32", "SHA3", "no-such-algorithm"]
    Throws(() => Crypt.Hmac("abc", "key", algorithm), A_LineNumber, ValueError)
Throws(() => Crypt.Hmac("abc", "key", , "no-such-encoding"), A_LineNumber, ValueError)
Throws(() => Crypt.Hmac({}, "key"), A_LineNumber, TypeError)
Throws(() => Crypt.Hmac("abc", {}), A_LineNumber, TypeError)

FileAppend "pass", "*"
