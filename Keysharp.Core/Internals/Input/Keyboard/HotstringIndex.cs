namespace Keysharp.Internals.Input.Keyboard
{
	/// <summary>
	/// Hotstring triggers stored reversed and case-folded, so the definitions whose trigger ends some text are found by
	/// walking back from its last character. Case-sensitive triggers are folded too; callers compare them exactly.
	/// Not thread-safe: HotstringManager serializes access.
	/// </summary>
	internal sealed class HotstringIndex
	{
		// Siblings are chained rather than held in per-node dictionaries, since most nodes have one child.
		private Node[] nodes;
		private int nodeCount;
		// The root's children for ASCII keys, which most triggers end with, are found directly instead.
		private int[] asciiRoots;
		// Definitions which end at the same node, chained; callers sort what Find returns.
		private int[] nextRegistrationIndex;

		internal HotstringIndex() => Clear();

		internal static bool FoldedEquals(ReadOnlySpan<char> x, ReadOnlySpan<char> y)
		{
			if (x.Length != y.Length)
				return false;

			for (var i = 0; i < x.Length; i++)
				if (Fold(x[i]) != Fold(y[i]))
					return false;

			return true;
		}

		// Locale-independent, like AutoHotkey's CharLower.
		private static char Fold(char c) => char.ToLowerInvariant(c);

		internal void Add(string trigger, int registrationIndex)
		{
			var node = 0;

			for (var i = trigger.Length - 1; i >= 0; i--)
			{
				var key = Fold(trigger[i]);
				var child = Child(node, key);

				if (child == 0)
				{
					if (nodeCount == nodes.Length)
						System.Array.Resize(ref nodes, nodeCount * 2);

					child = nodeCount++;
					nodes[child] = new Node { Key = key, FirstRegistrationIndex = -1 };

					if (node == 0 && key < asciiRoots.Length)
						asciiRoots[key] = child;
					else
					{
						nodes[child].NextSibling = nodes[node].FirstChild;
						nodes[node].FirstChild = child;
					}
				}

				node = child;
			}

			if (registrationIndex >= nextRegistrationIndex.Length)
				System.Array.Resize(ref nextRegistrationIndex, Math.Max(registrationIndex + 1, nextRegistrationIndex.Length * 2));

			nextRegistrationIndex[registrationIndex] = nodes[node].FirstRegistrationIndex;
			nodes[node].FirstRegistrationIndex = registrationIndex;
		}

		internal void Clear()
		{
			nodes = new Node[64];
			nodes[0].FirstRegistrationIndex = -1;
			nodeCount = 1;
			asciiRoots = new int[128];
			nextRegistrationIndex = new int[64];
		}

		/// <summary>
		/// Writes into registrationIndices the definitions whose folded trigger ends text and, with beforeLast, those which end just
		/// before its last character. Returns how many there are, which exceeds registrationIndices.Length when they did not fit.
		/// </summary>
		internal int Find(ReadOnlySpan<char> text, bool beforeLast, Span<int> registrationIndices)
		{
			var count = FindEndingAt(text, registrationIndices, 0);
			return beforeLast && text.Length > 1 ? FindEndingAt(text[..^1], registrationIndices, count) : count;
		}

		internal bool HasMatch(ReadOnlySpan<char> text, bool beforeLast) =>
			HasMatchEndingAt(text) || beforeLast && text.Length > 1 && HasMatchEndingAt(text[..^1]);

		private bool HasMatchEndingAt(ReadOnlySpan<char> text)
		{
			var node = 0;

			for (var i = text.Length - 1; i >= 0; i--)
			{
				if ((node = Child(node, Fold(text[i]))) == 0)
					return false;

				if (nodes[node].FirstRegistrationIndex >= 0)
					return true;
			}

			return false;
		}

		private int FindEndingAt(ReadOnlySpan<char> text, Span<int> registrationIndices, int count)
		{
			var node = 0;

			for (var i = text.Length - 1; i >= 0; i--)
			{
				if ((node = Child(node, Fold(text[i]))) == 0)
					break;

				for (var registrationIndex = nodes[node].FirstRegistrationIndex; registrationIndex >= 0; registrationIndex = nextRegistrationIndex[registrationIndex])
				{
					if (count < registrationIndices.Length)
						registrationIndices[count] = registrationIndex;

					count++;
				}
			}

			return count;
		}

		private int Child(int node, char key)
		{
			if (node == 0 && key < asciiRoots.Length)
				return asciiRoots[key];

			for (var child = nodes[node].FirstChild; child != 0; child = nodes[child].NextSibling)
				if (nodes[child].Key == key)
					return child;

			return 0;
		}

		// The root is node 0, so 0 also marks a missing child or sibling.
		private struct Node
		{
			internal int FirstChild, NextSibling, FirstRegistrationIndex;
			internal char Key;
		}
	}
}
