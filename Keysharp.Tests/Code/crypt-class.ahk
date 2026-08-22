#NoTrayIcon
#import KS { Crypt, Base64Encode, A_DirSeparator }
#Include <assert>

; The Crypt class through real dynamic dispatch. A String is taken as its UTF-8 bytes, so every digest here
; is the one an external tool prints for the same text; the published vectors for "abc" are used as such.

CompareBuffers(a, b) {
    if (a.Size != b.Size)
        return 0
    loop a.Size
        if (NumGet(a, A_Index - 1, "UChar") != NumGet(b, A_Index - 1, "UChar"))
            return 0
    return 1
}

AssertEq(Crypt.MD5("abc"), "900150983CD24FB0D6963F7D28E17F72", A_LineNumber)
AssertEq(Crypt.SHA1("abc"), "A9993E364706816ABA3E25717850C26C9CD0D89D", A_LineNumber)
AssertEq(Crypt.SHA256("abc"), "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD", A_LineNumber)
AssertEq(Crypt.SHA384("abc"), "CB00753F45A35E8BB5A03D699AC65007272C32AB0EDED1631A8B605A43FF5BED8086072BA1E7CC2358BAECA134C825A7", A_LineNumber)
AssertEq(Crypt.SHA512("abc"), "DDAF35A193617ABACC417349AE20413112E6FA4E89A97EA20A9EEEE64B55D39A2192992A274FC1A836BA3C23A3FEEBBD454D4423643CE80E2A9AC94FA54CA49F", A_LineNumber)
AssertEq(Crypt.SHA256(""), "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855", A_LineNumber)

; Hash defaults to SHA256, and takes the algorithm name in any case and with the "-" external tools print.
AssertEq(Crypt.Hash("abc"), Crypt.SHA256("abc"), A_LineNumber)
AssertEq(Crypt.Hash("abc", "sha-256"), Crypt.SHA256("abc"), A_LineNumber)

; The encoding a string is taken in is selectable, and this is the digest the old UTF-16 behavior produced.
AssertEq(Crypt.SHA256("abc", "UTF-16"), "13E228567E8249FCE53337F25D7970DE3BD68AB2653424C7B8F9FD05E33CAEDF", A_LineNumber)

; CRC32 as the integer it is, and as the hexadecimal Hash returns for it.
AssertEq(Crypt.CRC32("abc"), 891568578, A_LineNumber)
AssertEq(Crypt.Hash("abc", "CRC32"), "352441C2", A_LineNumber)

; Bytes already in memory are hashed as they stand, with no encoding involved.
buf := Buffer(3)
NumPut("UChar", 0x61, "UChar", 0x62, "UChar", 0x63, buf)
AssertEq(Crypt.SHA256(buf), Crypt.SHA256("abc"), A_LineNumber)

; HashFile reads the file as a stream and agrees with hashing the same bytes in memory.
path := A_Temp A_DirSeparator "keysharp-crypt-" A_TickCount ".bin"
FileAppend "abc", path, "UTF-8-RAW"
AssertEq(Crypt.HashFile(path), Crypt.SHA256("abc"), A_LineNumber)
AssertEq(Crypt.HashFile(path, "MD5"), Crypt.MD5("abc"), A_LineNumber)

; An open File hashes its whole content and is left at the position it was on.
f := FileOpen(path, "r")
f.Pos := 1
AssertEq(Crypt.Hash(f), Crypt.SHA256("abc"), A_LineNumber)
AssertEq(f.Pos, 1, A_LineNumber)
f.Close()

; Hashing a File that has been closed reports it, rather than throwing an uncatchable disposed-object error.
Throws(() => Crypt.Hash(f), A_LineNumber, ValueError)

; Every digest entry point accepts a File, including the checksum one.
of := FileOpen(path, "r")
AssertEq(Crypt.CRC32(of), Crypt.CRC32("abc"), A_LineNumber)
AssertEq(Crypt.SHA256(of), Crypt.SHA256("abc"), A_LineNumber)
of.Close()
FileDelete path

