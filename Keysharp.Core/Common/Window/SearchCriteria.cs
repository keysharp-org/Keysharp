namespace Keysharp.Core.Common.Window
{
	internal class SearchCriteria
	{
		internal bool IsPureID = false;
		internal bool Active { get; private set; }
		internal string ClassName { get; set; }
		internal string ExcludeText { get; set; }
		internal string ExcludeTitle { get; set; }
		internal string Group { get; set; }
		internal bool HasExcludes => !string.IsNullOrEmpty(ExcludeTitle) || !string.IsNullOrEmpty(ExcludeText);
		internal bool HasID => ID != 0;
		internal nint ID { get; set; }
		internal bool IsEmpty => !HasID && PID == 0 && !HasExcludes && string.IsNullOrEmpty(Group) && string.IsNullOrEmpty(Title) && string.IsNullOrEmpty(Text) && string.IsNullOrEmpty(ClassName) && string.IsNullOrEmpty(Path);
		internal string Path { get; set; }
		internal long PID { get; set; }
		internal string Text { get; set; }
		internal string Title { get; set; }

		internal static SearchCriteria FromString(object obj)
		{
			var criteria = new SearchCriteria();

			if (obj == null)
				return criteria;

			if (obj is long l)
			{
				criteria.ID = new nint(l);
				criteria.IsPureID = true;
				return criteria;
			}

			object hwnd = null;

			if (Script.TryGetPropertyValue(out hwnd, obj, "Hwnd") && hwnd != null)
			{
				if (hwnd is long ll)
				{
					criteria.ID = new nint(ll);
					criteria.IsPureID = true;
					return criteria;
				}
			}

			var mixed = obj.ToString();

			if (!mixed.Contains(Keyword_ahk, StringComparison.OrdinalIgnoreCase))
				return new SearchCriteria { Title = mixed };

			var i = 0;

			var t = false;

			int classCount = 0
							 , groupCount = 0
							 , exeCount = 0
							 , idCount = 0
							 , pidCount = 0
							 ;

			while (i < mixed.Length && (i = mixed.IndexOf(Keyword_ahk, i, StringComparison.OrdinalIgnoreCase)) != -1)
			{
				if (!t)
				{
					var pre = i == 0 ? string.Empty : mixed.Substring(0, i).Trim(Spaces);

					if (pre.Length != 0)
						criteria.Title = pre;

					t = true;
				}

				var span = mixed.AsSpan(i + Keyword_ahk.Length);

				if (span.StartsWith("class", StringComparison.OrdinalIgnoreCase))
				{
					var oldVal = criteria.ClassName;
					criteria.ClassName = span.Slice(5).TrimStart().ParseUntilSpace();

					if (oldVal != criteria.ClassName)
						classCount++;

					i += 9 + criteria.ClassName.Length;//Might be off by a space, but still reduces the next comparison.
				}
				else if (span.StartsWith("group", StringComparison.OrdinalIgnoreCase))
				{
					var oldVal = criteria.Group;
					criteria.Group = span.Slice(5).TrimStart().ParseUntilSpace();

					if (oldVal != criteria.Group)
						groupCount++;

					i += 9 + criteria.Group.Length;
				}
				else if (span.StartsWith("exe", StringComparison.OrdinalIgnoreCase))
				{
					var oldVal = criteria.Path;
					criteria.Path = span.Slice(3).TrimStart().ParseUntilSpace();

					if (oldVal != criteria.Path)
						exeCount++;

					i += 7 + criteria.Path.Length;
				}
				else if (span.StartsWith("id", StringComparison.OrdinalIgnoreCase))
				{
					var oldId = criteria.ID;
					var val = span.Slice(2).TrimStart().ParseUntilSpace();

					if (long.TryParse(val, out var id))
						criteria.ID = new nint(id);

					if (criteria.ID != oldId)
						idCount++;

					i += 6 + val.Length;
				}
				else if (span.StartsWith("A", StringComparison.OrdinalIgnoreCase))
				{
					criteria.Active = true;
					i += 5;
				}
				else if (span.StartsWith("pid", StringComparison.OrdinalIgnoreCase))
				{
					var oldVal = criteria.PID;
					var val = span.Slice(3).TrimStart().ParseUntilSpace();

					if (long.TryParse(val, out var id))
						criteria.PID = new nint(id);

					if (criteria.PID != oldVal)
						pidCount++;

					i += 7 + val.Length;
				}
				else
					i++;
			}

			//If they specified more than one of the same criteria, consider nothing.
			return classCount > 1
				   || groupCount > 1
				   || exeCount > 1
				   || idCount > 1
				   || pidCount > 1
				   ? new SearchCriteria()
				   : criteria;
		}

		internal static SearchCriteria FromString(object title, object text, object excludeTitle, object excludeText)
		{
			var criteria = FromString(title);
			criteria.Text = text.As();
			criteria.ExcludeTitle = excludeTitle.As();
			criteria.ExcludeText = excludeText.As();
			return criteria;
		}
	}
}