//System usings.
global using global::System;
global using global::System.Collections.Concurrent;
global using global::System.Collections.Generic;
global using global::System.Linq;
global using global::System.Linq.Expressions;
global using global::System.Reflection;
global using global::System.Reflection.Emit;
global using global::System.Runtime.CompilerServices;
global using global::System.Text;
global using global::System.Threading.Tasks;

//Our usings.
global using global::BenchmarkDotNet.Attributes;
global using global::BenchmarkDotNet.Configs;
global using global::BenchmarkDotNet.Exporters;
global using global::BenchmarkDotNet.Loggers;
global using global::BenchmarkDotNet.Running;
global using global::Keysharp.Builtins;
global using global::Keysharp.Internals.Invoke;
global using global::Keysharp.Internals.ExtensionMethods;
global using global::Keysharp.Compilation;
global using global::Keysharp.Runtime;

//Static usings.
global using static global::Keysharp.Builtins.Functions;
global using static global::Keysharp.Runtime.Script.Operator;

//Aliases.
global using Module = global::Keysharp.Runtime.Module;
