using Keysharp.Internals.AppleEvents;

namespace Keysharp.Tests
{
	/// <summary>
	/// Exercises the parts of the Apple Events layer that do not need macOS: four-character codes and the
	/// scripting-definition parser, which between them decide every name and keyword the rest of the layer sends.
	/// The parser is deliberately free of a platform guard so these run on every host; the sending, marshalling
	/// and notification paths need real hardware and are covered by the on-device protocol instead.
	/// </summary>
	[TestFixture, Category("Internal"), Category("Curated")]
	public class AppleEventsTests : TestRunner
	{
		/// <summary>
		/// A scripting definition small enough to read but covering the shapes that cost a debugging round when
		/// they are wrong: an eight-character command code, inheritance, a class-extension, an explicit plural,
		/// terms containing spaces, synonyms, enumerations, and a name defined by two suites at once.
		/// </summary>
		private const string FixtureSdef = """
			<?xml version="1.0" encoding="UTF-8"?>
			<dictionary title="Fixture">
				<suite name="Standard Suite" code="core" description="Common commands.">
					<command name="get" code="coregetd" description="Get the data for an object.">
						<direct-parameter type="specifier"/>
						<parameter name="as" code="rtyp" type="type" optional="yes"/>
						<result type="any"/>
					</command>
					<command name="count" code="corecnte">
						<direct-parameter type="specifier"/>
						<parameter name="each" code="kocl" type="type" optional="yes"/>
						<result type="integer"/>
					</command>
					<command name="close" code="coreclos">
						<direct-parameter type="specifier"/>
						<parameter name="saving" code="savo" optional="yes">
							<type type="save options"/>
						</parameter>
						<synonym name="shut"/>
					</command>
					<enumeration name="save options" code="savo">
						<enumerator name="yes" code="yes "/>
						<enumerator name="no" code="no  "/>
						<enumerator name="ask" code="ask ">
							<synonym name="prompt"/>
						</enumerator>
					</enumeration>
					<class name="item" code="cobj" description="A scriptable object.">
						<property name="class" code="pcls" type="type" access="r"/>
						<property name="name" code="pnam" type="text"/>
					</class>
					<class name="application" code="capp" inherits="item" plural="applications">
						<property name="frontmost" code="pisf" type="boolean" access="r"/>
						<element type="window"/>
						<element type="document"/>
					</class>
					<class name="window" code="cwin" inherits="item" plural="windows">
						<property name="index" code="pidx" type="integer"/>
						<property name="file name" code="ppth" type="text" access="r">
							<synonym name="path"/>
						</property>
						<responds-to command="close"><cocoa method="handleClose:"/></responds-to>
					</class>
					<class name="document" code="docu" inherits="item"/>
				</suite>
				<suite name="Fixture Suite" code="fixt" description="The application's own terminology.">
					<command name="refresh" code="fixtrfsh">
						<direct-parameter type="specifier" optional="yes"/>
					</command>
					<command name="count" code="fixtcnt2" description="Same name, different event.">
						<result type="integer"/>
					</command>
					<class-extension extends="application">
						<property name="theme" code="thme" type="text"/>
						<element type="tab"/>
					</class-extension>
					<class name="tab" code="ctab" inherits="item"/>
				</suite>
			</dictionary>
			""";

		private static AESdefDictionary Fixture(Func<string, string> resolveInclude = null)
			=> AESdef.Parse(FixtureSdef, resolveInclude);

		// ---- four-character codes -----------------------------------------------------------------

		[Test]
		public void FourCharCodesRoundTrip()
		{
			Assert.That(AEFourCharCode.Unpack(AEFourCharCode.Pack("utxt")), Is.EqualTo("utxt"));
			// Spaces are significant: "ID  " and "all " are ordinary codes.
			Assert.That(AEFourCharCode.Unpack(AEFourCharCode.Pack("ID  ")), Is.EqualTo("ID  "));
			Assert.That(AEFourCharCode.Unpack(AEFourCharCode.Pack("all ")), Is.EqualTo("all "));
		}

