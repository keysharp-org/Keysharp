#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#Import Ks { Crypt, Base32, Hex }
#Include <assert>

Totp(Secret, UnixTime, Algorithm := "SHA1") {
    counter := Buffer(8), steps := UnixTime // 30
    Loop 8
        NumPut("UChar", (steps >> ((8 - A_Index) * 8)) & 255, counter, A_Index - 1)
    digest := Hex.Decode(Crypt.Hmac(counter, Base32.Decode(Secret), Algorithm))
    offset := NumGet(digest, digest.Size - 1, "UChar") & 15, code := 0
    Loop 4
        code := (code << 8) | NumGet(digest, offset + A_Index - 1, "UChar")
    return Format("{1:08d}", Mod(code & 0x7fffffff, 100000000))
}

; RFC 6238 appendices A and B use 20, 32 and 64 byte secrets for SHA1, SHA256 and SHA512.
vectorSecret := "12345678901234567890"
secrets := [Base32.Encode(vectorSecret), Base32.Encode(vectorSecret "123456789012"), Base32.Encode(vectorSecret vectorSecret vectorSecret "1234")]
algorithms := ["SHA1", "SHA256", "SHA512"]
vectors := [[59, "94287082", "46119246", "90693936"],
    [1111111109, "07081804", "68084774", "25091201"],
    [1111111111, "14050471", "67062674", "99943326"],
    [1234567890, "89005924", "91819424", "93441116"],
    [2000000000, "69279037", "90698825", "38618901"],
    [20000000000, "65353130", "77737706", "47863826"]]
for vector in vectors
    for index, vectorAlgorithm in algorithms
        AssertEq(Totp(secrets[index], vector[1], vectorAlgorithm), vector[index + 1], A_LineNumber)
AssertEq(Totp("gezdgnbvgy3tqojqgezdgnbvgy3tqojq", 59), "94287082", A_LineNumber)

FileAppend "pass", "*"