; A file larger than one read block must stream to the same digest it produces in memory, which is what
; exercises the incremental path of each algorithm rather than a single ComputeHash call.
big := A_Temp A_DirSeparator "keysharp-crypt-big-" A_TickCount ".bin"
fh := FileOpen(big, "w", "UTF-8-RAW")

loop 2000
    fh.Write("The quick brown fox jumps over the lazy dog 0123456789")

fh.Close()
raw := FileRead(big, "RAW")
AssertEq(raw.Size, 108000, A_LineNumber)
AssertEq(Crypt.HashFile(big), Crypt.SHA256(raw), A_LineNumber)
AssertEq(Crypt.HashFile(big, "MD5"), Crypt.MD5(raw), A_LineNumber)
AssertEq(Crypt.HashFile(big, "CRC32"), Crypt.Hash(raw, "CRC32"), A_LineNumber)
FileDelete big

; Errors from the hashing surface, each of which must be catchable.
Throws(() => Crypt.Hash("abc", "SHA3"), A_LineNumber, ValueError)
Throws(() => Crypt.HashFile(A_Temp A_DirSeparator "keysharp-crypt-no-such-file.bin"), A_LineNumber, OSError)
Throws(() => Crypt.HashFile(""), A_LineNumber, ValueError)

; An encoding name which cannot be resolved is an error, never a silent substitution, because the digest
; would otherwise be computed over the wrong bytes.
Throws(() => Crypt.SHA256("abc", "no-such-encoding"), A_LineNumber, ValueError)

; Encrypt/Decrypt round-trip, with the key and the text taken in the same encoding as everything else.
secret := Crypt.Encrypt("hello", "key")
plain := Crypt.Decrypt(secret, "key")
AssertEq(StrGet(plain, plain.Size, "UTF-8"), "hello", A_LineNumber)

; Each call draws its own initialization vector, so the same text under one key does not encrypt alike,
; and the vector rides in front of the ciphertext: 16 bytes of it, ahead of one 16-byte AES block.
again := Crypt.Encrypt("hello", "key")
Assert(!CompareBuffers(secret, again), A_LineNumber)
AssertEq(secret.Size, 32, A_LineNumber)
AssertEq(Crypt.Decrypt(again, "key").Size, 5, A_LineNumber)

; With a vector supplied, the ciphertext itself is pinned rather than only the round trip, so a silent
; change to the key padding or to the vector handling has to fail here. It is not prepended in this form.
AssertEq(Base64Encode(Crypt.Encrypt("hello", "key", , , "0123456789abcdef")), "g1TwShPvFGwTdenQrf+wew==", A_LineNumber)
AssertEq(Base64Encode(Crypt.Encrypt("hello", "key", "AES", "ECB")), "V/rlD0HpNugob35tzHnxCw==", A_LineNumber)

; A supplied vector round-trips, and decrypting needs the same one back.
fixed := Crypt.Encrypt("hello", "key", , , "0123456789abcdef")
fixedPlain := Crypt.Decrypt(fixed, "key", , , "0123456789abcdef")
AssertEq(StrGet(fixedPlain, fixedPlain.Size, "UTF-8"), "hello", A_LineNumber)
AssertEq(fixed.Size, 16, A_LineNumber)
Throws(() => Crypt.Decrypt(fixed, "key", , , "fedcba9876543210"), A_LineNumber, ValueError)
Throws(() => Crypt.Encrypt("hello", "key", , , "too short"), A_LineNumber, ValueError)
Throws(() => Crypt.Encrypt("hello", "key", "AES", "ECB", "0123456789abcdef"), A_LineNumber, ValueError)

; The cipher and its chaining mode are named, not baked into the method, and an unknown one raises.
ecb := Crypt.Encrypt("hello", "key", "AES", "ECB")
plainEcb := Crypt.Decrypt(ecb, "key", "AES", "ECB")
AssertEq(StrGet(plainEcb, plainEcb.Size, "UTF-8"), "hello", A_LineNumber)
Assert(!CompareBuffers(ecb, secret), A_LineNumber)
Throws(() => Crypt.Encrypt("hello", "key", "Twofish"), A_LineNumber, ValueError)
Throws(() => Crypt.Encrypt("hello", "key", "AES", "XTS"), A_LineNumber, ValueError)

