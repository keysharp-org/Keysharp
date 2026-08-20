namespace Keysharp.Builtins
{
	public partial class Ks
	{
		/// <summary>
		/// Converts between JSON text and script values. Scripts reach it through the KS module:
		/// <c>#Import "Ks" { Json }</c>, then <c>Json.Encode(value)</c> and <c>Json.Decode(text)</c>.
		/// </summary>
		public class Json : KeysharpObject
		{
			/// <summary>
			/// The nesting limit, applied to both directions so that whatever encodes also decodes. When
			/// encoding, a container which (indirectly) contains itself would otherwise recurse until the stack
			/// overflows; cycles are detected by reference so that two distinct but equal containers remain legal.
			/// </summary>
			private const int MaxDepth = 128;

			/// <summary>
			/// The widest indent <see cref="Encode"/> accepts, which is the limit
			/// <see cref="JsonWriterOptions.IndentSize"/> itself enforces.
			/// </summary>
			private const int MaxIndent = 127;

			/// <summary>
			/// Returns the JSON text for a script value.
			/// </summary>
			/// <param name="this">The class object, supplied by the script-static call.</param>
			/// <param name="value">
			/// The value to encode. A <see cref="Map"/> or an object's own value properties become a JSON
			/// object, an <see cref="Array"/> becomes a JSON array, a string becomes a string, a number becomes
			/// a number, a boolean (<c>true</c>, or any comparison result) becomes true or false, and
			/// <c>Null</c> or an unset value becomes null.
			/// </param>
			/// <param name="indent">
			/// The indent applied per nesting level, following the convention of JavaScript's
			/// <c>JSON.stringify</c> and Python's <c>json.dumps</c>: omitted, <c>""</c> or 0 writes the compact
			/// single-line form (the default); a number writes that many spaces; a string of spaces or of tabs
			/// is used as the indent unit itself. Lines are separated by a line feed on every platform, so the
			/// same value always produces the same bytes.
			/// </param>
			/// <returns>JSON text.</returns>
			/// <exception cref="ValueError">Thrown if value contains a reference cycle or nests too deeply, or
			/// if indent is neither a width nor a run of spaces or tabs.</exception>
			[Static]
			public static object Encode(object @this, object value, object indent = null, object nullValue = null)
			{
				if (!ParseIndent(indent, out var indentChar, out var indentSize, out var error))
					return Errors.ValueErrorOccurred(error);

				using var stream = new MemoryStream();

				// The default encoder is HTML-safe, which would escape quotes and every non-ASCII character
				// as \uXXXX. Relaxed escaping emits the JSON a script author expects: \" for a quote and
				// literal text for anything outside ASCII.
				var options = new JsonWriterOptions
				{
					Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
					Indented = indentSize > 0,
					// Not Environment.NewLine: the same value must produce the same bytes on every platform, or
					// a hash taken over encoded JSON -- a lock file, a cache key -- would be host-dependent.
					NewLine = "\n"
				};

				if (indentSize > 0)
				{
					options.IndentCharacter = indentChar;
					options.IndentSize = indentSize;
				}

				using (var writer = new Utf8JsonWriter(stream, options))
					WriteValue(writer, value, new HashSet<object>(ReferenceEqualityComparer.Instance), 0, nullValue);

				return Encoding.UTF8.GetString(stream.ToArray());
			}

			/// <summary>
			/// Returns the script value for JSON text.
			/// </summary>
			/// <param name="this">The class object, supplied by the script-static call.</param>
			/// <param name="jsonText">The JSON text to decode.</param>
			/// <param name="caseSense">
			/// The case sensitivity every decoded <see cref="Map"/> is given, spelled as for
			/// <see cref="Map.CaseSense"/>: true (the default, matching <c>Map()</c>), false, or "Locale". It
			/// has to be chosen here because <c>Map.CaseSense</c> cannot be assigned once a map holds entries.
			/// </param>
			/// <param name="nullValue">
			/// The value a JSON <c>null</c> becomes. Omitted -- the default -- it becomes UNSET, which for a
			/// Map key means the key is simply absent and for an Array element means a hole, the same thing
			/// <c>unset</c> means everywhere else in the language.
			/// <para>There is no built-in stand-in for null, so a script which has to tell a null apart from an
			/// absent key supplies its own marker -- an object, so it cannot collide with data -- and hands the
			/// same marker to <see cref="Encode"/> to write it back out.</para>
			/// </param>
			/// <returns>
			/// A <see cref="Map"/> for a JSON object, an <see cref="Array"/> for a JSON array, a string, a
			/// number, a boolean for true or false, and <paramref name="nullValue"/> for null -- unset when
			/// none was given.
			/// </returns>
			/// <exception cref="ValueError">Thrown if jsonText is not well-formed JSON, or if caseSense is not
			/// one of the values listed above.</exception>
			[Static]
			public static object Decode(object @this, object jsonText, object caseSense = null, object nullValue = null)
			{
				//Omitted means case-sensitive, matching Map(); anything unrecognized is a typo, not a default.
				var cs = eCaseSense.On;

				if (caseSense != null)
				{
					if (Conversions.ParseCaseSense(caseSense) is eCaseSense parsed)
						cs = parsed;
					else
						return Errors.ValueErrorOccurred($"caseSense must be true, false or \"Locale\", not \"{caseSense}\".");
				}

				try
				{
					// Trailing commas and comments are accepted because hand-written configuration files
					// commonly carry them; everything else follows the JSON grammar.
					using var doc = JsonDocument.Parse(jsonText.As(), new JsonDocumentOptions
					{
						AllowTrailingCommas = true,
						CommentHandling = JsonCommentHandling.Skip,
						MaxDepth = MaxDepth
					});
					return ReadValue(doc.RootElement, cs, nullValue);
				}
				catch (JsonException ex)
				{
					return Errors.ValueErrorOccurred(ex.Message);
				}
			}

			/// <summary>
			/// Resolves the <c>indent</c> parameter of <see cref="Encode"/> to the writer's indent character
			/// and width, where a width of 0 means the compact form.
			/// </summary>
			/// <param name="indent">The parameter as the script passed it.</param>
			/// <param name="indentChar">The character one indent level is made of.</param>
			/// <param name="indentSize">How many of them one level is, or 0 for compact output.</param>
			/// <param name="error">The message to report when the parameter is not a valid indent.</param>
			/// <returns>True if indent was understood, else false.</returns>
			private static bool ParseIndent(object indent, out char indentChar, out int indentSize, out string error)
			{
				indentChar = ' ';
				indentSize = 0;
				error = null;

				if (indent == null)
					return true;

				var str = indent as string;

				// A run of spaces or of tabs is the indent unit itself. This is tested before the numeric
				// reading so that " " stays one space rather than being coerced through a number.
				if (str != null && str.Length != 0 && (str[0] == ' ' || str[0] == '\t'))
				{
					indentChar = str[0];

					foreach (var c in str)
					{
						if (c != indentChar)
						{
							error = "The indent must be made of spaces or of tabs, not a mix of them.";
							return false;
						}
					}

					indentSize = str.Length;
				}
				else if (str != null && str.Length == 0)
				{
					return true;//"" is the documented spelling of the compact form.
				}
				else if (indent.TryCoerceLong(out var width))
				{
					// Coerce rather than parse, so a width arrived at by arithmetic is accepted the way every
					// other integer parameter accepts one. A negative count is not an error anywhere else that
					// takes one, and 0 is the documented way to ask for compact output, so both mean compact.
					indentSize = width > 0 ? (int)System.Math.Min(width, int.MaxValue) : 0;
				}
				else
				{
					error = str != null
							? $"The indent must be a width or a run of spaces or tabs, not \"{str}\"."
							: $"The indent must be a width or a run of spaces or tabs, not a {Types.Type(indent)}.";
					return false;
				}

				if (indentSize > MaxIndent)
				{
					error = $"The indent cannot be wider than {MaxIndent}.";
					indentSize = 0;
					return false;
				}

				return true;
			}

			/// <summary>
			/// Whether a value is the caller's stand-in for null. An object marker is matched by identity, which
			/// is exact and cannot be produced accidentally by data; anything else is matched by value, because a
			/// caller who deliberately nominates a string or a number means every occurrence of it.
			/// </summary>
			/// <param name="value">The value being written.</param>
			/// <param name="marker">The marker, or null when the caller supplied none.</param>
			/// <returns>True if value stands for null.</returns>
			private static bool IsNullMarker(object value, object marker)
			{
				if (marker == null)
					return false;

				if (marker is Any)
					return ReferenceEquals(value, marker);

				return value != null && Script.IdentityEquality(value, marker) is bool b && b;
			}			/// <summary>
			/// Writes one script value to the JSON writer, recursing into containers.
			/// </summary>
			/// <param name="writer">The writer to append to.</param>
			/// <param name="value">The value to write.</param>
			/// <param name="open">The containers currently being written, used to detect a cycle.</param>
			/// <param name="depth">The current nesting depth.</param>
			/// <param name="nullMarker">The caller's stand-in for null, or null when they supplied none.</param>
			private static void WriteValue(Utf8JsonWriter writer, object value, HashSet<object> open, int depth, object nullMarker)
			{
				// Ahead of every other case: the marker is usually an object, which would otherwise be written
				// out as its own properties.
				if (IsNullMarker(value, nullMarker))
				{
					writer.WriteNullValue();
					return;
				}

				switch (value)
				{
					case null: writer.WriteNullValue(); return;

					case string s: writer.WriteStringValue(s); return;

					case bool b: writer.WriteBooleanValue(b); return;

					case long l: writer.WriteNumberValue(l); return;

					case int i: writer.WriteNumberValue(i); return;

					case double d: writer.WriteNumberValue(d); return;

					case decimal m: writer.WriteNumberValue(m); return;
				}

				if (depth >= MaxDepth)
				{
					_ = Errors.ValueErrorOccurred($"JSON nesting exceeds the limit of {MaxDepth}.");
					return;
				}

				if (!open.Add(value))
				{
					_ = Errors.ValueErrorOccurred("A value cannot be encoded because it contains itself.");
					return;
				}

				try
				{
					switch (value)
					{
						case Map map:
							writer.WriteStartObject();

							foreach (var (key, val) in (IEnumerable<(object, object)>)map)
							{
								writer.WritePropertyName(key?.ToString() ?? "");
								WriteValue(writer, val, open, depth + 1, nullMarker);
							}

							writer.WriteEndObject();
							break;

						case Array arr:
							writer.WriteStartArray();

							foreach (var item in (IEnumerable<object>)arr)
								WriteValue(writer, item, open, depth + 1, nullMarker);

							writer.WriteEndArray();
							break;

						case KeysharpObject kso:
							writer.WriteStartObject();

							// Only own value properties: a dynamic property would have to be invoked to produce
							// a value, which encoding a value must not do.
							if (kso.op != null)
							{
								foreach (var (name, desc) in kso.op)
								{
									if (desc.Value == null)
										continue;

									writer.WritePropertyName(name);
									WriteValue(writer, desc.Value, open, depth + 1, nullMarker);
								}
							}

							writer.WriteEndObject();
							break;

						default:
							writer.WriteStringValue(value.ToString());
							break;
					}
				}
				finally { _ = open.Remove(value); }
			}

			/// <summary>
			/// Converts one parsed JSON element to its script value, recursing into containers.
			/// </summary>
			/// <param name="element">The element to convert.</param>
			/// <param name="caseSense">The case sensitivity every decoded <see cref="Map"/> is built with.</param>
			/// <param name="nullValue">The value a JSON null becomes, or null for unset.</param>
			/// <returns>The script value.</returns>
			private static object ReadValue(JsonElement element, eCaseSense caseSense, object nullValue)
			{
				switch (element.ValueKind)
				{
					case JsonValueKind.Object:
						var map = new Map(caseSense);

						foreach (var prop in element.EnumerateObject())
						{
							// An unset marker means the key simply is not there. Assigning it would not store a
							// value-less entry anyway -- a Map has no such thing, and assigning unset to a key that
							// does not exist raises.
							if (ReadValue(prop.Value, caseSense, nullValue) is object v)
								map[prop.Name] = v;
						}

						return map;

					case JsonValueKind.Array:
						var arr = new Array();

						foreach (var item in element.EnumerateArray())
							_ = arr.Push(ReadValue(item, caseSense, nullValue));

						return arr;

					case JsonValueKind.String:
						return element.GetString();

					// An integral value stays an Integer so that it round-trips and indexes; anything else,
					// including a value too large for Int64, becomes a Float.
					// The (object) cast matters: without it the conditional's type unifies to double and
					// every integral value would decode as a Float.
					case JsonValueKind.Number:
						return element.TryGetInt64(out var l) ? l : (object)element.GetDouble();

					// A boolean is script-visibly the Integer 1 or 0 -- Type() says "Integer", it compares equal
					// to 1 or 0, it prints as "1" or "0" -- so nothing about reading the value changes, but the
					// writer can tell it apart from a number and true/false survive a round trip.
					case JsonValueKind.True:
						return true;

					case JsonValueKind.False:
						return false;

					default:
						return nullValue;
				}
			}
		}
	}
}
