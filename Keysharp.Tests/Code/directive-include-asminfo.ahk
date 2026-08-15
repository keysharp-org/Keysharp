#import KS { A_AssemblyCompany, A_AssemblyConfiguration, A_AssemblyCopyright, A_AssemblyDescription, A_AssemblyProduct, A_AssemblyTitle, A_AssemblyTrademark, A_AssemblyVersion }
#include "directive-header-asminfo.ahk"

if (A_AssemblyTitle == "This is a title!")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (A_AssemblyDescription == "This is a description!")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (A_AssemblyConfiguration == "This is a config!")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (A_AssemblyCompany == "This is a company!")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (A_AssemblyProduct == "This is a product!")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (A_AssemblyCopyright == "This is a copyright!")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (A_AssemblyTrademark == "This is a trademark!")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (A_AssemblyVersion == "9.8.7.6")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"