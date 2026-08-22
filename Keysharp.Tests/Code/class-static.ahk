#NoTrayIcon
#Include <assert>

class myclass
{
	static a := unset
	static b := ""
	static c := "asdf"
	static x := 123
	static y := this.x
	static arr := [1, 2, 3]
	static m := {one : 1, two : 2, three : 3}
}

classobj := myclass.Call()

Assert(!IsSet(myclass.a), A_LineNumber)

AssertEq(myclass.b, "", A_LineNumber)

AssertEq(myclass.c, "asdf", A_LineNumber)

AssertEq(myclass.x, 123, A_LineNumber)

AssertEq(myclass.y, myclass.x, A_LineNumber)
	
myclass.x := 456

AssertEq(myclass.x, 456, A_LineNumber)

AssertEq(myclass.y, 123, A_LineNumber)
	
classobj2 := myclass.Call()

AssertEq(myclass.x, 456, A_LineNumber)
	
classobj3 := myclass()

Assert(!classobj3.HasProp("x"), A_LineNumber)

a := 1

Assert(!IsSet(myclass.a), A_LineNumber)

myclass.a := 123

AssertEq(a, 1, A_LineNumber)
	
Assert(myclass.arr is Array && myclass.arr.Length == 3, A_LineNumber)
	
Assert(myclass.m is Object && myclass.m.one == 1 && myclass.m.two == 2 && myclass.m.three == 3, A_LineNumber)

; test static member initialized in a complex way.
class TypeSizeMapper {
	static NumTypeSize := this.MapInit()
	
	static MapInit()
	{
		temp := Map()
		for t in [
			[1,  'Int8' ,  'char' ],
			[1, 'UInt8' , 'uchar' ],
			[2,  'Int16',  'short'],
			[2, 'UInt16', 'ushort'],
			[4,  'Int32',  'int'  ],
			[4, 'UInt32', 'uint'  ],
			[8,  'Int64',  'int64'],
			[8, 'UInt64', 'uint64'],
			[4, 'Single', 'float' ],
			[8, 'Double', 'double'],
			[A_PtrSize, 'IntPtr', 'ptr'],
			[A_PtrSize, 'UIntPtr', 'uptr']
			] {
				temp[t[3]] := t[1]
			}

		return temp
	}
}

val := TypeSizeMapper.NumTypeSize["char"]

AssertEq(val, 1, A_LineNumber)

val := TypeSizeMapper.NumTypeSize["int64"]

AssertEq(val, 8, A_LineNumber)

val := TypeSizeMapper.NumTypeSize["ptr"]

AssertEq(val, A_PtrSize, A_LineNumber)

; do the same, but using static __Init()
class TypeSizeMapper2 {
	static NumTypeSize := ""
	
	static __New()
	{
global
		this.NumTypeSize := Map()
		for t in [
			[1,         'Int8' ,   'char'  ],
			[1,         'UInt8' ,  'uchar' ],
			[2,         'Int16',   'short' ],
			[2,         'UInt16',  'ushort'],
			[4,         'Int32',   'int'   ],
			[4,         'UInt32',  'uint'  ],
			[8,         'Int64',   'int64' ],
			[8,         'UInt64',  'uint64'],
			[4,         'Single',  'float' ],
			[8,         'Double',  'double'],
			[A_PtrSize, 'IntPtr',  'ptr'   ],
			[A_PtrSize, 'UIntPtr', 'uptr'  ]
		] {
			this.NumTypeSize[t[3]] := t[1]
		}
	}
}

val := TypeSizeMapper2.NumTypeSize["char"]

AssertEq(val, 1, A_LineNumber)

val := TypeSizeMapper2.NumTypeSize["int64"]

AssertEq(val, 8, A_LineNumber)

val := TypeSizeMapper2.NumTypeSize["ptr"]

AssertEq(val, A_PtrSize, A_LineNumber)

class sclass1
{
	static c2 := sclass2()
}

class sclass2
{
	x := 1
}

sc1 := sclass1()

Assert(!sc1.HasProp("c2"), A_LineNumber)

AssertEq(sclass1.c2.x, 1, A_LineNumber)

FileAppend "pass", "*"
