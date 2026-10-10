//System
global using global::System;
global using global::System.Collections.Generic;
global using global::System.Diagnostics;
global using global::System.IO;
global using global::System.IO.Pipes;
global using global::System.Linq;
global using global::System.Reflection;
global using global::System.Reflection.Metadata;
global using global::System.Reflection.PortableExecutable;
global using global::System.Runtime.CompilerServices;
global using global::System.Runtime.InteropServices;
global using global::System.Security.Cryptography;
global using global::System.Security.Principal;
global using global::System.Text;
global using global::System.Threading;

//Ours
global using global::Keysharp.Builtins;
global using global::Keysharp.Components.Scripting;
global using global::Keysharp.Internals.ExtensionMethods;
global using global::Keysharp.Internals.Scripting;
global using global::Keysharp.Internals.Strings;
global using global::Keysharp.Language;
global using global::Keysharp.Runtime;
#if WINDOWS
global using global::Keysharp.Internals.Os.Windows;
#endif

//Third party
global using global::Microsoft.NET.HostModel.AppHost;
#if WINDOWS
global using global::Microsoft.Win32;
global using global::Microsoft.Win32.SafeHandles;
global using global::System.Windows.Forms;
#else
	global using global::Eto.Forms;
#endif
