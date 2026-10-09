namespace Keyview;

internal readonly record struct EditorSelection(int Start, int Length);
internal sealed record EditorEdit(int Offset, string Removed, string Inserted, EditorSelection Before, EditorSelection After, DateTime Time)
{
	internal string Apply(string text, bool undo) => string.Concat(text.AsSpan(0, Offset), undo ? Removed : Inserted,
		text.AsSpan(Offset + (undo ? Inserted.Length : Removed.Length)));
	internal int Size => Removed.Length + Inserted.Length;
}

internal sealed class KeyviewEditHistory
{
	private const int MaxDepth = 200;
	private const int MaxCharacters = 4_000_000;
	private readonly LinkedList<EditorEdit> undo = new ();
	private readonly Stack<EditorEdit> redo = new ();
	private int characters;
	private bool canMerge;
	internal bool IsApplying { get; private set; }
	internal bool CanUndo => undo.Count > 0;
	internal bool CanRedo => redo.Count > 0;
	internal void Clear() { undo.Clear(); redo.Clear(); characters = 0; canMerge = false; }

	internal void RecordAppliedEdit(string before, string after, EditorSelection beforeSelection, Func<EditorSelection> apply, DateTime now)
	{
		EditorSelection afterSelection;
		IsApplying = true;
		try { afterSelection = apply(); }
		finally { IsApplying = false; }

		canMerge = false;
		Record(before, after, beforeSelection, afterSelection, now);
		canMerge = false;
	}

	internal void Record(string before, string after, EditorSelection beforeSelection, EditorSelection afterSelection, DateTime now)
	{
		if (IsApplying || before == after) return;
		foreach (var edit in redo) characters -= edit.Size;
		redo.Clear();
		var prefix = 0;
		while (prefix < before.Length && prefix < after.Length && before[prefix] == after[prefix]) prefix++;
		var suffix = 0;
		while (suffix < before.Length - prefix && suffix < after.Length - prefix
			&& before[before.Length - suffix - 1] == after[after.Length - suffix - 1]) suffix++;
		var next = new EditorEdit(prefix, before.Substring(prefix, before.Length - prefix - suffix),
			after.Substring(prefix, after.Length - prefix - suffix), beforeSelection, afterSelection, now);
		var previous = undo.Last?.Value;
		var merged = false;
		if (canMerge && previous != null && now - previous.Time < TimeSpan.FromSeconds(1)
			&& previous.After == beforeSelection && beforeSelection.Length == 0 && afterSelection.Length == 0)
		{
			if (next.Removed.Length == 0 && next.Inserted.Length == 1 && previous.Removed.Length == 0
				&& next.Offset == previous.Offset + previous.Inserted.Length)
			{
				next = next with { Offset = previous.Offset, Inserted = previous.Inserted + next.Inserted, Before = previous.Before };
				merged = true;
			}
			else if (next.Inserted.Length == 0 && next.Removed.Length == 1 && previous.Inserted.Length == 0
				&& next.Offset + next.Removed.Length == previous.Offset)
			{
				next = next with { Removed = next.Removed + previous.Removed, Before = previous.Before };
				merged = true;
			}
		}

		if (merged) { characters -= previous.Size; undo.RemoveLast(); }

		_ = undo.AddLast(next);
		characters += next.Size;
		canMerge = true;
		while (undo.Count > MaxDepth || characters > MaxCharacters)
		{
			characters -= undo.First.Value.Size;
			undo.RemoveFirst();
		}
	}

	internal (string Text, EditorSelection Selection) Undo(string text)
	{
		var edit = undo.Last.Value;
		undo.RemoveLast();
		redo.Push(edit);
		canMerge = false;
		return (edit.Apply(text, true), edit.Before);
	}

	internal (string Text, EditorSelection Selection) Redo(string text)
	{
		var edit = redo.Pop();
		_ = undo.AddLast(edit);
		canMerge = false;
		return (edit.Apply(text, false), edit.After);
	}
}
