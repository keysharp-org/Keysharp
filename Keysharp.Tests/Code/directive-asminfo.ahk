#import KS { A_AssemblyCompany, A_AssemblyConfiguration, A_AssemblyCopyright, A_AssemblyDescription, A_AssemblyName, A_AssemblyProduct, A_AssemblyTitle, A_AssemblyTrademark, A_AssemblyVersion }
#NoTrayIcon

#ASSEMBLYTITLE This is a title!
#ASSEMBLYDESCRIPTION This is a description!
#ASSEMBLYCONFIGURATION This is a config!
#ASSEMBLYCOMPANY This is a company!
#ASSEMBLYPRODUCT This is a product!
#ASSEMBLYCOPYRIGHT This is a copyright!
#ASSEMBLYTRADEMARK This is a trademark!
#ASSEMBLYVERSION 9.8.7.6
#ASSEMBLYNAME ThisIsAnAsmName

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

if (A_AssemblyName == "ThisIsAnAsmName")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
