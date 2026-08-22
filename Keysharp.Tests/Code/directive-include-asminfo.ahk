#import KS { A_AssemblyCompany, A_AssemblyConfiguration, A_AssemblyCopyright, A_AssemblyDescription, A_AssemblyProduct, A_AssemblyTitle, A_AssemblyTrademark, A_AssemblyVersion }
#include "directive-header-asminfo.ahk"
#Include <assert>

AssertEq(A_AssemblyTitle, "This is a title!", A_LineNumber)

AssertEq(A_AssemblyDescription, "This is a description!", A_LineNumber)

AssertEq(A_AssemblyConfiguration, "This is a config!", A_LineNumber)

AssertEq(A_AssemblyCompany, "This is a company!", A_LineNumber)

AssertEq(A_AssemblyProduct, "This is a product!", A_LineNumber)

AssertEq(A_AssemblyCopyright, "This is a copyright!", A_LineNumber)

AssertEq(A_AssemblyTrademark, "This is a trademark!", A_LineNumber)

AssertEq(A_AssemblyVersion, "9.8.7.6", A_LineNumber)

FileAppend "pass", "*"
