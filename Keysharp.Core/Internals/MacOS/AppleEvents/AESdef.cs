using System.Xml.Linq;

namespace Keysharp.Internals.AppleEvents
{
	/// <summary>One parameter of a scripting command, addressed on the wire by its four-character keyword.</summary>
	internal sealed class AESdefParameter
	{
		internal string Name;
		internal uint Code;
		internal string TypeName;
		internal bool Optional;
		internal bool Hidden;
	}

	/// <summary>
	/// A scripting command. The sdef gives an eight-character code holding the event class and id back to back,
	/// which is what an Apple event is addressed by.
	/// </summary>
	internal sealed class AESdefCommand
	{
		internal string Name;
		internal string Suite;
		internal uint EventClass;
		internal uint EventId;
		internal string DirectTypeName;
		internal bool HasDirectParameter;
		internal readonly Dictionary<string, AESdefParameter> Parameters = new (StringComparer.Ordinal);

		internal bool TryGetParameter(string canonicalKey, out AESdefParameter parameter)
			=> Parameters.TryGetValue(canonicalKey, out parameter);
	}

	internal sealed class AESdefProperty
	{
		internal string Name;
		internal uint Code;
		internal string TypeName;
		internal bool Hidden;
		internal bool CanRead = true;
		internal bool CanWrite = true;
	}

	internal sealed class AESdefElement
	{
		internal string TypeName;
		internal bool Hidden;
	}

	internal sealed class AESdefClass
	{
		internal string Name;
		internal string PluralName;
		internal uint Code;
		internal string Inherits;
		internal readonly Dictionary<string, AESdefProperty> Properties = new (StringComparer.Ordinal);
		internal readonly Dictionary<string, AESdefElement> Elements = new (StringComparer.Ordinal);
		internal readonly HashSet<string> RespondsTo = new (StringComparer.Ordinal);
	}

	internal sealed class AESdefEnumeration
	{
		internal string Name;
		internal readonly Dictionary<string, uint> Enumerators = new (StringComparer.Ordinal);
	}

	/// <summary>
	/// A parsed scripting dictionary: the type library of the Apple Events world. Member lookup is by canonical
	/// key (see <see cref="AESdef.Key"/>) because sdef terms contain spaces that a script member name cannot.
	/// </summary>
	internal sealed class AESdefDictionary
	{
		internal readonly List<string> Suites = [];
		internal readonly Dictionary<string, AESdefClass> ClassesByName = new (StringComparer.Ordinal);
		internal readonly Dictionary<string, List<AESdefClass>> ClassesByKey = new (StringComparer.Ordinal);
		internal readonly Dictionary<string, List<AESdefClass>> ClassesByPluralKey = new (StringComparer.Ordinal);
		internal readonly Dictionary<uint, AESdefClass> ClassesByCode = [];
		internal readonly Dictionary<string, List<AESdefCommand>> CommandsByKey = new (StringComparer.Ordinal);
		internal readonly Dictionary<string, AESdefEnumeration> EnumerationsByName = new (StringComparer.Ordinal);

		/// <summary>Every property keyword the dictionary defines, for turning a record's keys back into names.</summary>
		internal readonly Dictionary<uint, string> PropertyNamesByCode = [];

		/// <summary>
		/// Property keywords by canonical name, across every class. A record written for a command ("with
		/// properties") names properties without saying which class they belong to, and a given property name
		/// almost always carries the same keyword wherever it appears, so the first definition wins.
		/// </summary>
		internal readonly Dictionary<string, uint> PropertyCodesByKey = new (StringComparer.Ordinal);

		/// <summary>Every enumerator code, for reporting an enumerated reply as the name a script would write.</summary>
		internal readonly Dictionary<uint, string> EnumeratorNamesByCode = [];

		/// <summary>
		/// Walks a class and everything it inherits from looking for a property. Returns the first hit, which is
		/// the most derived one because the walk starts at the class itself.
		/// </summary>
		internal AESdefProperty FindProperty(string className, string canonicalKey)
		{
			foreach (var cls in Ancestry(className))
				if (cls.Properties.TryGetValue(canonicalKey, out var property))
					return property;

			return null;
		}

		/// <summary>The element class named by a plural term (windows, documents), inheritance included.</summary>
		internal AESdefClass FindElement(string className, string canonicalPluralKey)
		{
			foreach (var cls in Ancestry(className))
				foreach (var element in cls.Elements.Values)
				{
					var target = ResolveClass(element.TypeName);

					if (target != null && string.Equals(Key(Plural(target)), canonicalPluralKey, StringComparison.Ordinal))
						return target;
				}

			return null;
		}

