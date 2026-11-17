namespace Keysharp.Builtins
{
    public class Class : KeysharpObject
    {
		// Fixed at construction, independently of the mutable Base and Prototype properties.
		internal Prototype InstancePrototype;

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

			if (baseClass is not Class { InstancePrototype: not null } original)
				return Errors.ErrorOccurred("The base class must have a prototype");

			var staticInst = Create(name, original);
			_ = Script.InvokeMeta(staticInst, "__New", args);
			return staticInst;
        }

		// As AHK's Class_New: the prototype inherits the base's members through its base rather than copies of them, and
		// the base class's static __Init is not run again for the new class, which has none of its own. The new class's
		// instances are of its base's native type; its prototype is what identifies it.
		internal static Class Create(string name, Class baseClass)
		{
			using var registering = Script.classRegistration.EnterScope();
			var baseProto = baseClass.InstancePrototype;
			var staticInst = (Class)FastCtor.Instantiate(baseClass.GetType());
			staticInst.SetBaseInternal(baseClass);
			staticInst.type = baseProto.type;

			var proto = new Prototype(baseProto.type) { owner = staticInst };
			proto.SetBaseInternal(baseProto);
			proto.DefinePropInternal("__Class", new OwnPropsDesc(name));
			staticInst.DefinePropInternal("Prototype", new OwnPropsDesc(proto));
			staticInst.InstancePrototype = proto;
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

			if (FactoryFor(proto) is not { } factory)
				return Errors.ValueErrorOccurred("Invalid base.");

			var instance = factory.Create(proto);

			if (instance == null)
				return DefaultObject;

			instance.Initialize(args);
			return instance;
		}

		// How a script creates instances of the prototype's class, or null when it cannot, as an AutoHotkey class has no
		// factory. A Prototype keeps it, as AutoHotkey keeps the native constructor with the prototype.
		internal static FastCtor.Factory FactoryFor(Any prototype)
		{
			if (prototype is Prototype { factory: { } cached })
				return cached;

			var factory = CanConstruct(prototype.type) ? FastCtor.For(prototype.type) : null;

			if (prototype is Prototype p)
				p.factory = factory;

			return factory;
		}

		internal static bool CanConstruct(Type type) => FastCtor.CanConstruct(type)
			&& !typeof(RegExMatchInfo).IsAssignableFrom(type) && type != typeof(Ks.Timer)
			&& !typeof(Gui.Control).IsAssignableFrom(type) && !typeof(Ks.KeysharpScript).IsAssignableFrom(type);
	}

    // Every prototype object is one of these, and Type() reports "Prototype" for it, but as in AutoHotkey no global
    // class of that name exists.
    [PublicHiddenFromUser]
    public class Prototype : KeysharpObject
    {
        internal FastCtor.Factory factory;
		// The class the prototype belongs to.
		internal Class owner;
		// The native structure the prototype declares, created when first needed.
		internal StructDefinition definition;

        internal Prototype(Type t)
        {
            isPrototype = true;
            type = t;
        }
	}
}