		[Test]
		public void FourCharCodesPackBigEndian()
		{
			// 'long' is 0x6C6F6E67: the first character occupies the most significant byte.
			Assert.That(AEFourCharCode.Pack("long"), Is.EqualTo(0x6C6F6E67u));
		}

		[Test]
		public void FourCharCodesRejectWrongLength()
		{
			Assert.That(AEFourCharCode.TryPack("utf".AsSpan(), out _), Is.False);
			Assert.That(AEFourCharCode.TryPack("utext".AsSpan(), out _), Is.False);
			Assert.That(AEFourCharCode.TryPack("".AsSpan(), out _), Is.False);
			_ = Assert.Throws<ArgumentException>(() => AEFourCharCode.Pack("nope!"));
		}

		// ---- canonical names -----------------------------------------------------------------------

		[Test]
		public void KeyFoldsSpacesUnderscoresAndCase()
		{
			// A script cannot write "file name", so every spelling it can write has to reach the same term.
			Assert.That(AESdef.Key("file name"), Is.EqualTo("filename"));
			Assert.That(AESdef.Key("FileName"), Is.EqualTo("filename"));
			Assert.That(AESdef.Key("file_name"), Is.EqualTo("filename"));
			Assert.That(AESdef.Key("FILE NAME"), Is.EqualTo("filename"));
		}

		// ---- commands -------------------------------------------------------------------------------

		[Test]
		public void CommandCodeSplitsIntoClassAndEvent()
		{
			var command = Fixture().CommandsByKey["get"].Single();
			Assert.That(AEFourCharCode.Unpack(command.EventClass), Is.EqualTo("core"));
			Assert.That(AEFourCharCode.Unpack(command.EventId), Is.EqualTo("getd"));
			Assert.That(command.HasDirectParameter, Is.True);
		}

		[Test]
		public void CommandParametersCarryTheirKeywords()
		{
			var close = Fixture().CommandsByKey["close"].Single();
			Assert.That(close.TryGetParameter("saving", out var saving), Is.True);
			Assert.That(AEFourCharCode.Unpack(saving.Code), Is.EqualTo("savo"));
			// The type came from a child element rather than an attribute.
			Assert.That(saving.TypeName, Is.EqualTo("save options"));
			Assert.That(saving.Optional, Is.True);
		}

		[Test]
		public void CommandSynonymsResolveToTheSameCommand()
		{
			var dict = Fixture();
			Assert.That(dict.CommandsByKey.ContainsKey("shut"), Is.True);
			Assert.That(dict.CommandsByKey["shut"].Single().EventId, Is.EqualTo(dict.CommandsByKey["close"].Single().EventId));
		}

		[Test]
		public void SameNameInTwoSuitesStaysAmbiguous()
		{
			// Two different events sharing a name is what the object model has to report rather than guess at.
			var counts = Fixture().CommandsByKey["count"];
			Assert.That(counts.Count, Is.EqualTo(2));
			Assert.That(counts.Select(c => c.Suite), Is.EquivalentTo(new[] { "Standard Suite", "Fixture Suite" }));
		}

		// ---- classes ---------------------------------------------------------------------------------

		[Test]
		public void PropertiesAreFoundThroughInheritance()
		{
			var dict = Fixture();
			// "name" is defined on item, which window inherits from.
			var name = dict.FindProperty("window", "name");
			Assert.That(name, Is.Not.Null);
			Assert.That(AEFourCharCode.Unpack(name.Code), Is.EqualTo("pnam"));
			Assert.That(dict.FindProperty("window", "index"), Is.Not.Null);
		}

		[Test]
		public void PropertyAccessDefaultsToReadWrite()
		{
			var dict = Fixture();
			var name = dict.FindProperty("window", "name");
			Assert.Multiple(() =>
			{
				Assert.That(name.CanRead, Is.True);
				Assert.That(name.CanWrite, Is.True);
			});
			var path = dict.FindProperty("window", "filename");
			Assert.Multiple(() =>
			{
				Assert.That(path.CanRead, Is.True);
				Assert.That(path.CanWrite, Is.False);
			});
		}

