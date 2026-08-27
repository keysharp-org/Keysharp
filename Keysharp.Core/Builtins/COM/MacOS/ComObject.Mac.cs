#if OSX
using Keysharp.Internals.AppleEvents;
using Keysharp.Internals.Invoke;

namespace Keysharp.Builtins.COM
{
	/// <summary>
	/// A scriptable macOS application, or an object inside one, addressed as ComObject so the late-bound
	/// automation surface reads the same on every platform (the DllCall precedent). The IDispatch subset of COM
	/// maps closely: the scripting definition stands in for the type library, the get and set events for property
	/// access, elements for collections, and launching an application for CoCreateInstance. What has no analog —
	/// vtables, reference counts, raw interface pointers — throws rather than pretending (see Com.Mac.cs).
	/// <para>
	/// An Apple event object is a query rather than a handle: "window 1 of application Finder" is resolved by the
	/// application each time it is used. This object therefore holds the chain that describes it and evaluates it
	/// on access, which is what lets a collection be narrowed without a round trip.
	/// </para>
	/// </summary>
	// Derives from Any, not KeysharpObject, for the same reason the Windows ComValue does: Script.InvokeOrNull
	// tests for KeysharpObject (a callable object) BEFORE IMetaObject, so a KeysharpObject-derived meta object
	// is treated as callable and recurses until the stack is exhausted instead of dispatching by name.
	public class ComObject : Any, IMetaObject
	{
		internal AETarget target;
		internal List<AESpecifierStep> steps;
		internal string suite;                 // pinned suite, or null to search all of them
		internal string className;
		internal bool isCollection;
		internal ComAESink sink;               // live ComObjConnect subscription, if any

		private AEContext context;

		public ComObject(params object[] args) : base(args)
		{
			if (args != null && args.Length > 0)
				_ = Errors.ErrorOccurred("Construct an Apple Events ComObject by calling ComObject(name), not with New.");
		}

		internal ComObject(AETarget target, List<AESpecifierStep> steps, string suite) : base(null)
		{
			this.target = target;
			this.steps = steps ?? [];
			this.suite = suite;
			// What the object is follows from the last link in its chain, so a chain decoded out of a reply
			// describes itself as accurately as one this object model built. Only an empty chain is the
			// application itself.
			var last = this.steps.Count > 0 ? this.steps[^1] : null;

			if (last == null)
				className = RootClassName(Dictionary);
			else if (last.Kind != AESpecifierKind.Property && !string.IsNullOrEmpty(last.ClassName))
				className = last.ClassName;

			// A chain ending in a property names a value, and nothing in it says what class that value is, so the
			// class stays unknown rather than being guessed at as the application.
			isCollection = last != null && last.Kind == AESpecifierKind.AllElements;
		}

		/// <summary>ComObject("bundle.id" | "Name" | "/path/App.app" | "pid:123", "optional suite").</summary>
		public static object staticCall(object @this, object target, object suite = null)
			=> Create(target.As(), suite.As(), activate: true);

		// Addresses nothing until Create fills it in, which is the state a prototype instance stays in.
		public override string ToString() => target == null ? "" : AESpecifiers.Render(steps, target.ToString());

		internal static object Create(string targetText, string suiteName, bool activate)
		{
			AETarget resolved;

			try
			{
				resolved = AETargets.Resolve(targetText);
			}
			catch (Exception ex)
			{
				return Errors.ValueErrorOccurred(ex.Message);
			}

			try
			{
				// ComObject starts the application on demand, the way CoCreateInstance would; ComObjActive and
				// ComObjGet require one that is already running, like the running object table. Sending an event
				// never launches anything by itself, so this has to be explicit.
				if (activate)
					AETargets.Launch(resolved);
				else if (!AETargets.IsRunning(resolved))
					return Errors.ErrorOccurred($"No running application matches '{targetText}'.");

				var dictionary = AETerminology.Get(resolved);

				if (suiteName.Length > 0
						&& !dictionary.Suites.Any(s => string.Equals(s, suiteName, StringComparison.OrdinalIgnoreCase)))
					return Errors.ErrorOccurred(
							   $"'{targetText}' has no suite named '{suiteName}'. Available: {string.Join(", ", dictionary.Suites)}");

				return new ComObject(resolved, [], suiteName.Length > 0 ? suiteName : null);
			}
			catch (Exception ex) when (IsCallFailure(ex))
			{
				return Failed(ex);
			}
		}

