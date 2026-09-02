//System usings.
global using global::System;
global using global::System.Buffers;
global using global::System.Data;
global using global::System.CodeDom;
global using global::System.CodeDom.Compiler;
global using global::System.Collections;
global using global::System.Collections.Concurrent;
global using global::System.Collections.Frozen;
global using global::System.Collections.Generic;
global using global::System.Collections.Immutable;
global using global::System.Collections.ObjectModel;
global using global::System.Collections.Specialized;
global using global::System.ComponentModel;
global using global::System.Diagnostics;
global using global::System.Globalization;
global using global::System.IO;
global using global::System.IO.Compression;
global using global::System.Linq;
global using global::System.Net;
global using global::System.Net.Http;
global using global::System.Net.Http.Headers;
global using global::System.Net.Mail;
global using global::System.Net.Sockets;
global using global::System.Reflection;
global using global::System.Reflection.Emit;
global using global::System.Runtime.CompilerServices;
global using global::System.Runtime.ExceptionServices;
global using global::System.Runtime.InteropServices;
global using global::System.Runtime.Loader;
global using global::System.Security;
global using global::System.Security.Cryptography;
global using global::System.Security.Principal;
global using global::System.Text;
global using global::System.Text.Json;
global using global::System.Text.RegularExpressions;
global using global::System.Threading;
global using global::System.Threading.Channels;
global using global::System.Threading.Tasks;
#if WINDOWS
	global using global::System.Windows.Forms;
	global using Forms = System.Windows.Forms;
	global using global::System.Drawing;
	global using global::System.Drawing.Drawing2D;
	global using global::System.Drawing.Imaging;
#else
	global using global::Eto.Drawing;
	global using global::Eto.Forms;
	global using Forms = Eto.Forms;
	global using Range = System.Range;
	global using Keyboard = Keysharp.Builtins.Keyboard;
	global using FormWindowState = Eto.Forms.WindowState;
	global using StatusStrip = Keysharp.Builtins.KeysharpStatusStrip;
	global using DockStyle = System.Windows.Forms.DockStyle;
	global using ColumnHeader = System.Windows.Forms.ColumnHeader;
	global using TextBoxBase = Eto.Forms.TextBox;
	global using Keys = System.Windows.Forms.Keys;
#endif

// Alias String to avoid conflicts with Keysharp.Builtins.String
global using String = System.String;
global using POINT = Keysharp.Internals.Window.POINT;
#if WINDOWS
	global using UITimer = System.Windows.Forms.Timer;
#endif
global using Module = Keysharp.Runtime.Module;

//Our usings.
global using global::Keysharp.Internals.Containers;
global using global::Keysharp.Internals.Cryptography;
global using Diagnostics = global::Keysharp.Internals.Diagnostics;
global using global::Keysharp.Internals.ExtensionMethods;

global using global::Keysharp.Internals.Input.Hooks;
global using global::Keysharp.Internals.Images;
global using global::Keysharp.Internals.Input;
global using global::Keysharp.Internals.Input.Joystick;
global using global::Keysharp.Internals.Input.Keyboard;
global using global::Keysharp.Internals.Input.Mouse;
global using global::Keysharp.Internals.Mapper;
global using global::Keysharp.Internals.Os;
global using Platform = global::Keysharp.Internals.Platform;
global using static global::Keysharp.Internals.Platform.Keys;
global using static global::Keysharp.Internals.Platform.Library;
global using static global::Keysharp.Internals.Platform.Process;
global using static global::Keysharp.Internals.Platform.Desktop;
global using static global::Keysharp.Internals.Platform.Pointer;
global using static global::Keysharp.Internals.Platform.Messages;
global using static global::Keysharp.Internals.Platform.Time;
global using static global::Keysharp.Internals.Window.WindowCoords;
global using global::Keysharp.Builtins.COM;
global using global::Keysharp.Internals.Scripting;
global using global::Keysharp.Internals.Strings;
global using global::Keysharp.Internals.Threading;
global using global::Keysharp.Internals.Events;
global using global::Keysharp.Internals.Window;
global using global::Keysharp.Internals.Interop;
global using global::Keysharp.Internals.Invoke;
global using global::Keysharp.Language;
global using global::Keysharp.Runtime;
global using global::Semver.Comparers;
global using global::Semver.Utility;
global using global::PCRE;
global using global::BitFaster.Caching.Lfu;
global using global::BitFaster.Caching.Scheduler;

#if WINDOWS
	global using global::Accessibility;
	global using global::Microsoft.Win32;
	global using global::Microsoft.Win32.SafeHandles;
	global using global::System.Management;
	global using global::System.Media;
	global using global::System.Runtime.InteropServices.ComTypes;
	global using global::Keysharp.Internals.Input.Windows;
	global using global::Keysharp.Internals.Os.Windows;
	global using global::Keysharp.Internals.Input.Hooks.Windows;
	global using global::Keysharp.Internals.Window.Windows;
	global using AboutBox = Keysharp.Internals.Window.Windows.AboutBox;
	global using KeysharpActiveX = Keysharp.Internals.Window.Windows.KeysharpActiveX;
	global using MainWindow = Keysharp.Internals.UI.Windows.MainWindow;
	global using MessageFilter = Keysharp.Internals.Window.Windows.MessageFilter;
#endif

#if !WINDOWS
	global using global::Keysharp.Internals.Input.Unix;
	global using global::Keysharp.Internals.Os.Unix;
	global using global::Keysharp.Internals.Input.Hooks.Unix;
	global using global::Keysharp.Internals.Window.Unix;
	global using AboutBox = Keysharp.Internals.Window.Unix.AboutBox;
	global using MainWindow = Keysharp.Internals.UI.Unix.MainWindow;
#endif

#if LINUX
	global using global::Keysharp.Internals.Input.Linux;
	global using global::Keysharp.Internals.Input.Hooks.Linux;
	global using global::Keysharp.Internals.Window.Linux;
	global using global::Keysharp.Internals.Window.Linux.Proxies;
	global using global::Keysharp.Internals.Window.Linux.X11;
	global using global::Keysharp.Internals.Window.Linux.Wayland;
#endif

#if OSX
	global using global::Keysharp.Internals.Input.MacOS;
	global using global::Keysharp.Internals.Input.Hooks.MacOS;
	global using global::Keysharp.Internals.Window.MacOS;
#endif

//Static
global using static global::Keysharp.Builtins.Accessors;
global using static global::Keysharp.Language.Keywords;
global using static global::Keysharp.Runtime.Script;

#if WINDOWS
#endif

#if !WINDOWS
#endif
