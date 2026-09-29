using Keysharp.Builtins;
namespace Keysharp.Internals.Window
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
		internal bool HasNonGroupCriteria => Active || HasID || PID != 0L || HasExcludes || !string.IsNullOrEmpty(Title) || !string.IsNullOrEmpty(Text) || !string.IsNullOrEmpty(ClassName) || !string.IsNullOrEmpty(Path);
		internal bool IsEmpty => !Active && !HasID && PID == 0 && !HasExcludes && string.IsNullOrEmpty(Group) && string.IsNullOrEmpty(Title) && string.IsNullOrEmpty(Text) && string.IsNullOrEmpty(ClassName) && string.IsNullOrEmpty(Path);
		internal bool IsOnlyActive => Active && !HasID && PID == 0 && !HasExcludes && string.IsNullOrEmpty(Group) && string.IsNullOrEmpty(Title) && string.IsNullOrEmpty(Text) && string.IsNullOrEmpty(ClassName) && string.IsNullOrEmpty(Path);
		internal WindowSearchOptions Options { get; private set; }
		internal string Path { get; set; }
		internal long PID { get; set; }
		internal string Text { get; set; }
		internal string Title { get; set; }

		/// <summary>
		/// Parses a WinTitle. False when reading an object's Hwnd raised an error the script continued, and the
		/// caller then returns its empty value at once.
		/// </summary>
		internal static bool TryFromString(object obj, out SearchCriteria criteria)
		{
			criteria = new SearchCriteria();

			if (obj == null)
				return true;

			if (obj is long l)
			{
				criteria.ID = new nint(l);
				criteria.IsPureID = true;
				return true;
			}
			else if (obj is not string)
			{
				var hwnd = Script.GetPropertyValueOrNull(obj, "Hwnd");
				criteria = null;

				if (hwnd == null)
				{
					_ = Errors.PropertyErrorOccurred($"Object did not have an Hwnd property.");
					return false;
				}

				if (hwnd is long ll)
				{
					criteria = new SearchCriteria { ID = new nint(ll), IsPureID = true };
					return true;
				}

				_ = Errors.TypeErrorOccurred(hwnd, typeof(long));
				return false;
			}

			var mixed = obj.ToString();

			if (!mixed.Contains(Keyword_ahk, StringComparison.OrdinalIgnoreCase))
			{
				criteria = string.Equals(mixed, "A", StringComparison.OrdinalIgnoreCase)
					? new SearchCriteria { Active = true }
					: new SearchCriteria { Title = mixed };
				return true;
			}

			var i = 0;
			var foundFirstCriterion = false;
			var invalidCriteria = false;

			while (i < mixed.Length && (i = mixed.IndexOf(Keyword_ahk, i, StringComparison.OrdinalIgnoreCase)) != -1)
			{
				if (!IsCriterionBoundary(mixed, i))
				{
					i += Keyword_ahk.Length;
					continue;
				}

				var next = FindNextCriterionStart(mixed, i + Keyword_ahk.Length);
				var segment = next == -1 ? mixed[i..] : mixed[i..(next - 1)];
				var span = segment.AsSpan();
				var recognized = false;

				if (span.StartsWith(Keyword_ahk_class, StringComparison.OrdinalIgnoreCase))
				{
					criteria.ClassName = span[Keyword_ahk_class.Length..].TrimStart(SpaceTab).ToString();
					recognized = true;
				}
				else if (span.StartsWith(Keyword_ahk_group, StringComparison.OrdinalIgnoreCase))
				{
					criteria.Group = span[Keyword_ahk_group.Length..].TrimStart(SpaceTab).ToString();
					recognized = true;
				}
				else if (span.StartsWith(Keyword_ahk_exe, StringComparison.OrdinalIgnoreCase))
				{
					criteria.Path = span[Keyword_ahk_exe.Length..].TrimStart(SpaceTab).ToString();
					recognized = true;
				}
				else if (span.StartsWith(Keyword_ahk_id, StringComparison.OrdinalIgnoreCase))
				{
					if (long.TryParse(span[Keyword_ahk_id.Length..].TrimStart(SpaceTab), out var id))
					{
						var hwnd = new nint(id);

						if (criteria.HasID && criteria.ID != hwnd)
							invalidCriteria = true;
						else
							criteria.ID = hwnd;
					}

					recognized = true;
				}
				else if (span.StartsWith(Keyword_ahk_pid, StringComparison.OrdinalIgnoreCase))
				{
					if (long.TryParse(span[Keyword_ahk_pid.Length..].TrimStart(SpaceTab), out var pid))
						criteria.PID = pid;

					recognized = true;
				}
				else if (span.StartsWith(Keyword_ahk_opt, StringComparison.OrdinalIgnoreCase))
				{
					var optSpan = span[Keyword_ahk_opt.Length..].TrimStart(SpaceTab);
					criteria.Options ??= new WindowSearchOptions();

					foreach (Range r in optSpan.SplitAny(SpaceTabSv))
					{
						var opt = optSpan[r];

						if (opt.Length != 0 && !criteria.Options.ApplyToken(opt.ToString()))
						{
							invalidCriteria = true;
							break;
						}
					}

					recognized = true;
				}

				if (recognized && !foundFirstCriterion)
				{
					var pre = i == 0 ? string.Empty : mixed.Substring(0, i - 1);

					if (pre.Trim(SpaceTab).Length != 0)
						criteria.Title = pre;
				}

				if (recognized)
					foundFirstCriterion = true;
				else
					i += Keyword_ahk.Length;

				if (recognized)
					i = next == -1 ? mixed.Length : next;
			}

			if (!foundFirstCriterion)
				criteria = new SearchCriteria { Title = mixed };
			else if (invalidCriteria)
				criteria = new SearchCriteria();
			else if (string.Equals(criteria.Title, "A", StringComparison.OrdinalIgnoreCase))
			{
				criteria.Active = true;
				criteria.Title = null;
			}

			return true;
		}

		/// <summary>
		/// As <see cref="TryFromString(object, out SearchCriteria)"/>, with the window text and exclusions. False
		/// also when one of those raised a TypeError the script continued.
		/// </summary>
		internal static bool TryFromString(object title, object text, object excludeTitle, object excludeText, out SearchCriteria criteria)
		{
			if (!TryFromString(title, out criteria)
					|| !text.CoerceString(out var t) || !excludeTitle.CoerceString(out var exclTitle) || !excludeText.CoerceString(out var exclText))
			{
				criteria = null;
				return false;
			}

			criteria.Text = t;
			criteria.ExcludeTitle = exclTitle;
			criteria.ExcludeText = exclText;
			return true;
		}

		private static int FindNextCriterionStart(string value, int startIndex)
		{
			var index = startIndex;

			while (index < value.Length && (index = value.IndexOf(Keyword_ahk, index, StringComparison.OrdinalIgnoreCase)) != -1)
			{
				if (IsCriterionBoundary(value, index) && IsKnownCriterion(value.AsSpan(index)))
					return index;

				index += Keyword_ahk.Length;
			}

			return -1;
		}

		private static bool IsCriterionBoundary(string value, int index)
		{
			if (index == 0)
				return true;

			return SpaceTab.Contains(value[index - 1]);
		}

		private static bool IsKnownCriterion(ReadOnlySpan<char> value)
			=> value.StartsWith(Keyword_ahk_class, StringComparison.OrdinalIgnoreCase)
			   || value.StartsWith(Keyword_ahk_exe, StringComparison.OrdinalIgnoreCase)
			   || value.StartsWith(Keyword_ahk_group, StringComparison.OrdinalIgnoreCase)
			   || value.StartsWith(Keyword_ahk_id, StringComparison.OrdinalIgnoreCase)
			   || value.StartsWith(Keyword_ahk_opt, StringComparison.OrdinalIgnoreCase)
			   || value.StartsWith(Keyword_ahk_pid, StringComparison.OrdinalIgnoreCase);

	}
}