		[Test]
		public void PropertySynonymResolves()
		{
			var dict = Fixture();
			Assert.That(dict.FindProperty("window", "path")?.Code, Is.EqualTo(dict.FindProperty("window", "filename")?.Code));
		}

		[Test]
		public void ClassExtensionMergesIntoTheExistingClass()
		{
			var dict = Fixture();
			// "theme" and the tab element were added by a different suite than the one defining application.
			Assert.That(dict.FindProperty("application", "theme"), Is.Not.Null);
			Assert.That(dict.FindProperty("application", "frontmost"), Is.Not.Null);
			Assert.That(dict.FindElement("application", "tabs"), Is.Not.Null);
		}

		[Test]
		public void ElementsResolveByPlural()
		{
			var dict = Fixture();
			Assert.That(dict.FindElement("application", "windows")?.Name, Is.EqualTo("window"));
			// document has no explicit plural, so it falls back to the name plus s.
			Assert.That(dict.FindElement("application", "documents")?.Name, Is.EqualTo("document"));
			Assert.That(dict.FindElement("application", "windoze"), Is.Null);
		}

		[Test]
		public void ClassesAreReachableByCode()
		{
			var dict = Fixture();
			Assert.That(dict.ClassesByCode[AEFourCharCode.Pack("cwin")].Name, Is.EqualTo("window"));
			Assert.That(dict.ClassesByCode[AEFourCharCode.Pack("capp")].Name, Is.EqualTo("application"));
		}

		[Test]
		public void RespondsToIsRecorded()
		{
			Assert.That(Fixture().RespondsTo("window", "close"), Is.True);
			Assert.That(Fixture().RespondsTo("document", "close"), Is.False);
		}

		// ---- enumerations -----------------------------------------------------------------------------

		[Test]
		public void EnumeratorsMapBothWays()
		{
			var dict = Fixture();
			var options = dict.EnumerationsByName["save options"];
			Assert.That(AEFourCharCode.Unpack(options.Enumerators["yes"]), Is.EqualTo("yes "));
			Assert.That(AEFourCharCode.Unpack(options.Enumerators["ask"]), Is.EqualTo("ask "));
			// A synonym reaches the same enumerator.
			Assert.That(options.Enumerators["prompt"], Is.EqualTo(options.Enumerators["ask"]));
			Assert.That(dict.EnumeratorNamesByCode[AEFourCharCode.Pack("no  ")], Is.EqualTo("no"));
		}

		// ---- includes and robustness --------------------------------------------------------------------

		[Test]
		public void IncludedSuitesAreMerged()
		{
			const string included = """
				<?xml version="1.0" encoding="UTF-8"?>
				<dictionary title="Included">
					<suite name="Extra Suite" code="extr">
						<command name="reveal" code="extrrevl"/>
					</suite>
				</dictionary>
				""";
			const string host = """
				<?xml version="1.0" encoding="UTF-8"?>
				<dictionary title="Host" xmlns:xi="http://www.w3.org/2003/XInclude">
					<xi:include href="file:///System/Library/ScriptingDefinitions/CocoaStandard.sdef"/>
					<suite name="Own Suite" code="ownn">
						<command name="ping" code="ownnping"/>
					</suite>
				</dictionary>
				""";
			var dict = AESdef.Parse(host, _ => included);
			Assert.That(dict.CommandsByKey.ContainsKey("ping"), Is.True);
			Assert.That(dict.CommandsByKey.ContainsKey("reveal"), Is.True);
			Assert.That(dict.Suites, Does.Contain("Extra Suite"));
		}

		[Test]
		public void AMissingIncludeStillYieldsTheApplicationsOwnTerminology()
		{
			const string host = """
				<?xml version="1.0" encoding="UTF-8"?>
				<dictionary title="Host" xmlns:xi="http://www.w3.org/2003/XInclude">
					<xi:include href="file:///nope.sdef"/>
					<suite name="Own Suite" code="ownn">
						<command name="ping" code="ownnping"/>
					</suite>
				</dictionary>
				""";
			var dict = AESdef.Parse(host, _ => null);
			Assert.That(dict.CommandsByKey.ContainsKey("ping"), Is.True);
		}