		internal bool RespondsTo(string className, string commandName)
		{
			foreach (var cls in Ancestry(className))
				if (cls.RespondsTo.Contains(commandName))
					return true;

			return false;
		}

		/// <summary>The class itself, then each class it inherits from. Guarded against a dictionary whose
		/// inheritance forms a cycle, which a hand-written sdef occasionally does.</summary>
		internal IEnumerable<AESdefClass> Ancestry(string className)
		{
			var seen = new HashSet<string>(StringComparer.Ordinal);
			var current = ResolveClass(className);

			while (current != null && seen.Add(current.Name))
			{
				yield return current;
				current = string.IsNullOrEmpty(current.Inherits) ? null : ResolveClass(current.Inherits);
			}
		}

		internal AESdefClass ResolveClass(string name)
		{
			if (string.IsNullOrEmpty(name))
				return null;

			if (ClassesByName.TryGetValue(name, out var exact))
				return exact;

			return ClassesByKey.TryGetValue(Key(name), out var byKey) && byKey.Count > 0 ? byKey[0] : null;
		}

		internal static string Plural(AESdefClass cls)
			=> string.IsNullOrEmpty(cls.PluralName) ? cls.Name + "s" : cls.PluralName;

		private static string Key(string term) => AESdef.Key(term);
	}

	/// <summary>
	/// Reads an application's scripting definition. Deliberately free of any platform guard and of any IO: the
	/// caller supplies the sdef text and an include resolver, so the parser is exercised by tests on every host
	/// even though the rest of the Apple Events layer only builds on macOS.
	/// </summary>
	internal static class AESdef
	{
		/// <summary>
		/// Folds an sdef term into the form a script writes it in. Terms contain spaces ("file name"), which a
		/// member name cannot, so the space and the underscore a script might substitute both drop out.
		/// </summary>
		internal static string Key(string term)
		{
			if (string.IsNullOrEmpty(term))
				return "";

			var sb = new StringBuilder(term.Length);

			foreach (var ch in term)
				if (ch != ' ' && ch != '_')
					_ = sb.Append(char.ToLowerInvariant(ch));

			return sb.ToString();
		}

		/// <summary>
		/// Parses sdef XML. <paramref name="resolveInclude"/> maps an xi:include href to that file's text; when it
		/// is null or returns null the include is skipped, which costs the standard suite but still yields a usable
		/// dictionary for the application's own terminology.
		/// </summary>
		internal static AESdefDictionary Parse(string xml, Func<string, string> resolveInclude = null)
		{
			var dict = new AESdefDictionary();
			ParseInto(dict, xml, resolveInclude, 0);
			return dict;
		}

		private static void ParseInto(AESdefDictionary dict, string xml, Func<string, string> resolveInclude, int depth)
		{
			if (string.IsNullOrWhiteSpace(xml) || depth > 4)
				return;

			XDocument doc;

			try
			{
				// An sdef may name a DTD it cannot reach; resolving one would turn a parse into a network fetch.
				var settings = new System.Xml.XmlReaderSettings
				{
					DtdProcessing = System.Xml.DtdProcessing.Ignore,
					XmlResolver = null
				};
				using var reader = System.Xml.XmlReader.Create(new StringReader(xml), settings);
				doc = XDocument.Load(reader);
			}
			catch (Exception ex)
			{
				throw new FormatException($"Malformed scripting definition: {ex.Message}", ex);
			}

			var root = doc.Root;

			if (root == null)
				return;

			// Suites may sit at the root or arrive through an include that carried a whole dictionary.
			foreach (var suite in Descend(root, "suite"))
				ParseSuite(dict, suite);

			foreach (var include in Descend(root, "include"))
			{
				var href = Attr(include, "href");

				if (!string.IsNullOrEmpty(href) && resolveInclude != null)
					ParseInto(dict, resolveInclude(href), resolveInclude, depth + 1);
			}
		}

		private static void ParseSuite(AESdefDictionary dict, XElement suiteEl)
		{
			var suiteName = Attr(suiteEl, "name") ?? "";

			if (!dict.Suites.Contains(suiteName))
				dict.Suites.Add(suiteName);

			foreach (var el in Kids(suiteEl, "enumeration"))
				ParseEnumeration(dict, el);

			foreach (var el in Kids(suiteEl, "command"))
				ParseCommand(dict, suiteName, el);

			// An event is a command the application sends to us; the terminology shape is identical.
			foreach (var el in Kids(suiteEl, "event"))
				ParseCommand(dict, suiteName, el);

			foreach (var el in Kids(suiteEl, "class"))
				ParseClass(dict, el, isExtension: false);

			// A class-extension adds members to a class another suite defined, so it must merge rather than replace.
			foreach (var el in Kids(suiteEl, "class-extension"))
				ParseClass(dict, el, isExtension: true);
		}

