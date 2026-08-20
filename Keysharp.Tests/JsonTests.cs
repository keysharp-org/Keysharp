using Array = Keysharp.Builtins.Array;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Keysharp.Tests
{
	/// <summary>
	/// The Ks.Json class. The members are static, so every call passes a null receiver the way the script-static
	/// dispatch does; <see cref="ScriptSurface"/> is the one test that goes through real dynamic dispatch.
	/// </summary>
	public class JsonTests : TestRunner
	{
		private static string Enc(object value, object indent = null, object nullValue = null) => (string)Json.Encode(null, value, indent, nullValue);

		private static object Dec(object text, object caseSense = null, object nullValue = null)
			=> Json.Decode(null, text, caseSense, nullValue);

		private static Error ScriptError(TestDelegate action) => Assert.Throws<KeysharpException>(action).UserError;

		/// <summary>A marker a script would supply for null: an object, so it cannot collide with data.</summary>
		private static readonly KeysharpObject NullMarker = new ();

		#region Encode

		[Test, Category("Json")]
		public void EncodeScalars()
		{
			Assert.AreEqual("\"hi\"", Enc("hi"));
			Assert.AreEqual("\"\"", Enc(""));
			Assert.AreEqual("42", Enc(42L));
			Assert.AreEqual("-1.5", Enc(-1.5));
			Assert.AreEqual("null", Enc(null));
			// Relaxed escaping: a quote is escaped, non-ASCII text is not.
			Assert.AreEqual("\"a\\\"b\"", Enc("a\"b"));
			Assert.AreEqual("\"äöü\"", Enc("äöü"));
		}

		[Test, Category("Json")]
		public void EncodeContainers()
		{
			Assert.AreEqual("[1,2,3]", Enc(new Array([1L, 2L, 3L])));
			Assert.AreEqual("[]", Enc(new Array()));
			Assert.AreEqual("{\"a\":1,\"b\":\"two\"}", Enc(new Map("a", 1L, "b", "two")));
			Assert.AreEqual("{}", Enc(new Map()));
			Assert.AreEqual("{\"a\":[1,{\"b\":2}]}", Enc(new Map("a", new Array([1L, new Map("b", 2L)]))));
		}

		/// <summary>
		/// A Map enumerates in sorted key order (MapComparer, which is how AutoHotkey enumerates), so encoding
		/// one sorts its keys. Nothing about JSON requires that, but it does mean the same map always encodes
		/// to the same text regardless of insertion order.
		/// </summary>
		[Test, Category("Json")]
		public void EncodeSortsMapKeys()
		{
			Assert.AreEqual("{\"a\":1,\"b\":2,\"c\":3}", Enc(new Map("c", 3L, "a", 1L, "b", 2L)));
			Assert.AreEqual(Enc(new Map("a", 1L, "b", 2L)), Enc(new Map("b", 2L, "a", 1L)));
		}

		[Test, Category("Json")]
		public void EncodeObjectOwnProps()
		{
			var obj = new KeysharpObject();
			_ = Objects.DefineProp(obj, "name", new Map("Value", "x"));
			_ = Objects.DefineProp(obj, "n", new Map("Value", 3L));
			Assert.AreEqual("{\"name\":\"x\",\"n\":3}", Enc(obj));
		}

		[Test, Category("Json")]
		public void EncodeNullAndBooleans()
		{
			Assert.AreEqual("null", Enc(NullMarker, null, NullMarker));
			Assert.AreEqual("true", Enc(true));
			Assert.AreEqual("false", Enc(false));
			Assert.AreEqual("{\"a\":null,\"b\":true,\"c\":false}",
							Enc(new Map("a", NullMarker, "b", true, "c", false), null, NullMarker));
			// A boolean is what distinguishes true/false from the numbers 1 and 0.
			Assert.AreEqual("[1,0]", Enc(new Array([1L, 0L])));
		}

		[Test, Category("Json")]
		public void EncodeIndentWidth()
		{
			var value = new Map("a", 1L, "b", new Array([2L]));
			Assert.AreEqual("{\n  \"a\": 1,\n  \"b\": [\n    2\n  ]\n}", Enc(value, 2L));
			Assert.AreEqual("{\n    \"a\": 1,\n    \"b\": [\n        2\n    ]\n}", Enc(value, 4L));
			// A numeric string is a width too, as everywhere else a script passes a number.
			Assert.AreEqual(Enc(value, 2L), Enc(value, "2"));
			// A width arrived at by arithmetic is a Float, and is accepted as every other integer
			// parameter accepts one.
			Assert.AreEqual(Enc(value, 2L), Enc(value, 4.0 / 2));
		}

		[Test, Category("Json")]
		public void EncodeIndentUnitString()
		{
			var value = new Map("a", new Map("b", 1L));
			Assert.AreEqual("{\n\t\"a\": {\n\t\t\"b\": 1\n\t}\n}", Enc(value, "\t"));
			Assert.AreEqual("{\n \"a\": {\n  \"b\": 1\n }\n}", Enc(value, " "));
			Assert.AreEqual(Enc(value, 3L), Enc(value, "   "));
		}

		/// <summary>Every spelling of "no indent" produces the compact form, which is also the default.</summary>
		[Test, Category("Json")]
		public void EncodeIndentCompactForms()
		{
			var value = new Map("a", 1L);
			const string compact = "{\"a\":1}";
			Assert.AreEqual(compact, Enc(value));
			Assert.AreEqual(compact, Enc(value, ""));
			Assert.AreEqual(compact, Enc(value, 0L));
			Assert.AreEqual(compact, Enc(value, "0"));
			Assert.AreEqual(compact, Enc(value, -1L));
		}

		/// <summary>
		/// Line feeds, never CRLF: the same value has to produce the same bytes on every platform, or a hash
		/// taken over encoded JSON would be host-dependent.
		/// </summary>
		[Test, Category("Json")]
		public void EncodeIndentUsesLineFeeds()
		{
			var text = Enc(new Map("a", 1L), 2L);
			Assert.IsTrue(text.Contains('\n'));
			Assert.IsFalse(text.Contains('\r'));
		}

		[Test, Category("Json")]
		public void EncodeIndentInvalid()
		{
			var value = new Map("a", 1L);
			Assert.IsInstanceOf<ValueError>(ScriptError(() => Enc(value, " \t")));
			Assert.IsInstanceOf<ValueError>(ScriptError(() => Enc(value, "xx")));
			Assert.IsInstanceOf<ValueError>(ScriptError(() => Enc(value, 200L)));
			Assert.IsInstanceOf<ValueError>(ScriptError(() => Enc(value, new Map())));
		}

		[Test, Category("Json")]
		public void EncodeRejectsCycle()
		{
			var map = new Map();
			map["self"] = map;
			Assert.IsInstanceOf<ValueError>(ScriptError(() => Enc(map)));

			// Two distinct but equal containers are not a cycle.
			var shared = new Map("a", 1L);
			Assert.AreEqual("{\"x\":{\"a\":1},\"y\":{\"a\":1}}", Enc(new Map("x", shared, "y", shared)));
		}

		/// <summary>
		/// The limit is the same in both directions, so anything Encode accepts, Decode accepts back. It used
		/// to be possible to write JSON this class could not read.
		/// </summary>
		[Test, Category("Json")]
		public void NestingLimitIsSymmetric()
		{
			static string Nest(int levels) => new string('[', levels) + new string(']', levels);

			Assert.AreEqual(Nest(127), Enc(Dec(Nest(127))));
			Assert.IsInstanceOf<ValueError>(ScriptError(() => Dec(Nest(200))));
		}

		[Test, Category("Json")]
		public void EncodeRejectsExcessiveNesting()
		{
			var root = new Array();
			var leaf = root;

			for (var i = 0; i < 200; i++)
			{
				var next = new Array();
				_ = leaf.Push(next);
				leaf = next;
			}

			Assert.IsInstanceOf<ValueError>(ScriptError(() => Enc(root)));
		}

		#endregion

		#region Decode

		[Test, Category("Json")]
		public void DecodeScalars()
		{
			Assert.AreEqual("hi", Dec("\"hi\""));
			Assert.AreEqual(42L, Dec("42"));
			Assert.AreEqual(-1.5, Dec("-1.5"));
			// Integral stays an Integer so it indexes and round-trips; anything else becomes a Float.
			Assert.IsInstanceOf<long>(Dec("42"));
			Assert.IsInstanceOf<double>(Dec("42.0"));
			Assert.IsInstanceOf<double>(Dec("99999999999999999999"));
		}

		/// <summary>
		/// true/false decode to real booleans. That is invisible to a script -- Type() reports "Integer", they
		/// compare equal to 1 and 0 and print as "1" and "0" -- but it is what lets Encode write them back out
		/// as true/false instead of 1/0.
		/// </summary>
		[Test, Category("Json")]
		public void DecodeBooleans()
		{
			Assert.AreEqual(true, Dec("true"));
			Assert.AreEqual(false, Dec("false"));
			Assert.IsInstanceOf<bool>(Dec("true"));
			Assert.AreEqual("Integer", Types.Type(Dec("true")));
			Assert.IsTrue((bool)Script.IdentityEquality(Dec("true"), 1L));
			Assert.IsTrue((bool)Script.IdentityEquality(Dec("false"), 0L));
			Assert.AreEqual("1", Script.ForceString(Dec("true")));
			Assert.IsTrue(Script.ForceBool(Dec("true")));
			Assert.IsFalse(Script.ForceBool(Dec("false")));
		}

		[Test, Category("Json")]
		public void DecodeContainers()
		{
			var arr = (Array)Dec("[1,\"two\",[3]]");
			Assert.AreEqual(3L, arr.Length);
			Assert.AreEqual(1L, arr[1]);
			Assert.AreEqual("two", arr[2]);
			Assert.AreEqual(3L, ((Array)arr[3])[1]);

			var map = (Map)Dec("{\"a\":1,\"b\":{\"c\":2}}");
			Assert.AreEqual(2L, map.Count);
			Assert.AreEqual(1L, map["a"]);
			Assert.AreEqual(2L, ((Map)map["b"])["c"]);
		}

		[Test, Category("Json")]
		public void DecodeIsCaseSensitiveByDefault()
		{
			var map = (Map)Dec("{\"Key\":1,\"key\":2}");
			Assert.AreEqual("On", map.CaseSense);
			Assert.AreEqual(2L, map.Count);
			Assert.AreEqual(1L, map["Key"]);
			Assert.AreEqual(2L, map["key"]);
		}

		[Test, Category("Json")]
		public void DecodeCaseInsensitive()
		{
			var map = (Map)Dec("{\"Key\":1,\"Nested\":{\"Inner\":2}}", false);
			Assert.AreEqual("Off", map.CaseSense);
			Assert.AreEqual(1L, map["KEY"]);
			Assert.AreEqual(1L, map["key"]);
			Assert.IsTrue(map.Has("kEy"));
			// The mode reaches every map, not only the root.
			var nested = (Map)map["nested"];
			Assert.AreEqual("Off", nested.CaseSense);
			Assert.AreEqual(2L, nested["INNER"]);

			// A duplicate that differs only in case collapses, as it does in any case-insensitive Map.
			Assert.AreEqual(1L, ((Map)Dec("{\"a\":2,\"A\":1}", false)).Count);

			// The other spellings Map.CaseSense accepts work here too.
			Assert.AreEqual("Off", ((Map)Dec("{\"a\":1}", "Off")).CaseSense);
			Assert.AreEqual("On", ((Map)Dec("{\"a\":1}", true)).CaseSense);
			Assert.AreEqual("Locale", ((Map)Dec("{\"a\":1}", "Locale")).CaseSense);
		}

		/// <summary>
		/// With no marker a JSON null decodes to UNSET, which is what unset means everywhere else: a Map key
		/// is simply absent, and an Array element is a hole that keeps the array's Length.
		/// </summary>
		[Test, Category("Json")]
		public void DecodeNullIsUnsetByDefault()
		{
			Assert.IsNull(Dec("null"));

			var map = (Map)Dec("{\"a\":null,\"b\":\"\"}");
			Assert.IsFalse(map.Has("a"));
			Assert.AreEqual(1L, map.Count);
			// An empty string is still a value, so the two are no longer indistinguishable.
			Assert.IsTrue(map.Has("b"));
			Assert.AreEqual("", map["b"]);

			// An array keeps its shape: the null is a hole, not a removed element.
			var arr = (Array)Dec("[1,null,3]");
			Assert.AreEqual(3L, arr.Length);
			Assert.IsFalse(arr.Has(2) != 0);
			Assert.AreEqual(1L, arr[1]);
			Assert.AreEqual(3L, arr[3]);
		}		/// <summary>
		/// There is no built-in null sentinel: a script that has to tell a JSON null from an empty string
		/// supplies its own marker and hands the same one back to Encode.
		/// </summary>
		[Test, Category("Json")]
		public void DecodeNullValueMarker()
		{
			var decoded = Dec("null", null, NullMarker);
			Assert.AreSame(NullMarker, decoded);
			Assert.IsFalse((bool)Script.IdentityEquality(decoded, ""));

			var map = (Map)Dec("{\"a\":null,\"b\":\"\"}", null, NullMarker);
			Assert.AreSame(NullMarker, map["a"]);
			Assert.AreEqual("", map["b"]);
			Assert.AreSame(NullMarker, ((Array)Dec("[null]", null, NullMarker))[1]);

			// Only null is affected; everything else decodes as it always does.
			Assert.AreEqual(1L, ((Map)Dec("{\"a\":1}", null, NullMarker))["a"]);
		}

		/// <summary>
		/// Encode only writes null for a marker the caller nominated, so an ordinary value can never turn
		/// into a null by accident. An object marker matches by identity; anything else by value, because a
		/// caller who nominates a string means every occurrence of it.
		/// </summary>
		[Test, Category("Json")]
		public void EncodeNullValueMarker()
		{
			// No marker supplied: the object is just an object.
			Assert.AreEqual("{}", Enc(NullMarker));
			Assert.AreEqual("{\"a\":{}}", Enc(new Map("a", NullMarker)));

			// A different object is not the marker.
			Assert.AreEqual("{\"a\":{}}", Enc(new Map("a", new KeysharpObject()), null, NullMarker));

			// A non-object marker matches by value, which is the caller asking for exactly that.
			Assert.AreEqual("{\"a\":null,\"b\":\"keep\"}",
							Enc(new Map("a", "<null>", "b", "keep"), null, "<null>"));

			// An empty string is not special unless nominated.
			Assert.AreEqual("{\"a\":\"\"}", Enc(new Map("a", "")));
			Assert.AreEqual("{\"a\":null}", Enc(new Map("a", ""), null, ""));
		}		[Test, Category("Json")]
		public void DecodeAcceptsTrailingCommasAndComments()
		{
			Assert.AreEqual(1L, ((Map)Dec("{\"a\":1,}"))["a"]);
			Assert.AreEqual(1L, ((Array)Dec("[1,]"))[1]);
			Assert.AreEqual(1L, ((Map)Dec("{\n// leading\n\"a\":1 /* trailing */\n}"))["a"]);
		}

		[Test, Category("Json")]
		public void DecodeRejectsMalformed()
		{
			Assert.IsInstanceOf<ValueError>(ScriptError(() => Dec("{")));
			Assert.IsInstanceOf<ValueError>(ScriptError(() => Dec("")));
			Assert.IsInstanceOf<ValueError>(ScriptError(() => Dec("{\"a\":}")));
			Assert.IsInstanceOf<ValueError>(ScriptError(() => Dec("nope")));
		}

		[Test, Category("Json")]
		public void DecodeRejectsBadOptions()
		{
			Assert.IsInstanceOf<ValueError>(ScriptError(() => Dec("{}", "sensitive")));

			// nullValue is deliberately unvalidated: any value may stand for null, including a plain string.
			Assert.AreEqual("maybe", ((Map)Dec("{\"a\":null}", null, "maybe"))["a"]);
		}

		#endregion

		#region Booleans

		/// <summary>
		/// A boolean is written as JSON true/false where the Integer 1 or 0 is written as a number. That is
		/// not only about the true/false keywords: the language produces booleans on its own, so a comparison,
		/// a negation and Map.Has all encode as JSON booleans too.
		/// </summary>
		[Test, Category("Json")]
		public void EncodeWritesBooleansForBooleanValuedExpressions()
		{
			Assert.AreEqual("true", Enc(Script.LogicalNot(Script.LogicalNot(1L))));
			Assert.AreEqual("true", Enc(Script.GreaterThan(1L, 0L)));
			Assert.AreEqual("false", Enc(Script.IdentityEquality(1L, 2L)));
			Assert.AreEqual("true", Enc(new Map("a", 1L).Has("a")));
			Assert.AreEqual("{\"ok\":true}", Enc(new Map("ok", Script.GreaterThan(1L, 0L))));

			// The Integers stay numbers, which is the distinction being drawn.
			Assert.AreEqual("[1,0]", Enc(new Array([1L, 0L])));
		}

		#endregion

		#region Round trip

		[Test, Category("Json")]
		public void RoundTripPreservesTypes()
		{
			const string text = "{\"f\":false,\"n\":1.5,\"s\":\"x\",\"t\":true,\"z\":null}";
			// null survives only with a marker on both sides; true/false always do.
			Assert.AreEqual(text, Enc(Dec(text, null, NullMarker), null, NullMarker));
			// Without a marker the null key is simply absent, so it is not there to encode.
			Assert.AreEqual("{\"f\":false,\"n\":1.5,\"s\":\"x\",\"t\":true}", Enc(Dec(text)));
		}

		[Test, Category("Json")]
		public void RoundTripSurvivesIndent()
		{
			var value = new Map("a", new Array([1L, true, "x"]), "b", NullMarker);
			var pretty = Enc(value, 2L, NullMarker);
			Assert.AreEqual(Enc(value, null, NullMarker), Enc(Dec(pretty, null, NullMarker), null, NullMarker));
		}

		#endregion

		[Test, Category("Json")]
		public void ScriptSurface() => Assert.IsTrue(TestScript("json-class", true));
	}
}
