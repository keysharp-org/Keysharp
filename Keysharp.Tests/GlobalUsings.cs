//System usings.
global using global::System;
global using global::System.CodeDom.Compiler;
global using global::System.Collections;
global using global::System.Collections.Generic;
global using global::System.Diagnostics;
global using global::System.Globalization;
global using global::System.IO;
global using global::System.Linq;
global using global::System.Reflection;
global using global::System.Runtime.InteropServices;
global using global::System.Text;
global using global::System.Threading;
global using global::System.Threading.Tasks;
#if WINDOWS
	global using global::System.Windows.Forms;
	global using global::System.Drawing;
#else
	global using global::Eto.Forms;
	global using global::Eto.Drawing;
#endif

//Our usings.
global using global::NUnit.Framework;
global using global::Keysharp.Builtins;
global using global::Keysharp.Internals.ExtensionMethods;
global using global::Keysharp.Internals.Input.Keyboard;
global using global::Keysharp.Internals.Scripting;
global using global::Keysharp.Compilation;
global using global::Keysharp.Language;
global using global::Keysharp.Parsing;
global using global::Keysharp.Runtime;

global using global::Keysharp.Internals.Input.Hooks;
global using global::Keysharp.Internals.Strings;
global using global::Keysharp.Internals.Threading;

#if WINDOWS
	global using global::Keysharp.Internals.Input.Windows;
	global using global::Keysharp.Internals.Os.Windows;
	global using global::Keysharp.Internals.Input.Hooks.Windows;
	global using global::Keysharp.Internals.Window.Windows;
	global using MessageFilter = Keysharp.Internals.Window.Windows.MessageFilter;
#else
	global using global::Keysharp.Internals.Window.Unix;
#endif
//Static
global using static global::Keysharp.Builtins.Accessors;
global using static global::Keysharp.Builtins.Ks;
global using static global::Keysharp.Language.Keywords;
