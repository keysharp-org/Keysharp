#import KS { App }
#NoTrayIcon

#App {
	Name: "ThisIsAnAsmName",
	Title: "This is a title!",
	Description: "This is a description!",
	Configuration: "This is a config!",
	Company: "This is a company!",
	Product: "This is a product!",
	Copyright: "This is a copyright!",
	Trademark: "This is a trademark!",
	Version: "9.8.7.6",
}
#Include <assert>

AssertEq(App.Title, "This is a title!", A_LineNumber)

AssertEq(App.Description, "This is a description!", A_LineNumber)

AssertEq(App.Configuration, "This is a config!", A_LineNumber)

AssertEq(App.Company, "This is a company!", A_LineNumber)

AssertEq(App.Product, "This is a product!", A_LineNumber)

AssertEq(App.Copyright, "This is a copyright!", A_LineNumber)

AssertEq(App.Trademark, "This is a trademark!", A_LineNumber)

AssertEq(App.Version, "9.8.7.6", A_LineNumber)

AssertEq(App.Name, "ThisIsAnAsmName", A_LineNumber)

; The process command line always starts with the host executable, and A_Args is the script's own input.
Assert(InStr(App.CommandLine, A_AhkPath) = 1 || InStr(App.CommandLine, '"' A_AhkPath '"') = 1, A_LineNumber)

; No exit is in progress, so the exit state reads empty.
AssertEq(App.ExitReason, "", A_LineNumber)

AssertEq(App.ExitCode, 0, A_LineNumber)

; App has no instances.
Throws(() => App(), A_LineNumber)

; Every member is read-only.
Throws(() => App.Title := "nope", A_LineNumber)

FileAppend "pass", "*"