		private static string RootClassName(AESdefDictionary dictionary)
			=> dictionary != null && dictionary.ClassesByCode.TryGetValue(AE.ClassApplication, out var cls) ? cls.Name : "application";

		// ---- terminology ------------------------------------------------------------------------

		// Resolving a single member consults the terminology several times, so it is held per object rather than
		// fetched per lookup.
		private AEContext Context => context ??= new AEContext
		{
			Dictionary = AETerminology.Get(target),
			Target = target,
			Suite = suite
		};

		private AESdefDictionary Dictionary => Context.Dictionary;

		/// <summary>The chain for this object with one more link on the end.</summary>
		private List<AESpecifierStep> Extend(AESpecifierStep step)
		{
			var chain = new List<AESpecifierStep>(steps.Count + 1);
			chain.AddRange(steps);
			chain.Add(step);
			return chain;
		}

		private bool TryResolveCommand(string name, out AESdefCommand command)
		{
			command = null;

			if (!Dictionary.CommandsByKey.TryGetValue(AESdef.Key(name), out var candidates) || candidates.Count == 0)
				return false;

			var matches = suite == null
						  ? candidates
						  : candidates.Where(c => string.Equals(c.Suite, suite, StringComparison.OrdinalIgnoreCase)).ToList();

			if (matches.Count == 0)
				return false;

			if (matches.Count > 1)
				throw new AmbiguousMatchException(
					$"'{name}' is defined by more than one suite ({string.Join(", ", matches.Select(m => m.Suite))}); pass the suite to ComObject or use ComObjQuery.");

			command = matches[0];
			return true;
		}

		private AESdefProperty ResolveProperty(string name) => Dictionary.FindProperty(className, AESdef.Key(name));

		private AESdefClass ResolveElement(string name) => Dictionary.FindElement(className, AESdef.Key(name));

		// ---- IMetaObject -------------------------------------------------------------------------

		object IMetaObject.Call(string name, object[] args)
		{
			// Apple event parameters are addressed by keyword, so unlike D-Bus the names are what carries them.
			var positional = NamedArgBinder.Split(args, out var named);

			if (isCollection && string.Equals(name, "ById", StringComparison.OrdinalIgnoreCase))
				return ElementById(positional);

			AESdefCommand command;

			try
			{
				if (!TryResolveCommand(name, out command))
				{
					// A property read as a call mirrors the property and method blur of IDispatch.
					if (positional.Length == 0 && named == null && ResolveProperty(name) is AESdefProperty property)
						return ReadProperty(property);

					return Errors.MethodErrorOccurred(Unknown(name, "command"));
				}
			}
			catch (AmbiguousMatchException ex)
			{
				return Errors.MethodErrorOccurred(ex.Message);
			}

			return Invoke(command, positional, named);
		}

		object IMetaObject.Get(string name, object[] args)
		{
			if (isCollection && string.Equals(name, "Count", StringComparison.OrdinalIgnoreCase))
				return Count();

			if (ResolveProperty(name) is AESdefProperty property)
				return ReadProperty(property);

			if (ResolveElement(name) is AESdefClass element)
				return Collection(element);

			try
			{
				// Reading the name of a command that needs no parameters runs it, as IDispatch would.
				if (TryResolveCommand(name, out var command) && !command.HasDirectParameter)
					return Invoke(command, [], null);
			}
			catch (AmbiguousMatchException ex)
			{
				return Errors.PropertyErrorOccurred(ex.Message);
			}

			return Errors.PropertyErrorOccurred(Unknown(name, "property"));
		}

		void IMetaObject.Set(string name, object[] args, object value)
		{
			if (ResolveProperty(name) is not AESdefProperty property)
			{
				_ = Errors.PropertyErrorOccurred(Unknown(name, "property"));
				return;
			}

			if (!property.CanWrite)
			{
				_ = Errors.PropertyErrorOccurred($"Property '{property.Name}' of {className} is read-only.");
				return;
			}

			var chain = Extend(new AESpecifierStep
			{
				Kind = AESpecifierKind.Property,
				PropertyCode = property.Code,
				PropertyName = property.Name
			});

			try
			{
				AECalls.SetData(target, chain, value, property.TypeName, Context);
			}
			catch (Exception ex) when (IsCallFailure(ex))
			{
				_ = Failed(ex);
			}
		}