		[Test]
		public void MalformedDefinitionsAreReportedNotSwallowed()
			=> Assert.Throws<FormatException>(() => AESdef.Parse("<dictionary><suite>"));

		[Test]
		public void EmptyDefinitionYieldsAnEmptyDictionary()
		{
			Assert.That(AESdef.Parse("").Suites, Is.Empty);
			Assert.That(AESdef.Parse(null).CommandsByKey, Is.Empty);
		}

		[Test]
		public void InheritanceCyclesDoNotHang()
		{
			const string cyclic = """
				<?xml version="1.0" encoding="UTF-8"?>
				<dictionary title="Cyclic">
					<suite name="S" code="ssss">
						<class name="a" code="aaaa" inherits="b"/>
						<class name="b" code="bbbb" inherits="a"/>
					</suite>
				</dictionary>
				""";
			var dict = AESdef.Parse(cyclic);
			Assert.That(dict.Ancestry("a").Count(), Is.EqualTo(2));
			Assert.That(dict.FindProperty("a", "nothing"), Is.Null);
		}

		[Test]
		public void ARestatedCommandMergesRatherThanBeingDropped()
		{
			// An application commonly restates a standard command to add its own parameters. Both definitions are
			// the same event, so the second must extend the first instead of being discarded as a duplicate.
			const string sdef = """
				<?xml version="1.0" encoding="UTF-8"?>
				<dictionary title="Restated">
					<suite name="Standard Suite" code="core">
						<command name="open" code="aevtodoc">
							<direct-parameter type="file"/>
						</command>
					</suite>
					<suite name="Own Suite" code="ownn">
						<command name="open" code="aevtodoc">
							<parameter name="reading" code="rdng" type="boolean" optional="yes"/>
						</command>
					</suite>
				</dictionary>
				""";
			var dict = AESdef.Parse(sdef);
			var open = dict.CommandsByKey["open"];
			Assert.That(open.Count, Is.EqualTo(1), "the same event under one name is one command");
			Assert.Multiple(() =>
			{
				Assert.That(open[0].HasDirectParameter, Is.True);
				Assert.That(open[0].TryGetParameter("reading", out _), Is.True);
			});
		}

		[Test]
		public void AMalformedCodeSkipsOnlyThatTerm()
		{
			// A dictionary is hundreds of terms; one unusable code must not cost the application all of them,
			// because the failure is cached and would leave the application permanently unusable.
			const string sdef = """
				<?xml version="1.0" encoding="UTF-8"?>
				<dictionary title="Damaged">
					<suite name="S" code="ssss">
						<command name="good" code="ssssgood"/>
						<command name="shortcode" code="sss"/>
						<command name="widechar" code="ssss☃ood"/>
						<class name="thing" code="thng">
							<property name="fine" code="fine"/>
							<property name="broken" code="ab"/>
						</class>
					</suite>
				</dictionary>
				""";
			var dict = AESdef.Parse(sdef);
			Assert.Multiple(() =>
			{
				Assert.That(dict.CommandsByKey.ContainsKey("good"), Is.True);
				Assert.That(dict.CommandsByKey.ContainsKey("shortcode"), Is.False);
				Assert.That(dict.CommandsByKey.ContainsKey("widechar"), Is.False);
				Assert.That(dict.FindProperty("thing", "fine"), Is.Not.Null);
				Assert.That(dict.FindProperty("thing", "broken"), Is.Null);
			});
		}

		[Test]
		public void PropertyCodesAreIndexedForRecordKeys()
		{
			// Writing "with properties {name: ...}" needs the keyword without knowing the class.
			var dict = Fixture();
			Assert.That(dict.PropertyCodesByKey["name"], Is.EqualTo(AEFourCharCode.Pack("pnam")));
			Assert.That(dict.PropertyNamesByCode[AEFourCharCode.Pack("ppth")], Is.EqualTo("file name"));
		}
	}
}
