global using global::Keysharp.Core;
global using global::Keysharp.Scripting;
global using global::System;
global using global::System.Collections.Generic;
global using global::System.Diagnostics;
global using global::System.IO;
global using global::System.Linq;
global using global::System.Reflection;
global using global::System.Text;
#if WINDOWS
    global using global::ScintillaNET;
    global using global::System.Drawing;
    global using global::System.Windows.Forms;
    global using UITimer = System.Windows.Forms.Timer;
#else
    global using global::Eto.Drawing;
    global using global::Eto.Forms;
#endif
