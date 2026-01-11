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
global using global::Keysharp.Core;
global using global::Keysharp.Core.Common.Keyboard;
global using global::Keysharp.Core.Common.ObjectBase;
global using global::Keysharp.Core.Common.Strings;

#if WINDOWS
	global using global::Keysharp.Core.Windows;
#endif

global using global::Keysharp.Scripting;

//Static
global using static global::Keysharp.Core.Accessors;
global using static global::Keysharp.Core.KeysharpEnhancements;
global using static global::Keysharp.Scripting.Keywords;