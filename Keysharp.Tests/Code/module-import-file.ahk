#NoTrayIcon

#import "module_import_file_target" { * }
#import Lib/module_import_path_target
#import Lib/implicit_default_import_target
#import Lib/implicit_default_import_target as implicit_default_alias
#Include <assert>
AssertEq(Success(), "imported", A_LineNumber)
AssertEq(module_import_path_target.PathSuccess(), "imported by path", A_LineNumber)

AssertEq(ImportedClass.Value, 42, A_LineNumber)

Assert(implicit_default_import_target.implicit_default_import_target.Value == 43
	&& implicit_default_alias.implicit_default_import_target.Value == 43, A_LineNumber)

ScopedImplicitDefault() {
	#import Lib/implicit_default_import_target
	return implicit_default_import_target.implicit_default_import_target.Value
}

AssertEq(ScopedImplicitDefault(), 43, A_LineNumber)

FileAppend "pass", "*"