		object IMetaObject.get_Item(object[] indexArgs) => get_Item(indexArgs);

		void IMetaObject.set_Item(object[] indexArgs, object value)
			=> _ = Errors.PropertyErrorOccurred("An element of a macOS application cannot be assigned to; set one of its properties instead.");

		/// <summary>Narrows a collection to one element, by position or by name.</summary>
		public object get_Item(params object[] args)
		{
			if (!isCollection)
				return Errors.PropertyErrorOccurred($"{ToString()} is not a collection, so it cannot be indexed.");

			if (args == null || args.Length != 1 || args[0] == null)
				return Errors.ValueErrorOccurred("Indexing a collection needs one position or name.");

			var last = steps[^1];
			var step = new AESpecifierStep { ClassCode = last.ClassCode, ClassName = last.ClassName };

			if (args[0] is string name)
			{
				step.Kind = AESpecifierKind.ElementByName;
				step.Name = name;
			}
			else
			{
				// Apple events count from one and read a negative index from the end, matching Keysharp's Array.
				step.Kind = AESpecifierKind.ElementByIndex;
				step.Index = args[0].Al();
			}

			return Narrow(step);
		}

		private object ElementById(object[] args)
		{
			if (args == null || args.Length != 1 || args[0] == null)
				return Errors.ValueErrorOccurred("ById needs one identifier.");

			var last = steps[^1];
			return Narrow(new AESpecifierStep
			{
				Kind = AESpecifierKind.ElementById,
				ClassCode = last.ClassCode,
				ClassName = last.ClassName,
				Id = args[0]
			});
		}

		/// <summary>Replaces the trailing "every" link with one that picks a single element out of it.</summary>
		private object Narrow(AESpecifierStep step)
		{
			var chain = new List<AESpecifierStep>(steps.Count);
			chain.AddRange(steps.Take(steps.Count - 1));
			chain.Add(step);
			return new ComObject(target, chain, suite);
		}

		private object Collection(AESdefClass element)
			=> new ComObject(target, Extend(new AESpecifierStep
		{
			Kind = AESpecifierKind.AllElements,
			ClassCode = element.Code,
			ClassName = element.Name
		}), suite);

		private object ReadProperty(AESdefProperty property)
		{
			if (!property.CanRead)
				return Errors.PropertyErrorOccurred($"Property '{property.Name}' of {className} is write-only.");

			var chain = Extend(new AESpecifierStep
			{
				Kind = AESpecifierKind.Property,
				PropertyCode = property.Code,
				PropertyName = property.Name
			});

			try
			{
				return AECalls.GetData(target, chain, Context);
			}
			catch (Exception ex) when (IsCallFailure(ex))
			{
				return Failed(ex);
			}
		}

		private object Count()
		{
			try
			{
				var last = steps[^1];
				return AECalls.CountElements(target, steps.Take(steps.Count - 1).ToList(), last.ClassCode, Context);
			}
			catch (Exception ex) when (IsCallFailure(ex))
			{
				return Failed(ex);
			}
		}

		/// <summary>
		/// Sends one command. The first positional argument is the direct parameter; everything else must be named,
		/// because that is how an Apple event addresses its parameters.
		/// </summary>
		private object Invoke(AESdefCommand command, object[] positional, Ks.NamedArgs named)
		{
			var request = new AECallRequest
			{
				Target = target,
				EventClass = command.EventClass,
				EventId = command.EventId,
				Context = Context
			};

			if (positional.Length > 1)
				return Errors.ValueErrorOccurred(
						   $"'{command.Name}' takes at most one unnamed argument (its direct parameter); name the rest, as in {command.Name}(With: value).");

			if (positional.Length == 1)
			{
				// The receiver would already be the direct parameter, so supplying one too says two different
				// things about the same slot.
				if (steps.Count > 0)
					return Errors.ValueErrorOccurred(
							   $"'{command.Name}' is being sent to {ToString()}, which is already its direct parameter; pass the other values by name.");

				request.HasDirectValue = true;
				request.DirectValue = positional[0];
				request.DirectTypeName = command.DirectTypeName;
			}
			else if (steps.Count > 0 && command.HasDirectParameter)
				request.DirectSpecifier = steps;