; Decryption reports failure as a catchable error rather than letting a raw .NET exception escape. The
; vector is fixed so that the wrong key lands on the same invalid padding every run.
Throws(() => Crypt.Decrypt(fixed, "wrongkey", , , "0123456789abcdef"), A_LineNumber, ValueError)
Throws(() => Crypt.Decrypt("hello", "key"), A_LineNumber, ValueError)

; RandomBytes is what makes a vector available to a script at all.
iv1 := Crypt.RandomBytes(16)
iv2 := Crypt.RandomBytes(16)
AssertEq(iv1.Size, 16, A_LineNumber)
Assert(!CompareBuffers(iv1, iv2), A_LineNumber)
AssertEq(Crypt.RandomBytes(0).Size, 0, A_LineNumber)

; GCM authenticates as well as encrypts: the nonce rides in front and the tag at the end, so the result is
; 12 + 5 + 16 bytes for five bytes of text.
sealed := Crypt.Encrypt("hello", "key", , "GCM")
opened := Crypt.Decrypt(sealed, "key", , "GCM")
AssertEq(sealed.Size, 33, A_LineNumber)
AssertEq(StrGet(opened, opened.Size, "UTF-8"), "hello", A_LineNumber)

; Altering the ciphertext, the tag or the nonce is detected, where a chained mode would decrypt any of them
; to rubbish without complaint. Offsets: nonce 0-11, ciphertext 12-16, tag 17-32.
for offset in [14, 20, 3] {
    torn := Crypt.Encrypt("hello", "key", , "GCM")
    NumPut("UChar", NumGet(torn, offset, "UChar") ^ 1, torn, offset)
    Throws(() => Crypt.Decrypt(torn, "key", , "GCM"), A_LineNumber, ValueError)
}

; A nonce is 12 bytes, not the 16 a chaining mode's vector takes.
Throws(() => Crypt.Encrypt("hello", "key", , "GCM", "0123456789abcdef"), A_LineNumber, ValueError)

; PBKDF2 against RFC 6070's second test vector, which pins the iteration count and the output length.
AssertEq(Base64Encode(Crypt.PBKDF2("password", "salt", 2, 20, "SHA1")), "6mwBTcctb4zNHtkqzh1B8NjeiVc=", A_LineNumber)

; A derived key is what makes a passphrase usable as one, and it is what Encrypt should be handed.
salt := Crypt.RandomBytes(16)
derived := Crypt.PBKDF2("hunter2", salt, 1000)
sealed2 := Crypt.Encrypt("secret", derived, , "GCM")
opened2 := Crypt.Decrypt(sealed2, derived, , "GCM")
AssertEq(derived.Size, 32, A_LineNumber)
AssertEq(StrGet(opened2, opened2.Size, "UTF-8"), "secret", A_LineNumber)
Throws(() => Crypt.PBKDF2("password", "salt", 2, 20, "MD5"), A_LineNumber, ValueError)
Throws(() => Crypt.PBKDF2("password", "salt", 0), A_LineNumber, ValueError)

; A name left blank falls back to the default, since an omitted argument reaches a function as "".
blank := ""
AssertEq(Crypt.Hash("abc", blank), Crypt.SHA256("abc"), A_LineNumber)
AssertEq(Crypt.Hash("abc", blank, blank), Crypt.SHA256("abc"), A_LineNumber)

; The upper bound of a SecureRandom range is included in it, as Random's is.
hits := 0

loop 300
    if (Crypt.SecureRandom(1, 3) == 3)
        hits := 1

Assert(hits, A_LineNumber)
AssertEq(Crypt.SecureRandom(7, 7), 7, A_LineNumber)

FileAppend "pass", "*"
