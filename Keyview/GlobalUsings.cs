global using global::Keysharp.Builtins;
global using global::Keysharp.Components.Scripting;
global using global::Keysharp.Internals.Scripting;
global using global::Keysharp.Language;
global using global::Keysharp.Runtime;
global using global::System;
global using global::System.Collections.Generic;
global using global::System.Diagnostics;
global using global::System.IO;
global using global::System.Linq;
global using global::System.Reflection;
global using global::System.Text;
global using global::System.Threading.Tasks;
#if WINDOWS
    global using global::ScintillaNET;
    global using global::System.Drawing;
    global using global::System.Windows.Forms;
    global using UITimer = System.Windows.Forms.Timer;
#else
    global using global::Eto.Drawing;
    global using global::Eto.Forms;
#endif
