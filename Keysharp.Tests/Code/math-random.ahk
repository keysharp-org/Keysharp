#NoTrayIcon

#import KS { RandomSeed }
#Include <assert>
Assert(Random() >= 0, A_LineNumber)

x := Random(-1, 1)
 
Assert(x >= -1 && x <= 1, A_LineNumber)

RandomSeed(1234.1234)

Assert(Random() >= 0, A_LineNumber)

x := Random(-1.234, 1.234)
 
Assert(x >= -1.234 && x <= 1.234, A_LineNumber)

FileAppend "pass", "*"