		private static void ParseEnumeration(AESdefDictionary dict, XElement el)
		{
			var name = Attr(el, "name");

			if (string.IsNullOrEmpty(name))
				return;

			var enumeration = new AESdefEnumeration { Name = name };

			foreach (var enumerator in Kids(el, "enumerator"))
			{
				var enumName = Attr(enumerator, "name");

				if (string.IsNullOrEmpty(enumName) || !TryCode(Attr(enumerator, "code"), out var packed))
					continue;
				enumeration.Enumerators[Key(enumName)] = packed;
				dict.EnumeratorNamesByCode[packed] = enumName;

				foreach (var synonym in Kids(enumerator, "synonym"))
				{
					var synName = Attr(synonym, "name");

					if (!string.IsNullOrEmpty(synName))
						enumeration.Enumerators[Key(synName)] = packed;
				}
			}

			dict.EnumerationsByName[name] = enumeration;
		}

		private static void ParseCommand(AESdefDictionary dict, string suiteName, XElement el)
		{
			var name = Attr(el, "name");
			var code = Attr(el, "code");

			// The code packs the event class and id back to back; without both there is no event to send.
			if (string.IsNullOrEmpty(name) || code == null || code.Length != 8
					|| !TryCode(code[..4], out var eventClass) || !TryCode(code[4..], out var eventId))
				return;

			var command = new AESdefCommand
			{
				Name = name,
				Suite = suiteName,
				EventClass = eventClass,
				EventId = eventId
			};

			foreach (var direct in Kids(el, "direct-parameter"))
			{
				command.HasDirectParameter = true;
				command.DirectTypeName = TypeOf(direct);
			}

			foreach (var p in Kids(el, "parameter"))
			{
				var pName = Attr(p, "name");

				if (string.IsNullOrEmpty(pName) || !TryCode(Attr(p, "code"), out var pCode))
					continue;

				var parameter = new AESdefParameter
				{
					Name = pName,
					Code = pCode,
					TypeName = TypeOf(p),
					Optional = IsYes(Attr(p, "optional")),
					Hidden = IsYes(Attr(p, "hidden"))
				};
				command.Parameters[Key(pName)] = parameter;

				foreach (var synonym in Kids(p, "synonym"))
				{
					var synName = Attr(synonym, "name");

					if (!string.IsNullOrEmpty(synName))
						command.Parameters[Key(synName)] = parameter;
				}
			}

			AddCommand(dict, Key(name), command);

			foreach (var synonym in Kids(el, "synonym"))
			{
				var synName = Attr(synonym, "name");

				if (!string.IsNullOrEmpty(synName))
					AddCommand(dict, Key(synName), command);
			}
		}

		/// <summary>
		/// Records a command under one name. A dictionary that defines the same term twice with the same event is
		/// describing one command, so the definitions are merged rather than the later one dropped: an application
		/// commonly restates a standard command to add its own parameters. Only a genuinely different event
		/// becomes an ambiguity the caller must resolve.
		/// </summary>
		private static void AddCommand(AESdefDictionary dict, string key, AESdefCommand command)
		{
			if (!dict.CommandsByKey.TryGetValue(key, out var list))
				dict.CommandsByKey[key] = list = [];

			foreach (var existing in list)
				if (existing.EventClass == command.EventClass && existing.EventId == command.EventId)
				{
					foreach (var parameter in command.Parameters)
						existing.Parameters.TryAdd(parameter.Key, parameter.Value);

					existing.DirectTypeName ??= command.DirectTypeName;
					existing.HasDirectParameter |= command.HasDirectParameter;
					return;
				}

			list.Add(command);
		}

		private static void ParseClass(AESdefDictionary dict, XElement el, bool isExtension)
		{
			var cls = ParseClassBody(dict, el, isExtension, out var name);

			if (cls == null || string.IsNullOrEmpty(name))
				return;

			dict.ClassesByName[name] = cls;
			Index(dict.ClassesByKey, Key(name), cls);
			Index(dict.ClassesByPluralKey, Key(AESdefDictionary.Plural(cls)), cls);

			if (cls.Code != 0)
				dict.ClassesByCode[cls.Code] = cls;

			foreach (var synonym in Kids(el, "synonym"))
			{
				var synName = Attr(synonym, "name");

				if (!string.IsNullOrEmpty(synName))
					Index(dict.ClassesByKey, Key(synName), cls);
			}
		}

