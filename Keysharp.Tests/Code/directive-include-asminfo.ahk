#import KS { App }
#include "directive-header-asminfo.ahk"
#Include <assert>

AssertEq(App.Title, "This is a title!", A_LineNumber)

AssertEq(App.Description, "This is a description!", A_LineNumber)

AssertEq(App.Configuration, "This is a config!", A_LineNumber)

AssertEq(App.Company, "This is a company!", A_LineNumber)

AssertEq(App.Product, "This is a product!", A_LineNumber)

AssertEq(App.Copyright, "This is a copyright!", A_LineNumber)

AssertEq(App.Trademark, "This is a trademark!", A_LineNumber)

AssertEq(App.Version, "9.8.7.6", A_LineNumber)

FileAppend "pass", "*"
