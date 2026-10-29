using System.ComponentModel.Design.Serialization;

namespace Keysharp.Builtins
{
    public class Class(params object[] args) : KeysharpObject(args)
    {
		// Fixed at construction, independently of the mutable Base and Prototype properties.
		internal Any InstancePrototype;

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

			if (baseClass is not Class { InstancePrototype: { } baseProto } original)
				return Errors.ErrorOccurred("The base class must have a prototype");

			var baseType = baseProto.type;
			var isStruct = Struct.IsStructType(baseType);
			var userType = isStruct ? Struct.CreateDynamicStructType(name, baseType) : baseType;
			if (isStruct)
				TheScript.Operators.RegisterAlias(userType, baseType);
			var staticInst = (Class)RuntimeHelpers.GetUninitializedObject(original.GetType());

			staticInst.SetBaseInternal(baseClass);
            staticInst.type = userType;

            // As AHK's Class_New: the prototype inherits the base's members through its base rather than copies of
            // them, and the base class's static __Init is not run again for the new class, which has none of its own.
            var proto = new Prototype(userType);
            proto.SetBaseInternal(baseProto);
			proto.DefinePropInternal("__Class", new OwnPropsDesc(name));
			staticInst.DefinePropInternal("Prototype", new OwnPropsDesc(proto));
			staticInst.InstancePrototype = proto;

			// A struct class has a type of its own, by which its fields, DllCall and callbacks create its instances.
			if (isStruct)
			{
				vars.Prototypes[userType] = proto;
				vars.Statics[userType] = staticInst;
			}

			_ = Script.InvokeMeta(staticInst, "__New", args);

			return staticInst;
        }

        // Construction relays its arguments straight to __New, so a named argument names one of __New's parameters,
        // not one of Call's (it has none of its own): the container rides the tail of args and binds there.
        // As in AutoHotkey, a script reaches it only through a class's own Call, such as Object's, and not through
        // Class.Prototype, so a class with none in its chain, as Primitive and VarRef have none, cannot be called.
        [PublicHiddenFromUser]
        public object Call(params object[] args)
        {
			var own = op != null && op.TryGetValue("Prototype", out var desc) ? desc.Value : null;

			if ((own ?? Script.GetPropertyValueOrNull(this, "Prototype")) is not Any proto)
				return Errors.TypeErrorOccurred("This class has no Prototype to create an instance from.");

			if (!CanConstruct(proto.type))
				return Errors.ValueErrorOccurred("Invalid base.");

			var kso = FastCtor.Call(proto.type, null) as Any;
			kso.type = proto.type;

			kso.SetBaseInternal(proto);

			if (kso is Struct st)
				st.InitializeStructStorage();

			Script.InvokeMeta(kso, "__Init");
			Script.InvokeMeta(kso, "__New", args);
            return kso;
		}

		internal static bool CanConstruct(Type type) => type != null && typeof(Any).IsAssignableFrom(type)
			&& !typeof(RegExMatchInfo).IsAssignableFrom(type);
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
