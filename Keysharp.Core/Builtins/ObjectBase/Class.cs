using System.ComponentModel.Design.Serialization;

namespace Keysharp.Builtins
{
    public class Class(params object[] args) : KeysharpObject(args)
    {
		// Fixed at construction, independently of the mutable Base and Prototype properties.
		internal Type OperatorType;

		public new static object staticCall(object @this, params object[] args)
        {
            if (args.Length == 0)
                return new Class();

            string name = args[0] as string;
            Any baseClass = name == null ? args[0] as Any : args.Length > 1 ? args[1] as Any : null;
            int skip = 2;
            var vars = TheScript.Vars;
            if (name == null)
            {
                name = "";
                skip--;
            }
            if (baseClass == null)
            {
                baseClass = vars.Statics[typeof(KeysharpObject)];
                skip--;
            }
            args = args[skip..^0];

			var baseProto = Script.GetPropertyValueOrNull(baseClass, "Prototype") as Any;
			if (baseProto == null)
				return Errors.ErrorOccurred("The base class must have a prototype");

			var baseType = baseProto.type;
			var isStruct = Struct.IsStructType(baseType);
			var userType = isStruct ? Struct.CreateDynamicStructType(name, baseType) : baseType;
			if (isStruct)
				TheScript.Operators.RegisterAlias(userType, baseType);
			var staticType = baseClass.GetType();
			Any staticInst = (Any)RuntimeHelpers.GetUninitializedObject(staticType);
			if (staticInst is Class created && baseClass is Class original)
				created.OperatorType = original.OperatorType;

			staticInst.SetBaseInternal(baseClass);
            staticInst.type = userType;

            // As AHK's Class_New: the prototype inherits the base's members through its base rather than copies of
            // them, and the base class's static __Init is not run again for the new class, which has none of its own.
            var proto = new Prototype(userType);
            proto.SetBaseInternal(baseProto);
			proto.DefinePropInternal("__Class", new OwnPropsDesc(name));
			staticInst.DefinePropInternal("Prototype", new OwnPropsDesc(proto));

			_ = Script.InvokeMeta(staticInst, "__New", args);

			return staticInst;
        }

        // Construction relays its arguments straight to __New, so a named argument names one of __New's parameters,
        // not one of Call's (it has none of its own): the container rides the tail of args and binds there.
        public object Call(params object[] args)
        {
			var own = op != null && op.TryGetValue("Prototype", out var desc) ? desc.Value : null;

			if ((own ?? Script.GetPropertyValueOrNull(this, "Prototype")) is not Any proto)
				return Errors.TypeErrorOccurred("This class has no Prototype to create an instance from.");

			var kso = FastCtor.Call(proto.type, null) as Any;
			kso.type = proto.type;

			kso.SetBaseInternal(proto);

			if (kso is Struct st)
				st.InitializeStructStorage();

			Script.InvokeMeta(kso, "__Init");
			Script.InvokeMeta(kso, "__New", args);
            return kso;
		}
	}

    // Every prototype object is one of these, and Type() reports "Prototype" for it, but as in AutoHotkey no global
    // class of that name exists.
    [PublicHiddenFromUser]
    public class Prototype : KeysharpObject
    {
        public Prototype(params object[] args) : base(args)
        {
            isPrototype = true;
            type = typeof(Prototype);
        }
        internal Prototype(Type t) : base(null)
        {
            isPrototype = true;
            type = t;
        }
	}
}