		private static AESdefClass ParseClassBody(AESdefDictionary dict, XElement el, bool isExtension, out string name)
		{
			// A class-extension names its target with "extends"; it must land on the existing class, not beside it.
			name = isExtension ? Attr(el, "extends") : Attr(el, "name");

			if (string.IsNullOrEmpty(name))
				return null;

			if (!dict.ClassesByName.TryGetValue(name, out var cls))
			{
				cls = new AESdefClass { Name = name };

				if (isExtension)
					dict.ClassesByName[name] = cls;
			}

			if (!isExtension)
			{
				cls.PluralName = Attr(el, "plural") ?? cls.PluralName;
				cls.Inherits = Attr(el, "inherits") ?? cls.Inherits;

				if (TryCode(Attr(el, "code"), out var classCode))
					cls.Code = classCode;
			}

			foreach (var p in Kids(el, "property"))
			{
				var pName = Attr(p, "name");

				if (string.IsNullOrEmpty(pName) || !TryCode(Attr(p, "code"), out var pCode))
					continue;

				// The sdef default for access is read and write; "r" or "w" narrows it.
				var access = Attr(p, "access") ?? "rw";
				var property = new AESdefProperty
				{
					Name = pName,
					Code = pCode,
					TypeName = TypeOf(p),
					Hidden = IsYes(Attr(p, "hidden")),
					CanRead = access.Contains('r', StringComparison.Ordinal),
					CanWrite = access.Contains('w', StringComparison.Ordinal)
				};
				cls.Properties[Key(pName)] = property;
				dict.PropertyNamesByCode[property.Code] = pName;
				_ = dict.PropertyCodesByKey.TryAdd(Key(pName), property.Code);

				foreach (var synonym in Kids(p, "synonym"))
				{
					var synName = Attr(synonym, "name");

					if (!string.IsNullOrEmpty(synName))
						cls.Properties[Key(synName)] = property;
				}
			}

			foreach (var e in Kids(el, "element"))
			{
				var type = Attr(e, "type");

				if (!string.IsNullOrEmpty(type))
					cls.Elements[Key(type)] = new AESdefElement { TypeName = type, Hidden = IsYes(Attr(e, "hidden")) };
			}

			foreach (var r in Kids(el, "responds-to"))
			{
				// Older dictionaries name the command with "name", newer ones with "command".
				var command = Attr(r, "command") ?? Attr(r, "name");

				if (!string.IsNullOrEmpty(command))
					_ = cls.RespondsTo.Add(command);
			}

			return cls;
		}

		private static void Index(Dictionary<string, List<AESdefClass>> map, string key, AESdefClass cls)
		{
			if (!map.TryGetValue(key, out var list))
				map[key] = list = [];

			if (!list.Contains(cls))
				list.Add(cls);
		}

		/// <summary>The type of a parameter or property, which the sdef may give as an attribute or as a child
		/// element.</summary>
		private static string TypeOf(XElement el)
		{
			var direct = Attr(el, "type");

			if (!string.IsNullOrEmpty(direct))
				return direct;

			foreach (var typeEl in Kids(el, "type"))
			{
				var name = Attr(typeEl, "type");

				if (!string.IsNullOrEmpty(name))
					return name;
			}

			return null;
		}

		/// <summary>
		/// A four-character code from the document. A term whose code is missing or malformed is skipped rather
		/// than thrown on: one bad entry in a dictionary of hundreds must not cost the whole application its
		/// terminology.
		/// </summary>
		private static bool TryCode(string code, out uint packed)
			=> AEFourCharCode.TryPack((code ?? "").AsSpan(), out packed);

		private static bool IsYes(string value) => string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);

		// Matching on local names keeps parsing working whether or not the document declares a default namespace.
		private static IEnumerable<XElement> Kids(XElement el, string name)
			=> el.Elements().Where(e => string.Equals(e.Name.LocalName, name, StringComparison.Ordinal));

		private static IEnumerable<XElement> Descend(XElement el, string name)
			=> el.Descendants().Where(e => string.Equals(e.Name.LocalName, name, StringComparison.Ordinal));

		private static string Attr(XElement el, string name)
			=> el.Attributes().FirstOrDefault(a => string.Equals(a.Name.LocalName, name, StringComparison.Ordinal))?.Value;
	}
}
