; Included at the end of module-compatibility-mode.ahk. It declares a module, whose mode its own first #Requires sets rather
; than that of the file it includes before it, and it includes compat_mode_nested.ahk on either side of that directive.
#Module IncludedModule
#Include compat_mode_nested.ahk
#Requires AutoHotkey v2.0
try {
	Map().Get("absent")
	StartupMode := "v2.1"
} catch
	StartupMode := "v2.0"

#IncludeAgain compat_mode_nested.ahk
AfterNestedNoReturn() {
}

; The child's #Module is in the including file.
#Module IncludedParent
#Requires AutoHotkey v2.0
try {
	Map().Get("absent")
	StartupMode := "v2.1"
} catch
	StartupMode := "v2.0"

; The parent's own #Module is in the including file.
#Module LocalParent:Child