			if (named?.map != null)
			{
				request.Parameters = [];

				foreach (var kv in named.map)
				{
					var name = kv.Key.As();

					if (!command.TryGetParameter(AESdef.Key(name), out var parameter))
						return Errors.ValueErrorOccurred(
								   $"'{command.Name}' has no parameter named '{name}'. It accepts: {Names(command)}");

					request.Parameters.Add((parameter.Code, kv.Value, parameter.TypeName));
				}
			}

			try
			{
				return AECalls.Send(request);
			}
			catch (Exception ex) when (IsCallFailure(ex))
			{
				return Failed(ex);
			}
		}

		/// <summary>
		/// Evaluates a collection into an Array. A for loop asks the application once and walks the answer, rather
		/// than sending an event per element.
		/// </summary>
		internal Keysharp.Builtins.Array Evaluate()
		{
			var result = AECalls.GetData(target, steps, Context);

			if (result is Keysharp.Builtins.Array array)
				return array;

			var single = new Keysharp.Builtins.Array();

			if (result != null && !(result is string s && s.Length == 0))
				_ = single.Push(result);

			return single;
		}

		public KeysharpFunc __Enum(object count)
		{
			if (!isCollection)
			{
				_ = Errors.ErrorOccurred($"{ToString()} is not a collection, so it cannot be enumerated.");
				return null;
			}

			try
			{
				return Evaluate().__Enum(count);
			}
			catch (Exception ex) when (IsCallFailure(ex))
			{
				_ = Failed(ex);
				return null;
			}
		}

		/// <summary>
		/// The failures a call can produce: the application or the Apple Event Manager refusing it, and the
		/// marshaller rejecting a value on the way out. Both surface as script errors; anything else is a defect
		/// in this layer and is left to propagate.
		/// </summary>
		private static bool IsCallFailure(Exception ex) => ex is AEException or ArgumentException or OverflowException;

		private static object Failed(Exception ex)
			=> ex is AEException
			   ? Errors.OSErrorOccurredWithMessage(ex.Message)
			   : Errors.ValueErrorOccurred(ex.Message);

		// ---- notifications -----------------------------------------------------------------------

		internal void Connect(object sinkOrPrefix)
		{
			sink?.Dispose();
			sink = null;

			if (sinkOrPrefix == null)
				return;

			sink = new ComAESink(this, sinkOrPrefix);
		}

		// ---- diagnostics -------------------------------------------------------------------------

		/// <summary>The descriptor for this object's chain, for the marshaller to send. The caller owns it.</summary>
		internal AEValue BuildSpecifier() => AESpecifiers.Build(steps);

		internal string BundleId => target?.BundleId;

		/// <summary>
		/// A miss reports what the object does offer. The terminology is the only map a script has of an
		/// application, so naming the near neighbours is most of the diagnosis.
		/// </summary>
		private string Unknown(string name, string kind)
		{
			var dictionary = Dictionary;
			var properties = new List<string>();
			var elements = new List<string>();

			foreach (var cls in dictionary.Ancestry(className))
			{
				foreach (var property in cls.Properties.Values)
					if (!property.Hidden && !properties.Contains(property.Name))
						properties.Add(property.Name);

				foreach (var element in cls.Elements.Values)
				{
					var target = dictionary.ResolveClass(element.TypeName);

					if (target != null && !element.Hidden)
					{
						var plural = AESdefDictionary.Plural(target);

						if (!elements.Contains(plural))
							elements.Add(plural);
					}
				}
			}

			var text = new StringBuilder();
			_ = text.Append($"'{name}' is not a {kind} of {ToString()}.");

			if (properties.Count > 0)
				_ = text.Append($" Properties: {Join(properties)}.");

			if (elements.Count > 0)
				_ = text.Append($" Elements: {Join(elements)}.");

			return text.ToString();
		}

		private static string Names(AESdefCommand command)
		{
			var names = command.Parameters.Values.Where(p => !p.Hidden).Select(p => p.Name).Distinct().ToList();
			return names.Count > 0 ? Join(names) : "no named parameters";
		}

		private static string Join(List<string> names)
			=> string.Join(", ", names.Take(24)) + (names.Count > 24 ? ", ..." : "");
	}
}
#endif
