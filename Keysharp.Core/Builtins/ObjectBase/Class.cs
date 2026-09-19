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
			staticInst.type = typeof(Class); staticInst.InitializePrivates();
			if (staticInst is Class created && baseClass is Class original)
				created.OperatorType = original.OperatorType;

			staticInst.SetBaseInternal(baseClass);
            staticInst.type = userType;

            var proto = new Prototype(userType);
            proto.SetBaseInternal(baseProto);

            if (baseProto.op != null && !Struct.IsStructType(userType))
            {
                proto.EnsureOwnProps();
                foreach (var (key, value) in baseProto.op)
                    proto.DefinePropInternal(key, new OwnPropsDesc(proto, value.Value, value.Get, value.Set, value.Call));
            }
			proto.DefinePropInternal("__Class", new OwnPropsDesc(proto, name));
			staticInst.DefinePropInternal("Prototype", new OwnPropsDesc(staticInst, proto));

			_ = Script.InvokeMeta(staticInst, "__Init");
			_ = Script.InvokeMeta(staticInst, "__New", args);

			return staticInst;
        }

        // Construction relays its arguments straight to __New, so a named argument names one of __New's parameters,
        // not one of Call's (it has none of its own): the container rides the tail of args and binds there.
        public object Call(params object[] args)
        {
			var proto = (this.op["Prototype"].Value ?? Script.GetPropertyValueOrNull(this, "Prototype")) as Any;

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
        internal Prototype(Type t) : base()
        {
            isPrototype = true;
            type = t;
        }
	}
}
