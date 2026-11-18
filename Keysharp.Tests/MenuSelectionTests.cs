using Keysharp.Internals.Os;

namespace Keysharp.Tests;

[TestFixture, NonParallelizable, Category("Internal"), Category("Curated")]
public class MenuSelectionTests
{
	[TestCase("1&", 0)]
	[TestCase("3&", 2)]
	[TestCase("0&", -1)]
	[TestCase("-1&", -1)]
	[TestCase("File", -1)]
	[TestCase("", -1)]
	public void Position(string selector, int expected)
	{
		Assert.That(ControlManagerBase.TryParseMenuPosition(selector, out var index), Is.EqualTo(expected >= 0));
		Assert.That(index, Is.EqualTo(expected));
	}

	[TestCase("&File", "file", true)]
	[TestCase("E&xit", "Exi", true)]
	[TestCase("Tools && More", "Tools & More", true)]
	[TestCase("Üks ja kaks", "ÜKS", true)]
	[TestCase("Open", "Other", false)]
	public void Caption(string caption, string selector, bool expected)
		=> Assert.That(ControlManagerBase.MenuMatchHelper(caption, selector), Is.EqualTo(expected));

#if OSX
	[TestCase(0, true)]
	[TestCase(-25204, true)]
	[TestCase(-25206, false)]
	public void NativeActionResult(int status, bool expected)
	{
		using var tree = new MenuTree { ActionStatus = status };

		Assert.That(tree.Select(["Probe", "Ping"], out var invalidPath, out _), Is.EqualTo(expected));
		Assert.That(invalidPath, Is.False);
		Assert.That(tree.PressCount, Is.EqualTo(1));
		Assert.That(tree.Pressed, Is.EqualTo(tree.Command));
	}

	[TestCase("AXTitle", false)]
	[TestCase("AXRole", false)]
	[TestCase("AXChildren", false)]
	[TestCase("AXEnabled", false)]
	[TestCase("AXRole", true)]
	[TestCase("AXChildren", true)]
	public void NativeQueryFailure(string attribute, bool submenu)
	{
		using var tree = new MenuTree();
		tree.Fail(submenu ? tree.Submenu : tree.Command, attribute, -25204);

		Assert.That(tree.Select(["Probe", "Ping"], out var invalidPath, out var failure), Is.False);
		Assert.That(invalidPath, Is.False);
		Assert.That(failure, Is.Not.Empty);
		Assert.That(tree.PressCount, Is.Zero);
	}

	[Test]
	public void NativeNestedQueryFailure()
	{
		using var tree = new MenuTree();
		var leaf = tree.AddSubmenu();
		tree.Fail(leaf, "AXEnabled", -25204);

		Assert.That(tree.Select(["Probe", "Ping", "Leaf"], out var invalidPath, out _), Is.False);
		Assert.That(invalidPath, Is.False);
		Assert.That(tree.PressCount, Is.Zero);
	}

	[TestCase(-25205)]
	[TestCase(-25212)]
	[TestCase(0)]
	public void NativeCommandChildren(int status)
	{
		using var tree = new MenuTree();
		if (status == 0)
			tree.Children(tree.Command);
		else
			tree.Fail(tree.Command, "AXChildren", status);

		Assert.That(tree.Select(["Probe", "Ping"], out var invalidPath, out _), Is.True);
		Assert.That(invalidPath, Is.False);
		Assert.That(tree.PressCount, Is.EqualTo(1));
	}

	[Test]
	public void NativeSubmenuIsNotCommand()
	{
		using var tree = new MenuTree();
		tree.AddSubmenu();

		Assert.That(tree.Select(["Probe", "Ping"], out var invalidPath, out _), Is.False);
		Assert.That(invalidPath, Is.True);
		Assert.That(tree.PressCount, Is.Zero);
	}

	private sealed class MenuTree : IDisposable
	{
		private readonly List<nint> owned = [];
		private readonly Dictionary<(nint Element, string Attribute), (int Status, nint Value)> attributes = [];
		private readonly nint root;
		internal nint Submenu { get; }
		internal nint Command { get; }
		internal int ActionStatus { get; set; }
		internal int PressCount { get; private set; }
		internal nint Pressed { get; private set; }

		internal MenuTree()
		{
			root = Node("Root", "AXMenuBar");
			var top = Node("Probe", "AXMenuBarItem");
			Submenu = Node("Submenu", "AXMenu");
			Command = Node("Ping", "AXMenuItem");
			Children(root, top);
			Children(top, Submenu);
			Children(Submenu, Command);
		}

		internal bool Select(IReadOnlyList<string> path, out bool invalidPath, out string failure)
			=> MacAccessibility.TrySelectMenuItem(root, path, Read, Press, out invalidPath, out failure);

		internal void Fail(nint element, string attribute, int status)
			=> attributes[(element, attribute)] = (status, 0);

		internal void Children(nint element, params nint[] children)
			=> attributes[(element, "AXChildren")] = (0, Own(CF.CFArrayCreate(0, children, children.Length, 0)));

		internal nint AddSubmenu()
		{
			var submenu = Node("Nested", "AXMenu");
			var leaf = Node("Leaf", "AXMenuItem");
			Children(Command, submenu);
			Children(submenu, leaf);
			return leaf;
		}

		private nint Node(string title, string role)
		{
			var node = Own(CF.CreateString(title));
			attributes[(node, "AXTitle")] = (0, node);
			attributes[(node, "AXRole")] = (0, Own(CF.CreateString(role)));
			attributes[(node, "AXEnabled")] = (0, Own(CF.CFRetain(CF.ResolvePointerConstant("kCFBooleanTrue"))));
			return node;
		}

		private nint Own(nint value)
		{
			owned.Add(value);
			return value;
		}

		private int Read(nint element, nint attribute, out nint value)
		{
			if (!attributes.TryGetValue((element, CF.ReadString(attribute)), out var result))
			{
				value = 0;
				return -25205;
			}
			value = result.Value != 0 ? CF.CFRetain(result.Value) : 0;
			return result.Status;
		}

		private int Press(nint element)
		{
			PressCount++;
			Pressed = element;
			return ActionStatus;
		}

		public void Dispose()
		{
			for (var i = owned.Count - 1; i >= 0; i--)
				CF.CFRelease(owned[i]);
		}
	}
#endif
}
