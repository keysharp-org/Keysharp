using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Keysharp.Scripting
{
	internal partial class Parser
	{
        internal class MethodReference
        {
            internal string MethodName { get; set; }

            internal Type TargetObject { get; set; }

            internal Type[] TypeArguments { get; set; }

            internal MethodReference(Type targetObject, string methodName)
                : this(targetObject, methodName, null) { }

            internal MethodReference(Type targetObject, string methodName, Type[] typeArguments)
            {
                TargetObject = targetObject;
                MethodName = methodName;
                TypeArguments = typeArguments;
            }

            public static explicit operator InvocationExpressionSyntax(MethodReference source)
            {
                return SyntaxFactory.InvocationExpression(Parser.CreateMemberAccess(source.TargetObject.FullName, source.MethodName));
            }

            public static explicit operator MethodInfo(MethodReference source)
            {
                return source.TypeArguments == null ? source.TargetObject.GetMethod(source.MethodName) : source.TargetObject.GetMethod(source.MethodName, source.TypeArguments);
            }
        }

        internal class InternalMethods
		{
			internal static MethodReference AddHotkey => new (typeof(HotkeyDefinition), "AddHotkey");
			internal static MethodReference AddHotstring => new (typeof(HotstringManager), "AddHotstring");
			internal static MethodReference CreateTrayMenu => new (typeof(Script), "CreateTrayMenu");
			internal static MethodReference Exit => new (typeof(Flow), "Exit");
			internal static MethodReference ExitApp => new (typeof(Flow), "ExitApp");
			internal static MethodReference ExitIfNotPersistent => new (typeof(Script), "ExitIfNotPersistent");
			internal static MethodReference ExtendArray => new (typeof(Script), "ExtendArray");
			internal static MethodReference ForceBool => new (typeof(Script), "ForceBool");
			internal static MethodReference ForceString => new(typeof(Script), "ForceString");
			internal static MethodReference FlattenParam => new (typeof(Script), "FlattenParam");
			internal static MethodReference Func => new (typeof(Functions), "Func");
            internal static MethodReference Closure => new(typeof(Functions), "Closure");
            internal static MethodReference GetMethodOrProperty => new (typeof(Script), "GetMethodOrProperty");
			internal static MethodReference GetPropertyValue => new (typeof(Script), "GetPropertyValue");
			internal static MethodReference HandleSingleInstance => new (typeof(Script), "HandleSingleInstance");
			internal static MethodReference HotIf => new (typeof(Keyboard), "HotIf");
			internal static MethodReference Hotkey => new (typeof(Keyboard), "Hotkey");
			internal static MethodReference Hotstring => new (typeof(Keyboard), "Hotstring");
            internal static MethodReference IfTest => new (typeof(Script), "IfTest");
			internal static MethodReference IfLegacy => new (typeof(Script), "IfLegacy");
			internal static MethodReference IsTrueAndRunning => new (typeof(Flow), "IsTrueAndRunning");
			internal static MethodReference Inc => new (typeof(Loops), "Inc");
			internal static MethodReference Index => new (typeof(Script), "Index");
			internal static MethodReference Invoke => new (typeof(Script), "Invoke");
			internal static MethodReference LabelCall => new (typeof(Script), "LabelCall");
			internal static MethodReference Loop => new (typeof(Loops), "Loop");
			internal static MethodReference LoopFile => new (typeof(Loops), "LoopFile");
			internal static MethodReference LoopParse => new (typeof(Loops), "LoopParse");
			internal static MethodReference LoopRead => new (typeof(Loops), "LoopRead");
#if WINDOWS
			internal static MethodReference LoopRegistry => new (typeof(Loops), "LoopRegistry");
#endif
			internal static MethodReference MakeObjectTuple => new (typeof(Script), "MakeObjectTuple");
			internal static MethodReference MultiStatement => new (typeof(Script), "MultiStatement");
			internal static MethodReference Object => new (typeof(Objects), "Object");
			internal static MethodReference Operate => new (typeof(Script), "Operate");
			internal static MethodReference OperateTernary => new (typeof(Script), "OperateTernary");
			internal static MethodReference OperateUnary => new (typeof(Script), "OperateUnary");
			internal static MethodReference OperateZero => new (typeof(Script), "OperateZero");
			internal static MethodReference PostfixIncDecIndex => new (typeof(Script), "PostfixIncDecIndex");
			internal static MethodReference PostfixIncDecProp => new (typeof(Script), "PostfixIncDecProp");
			internal static MethodReference OrMaybe => new (typeof(Script), "OrMaybe");
			internal static MethodReference Parameter => new (typeof(Script), "Parameter");
			internal static MethodReference Parameters => new (typeof(Script), "Parameters");
			internal static MethodReference Pop => new (typeof(Loops), "Pop");
			internal static MethodReference Push => new (typeof(Loops), "Push");
			internal static MethodReference PopTry => new (typeof(Loops), "PopTry");
			internal static MethodReference PushTry => new (typeof(Loops), "PushTry");
			internal static MethodReference RunMainWindow => new (typeof(Script), "RunMainWindow");
			internal static MethodReference Send => new (typeof(Keyboard), "Send");
			internal static MethodReference SetObject => new (typeof(Script), "SetObject");
			internal static MethodReference SetPropertyValue => new (typeof(Script), "SetPropertyValue");
			internal static MethodReference SetReady => new (typeof(Script), "SetReady");
			internal static MethodReference StringConcat => new (typeof(string), "Concat", [typeof(object)]);
			internal static MethodReference Super => new(typeof(Script), "Super");
			internal static MethodReference WaitThreads => new (typeof(Script), "WaitThreads");
		}
	}
}