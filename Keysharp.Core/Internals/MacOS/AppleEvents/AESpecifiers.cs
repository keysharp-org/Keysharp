#if OSX
namespace Keysharp.Internals.AppleEvents
{
	internal enum AESpecifierKind
	{
		Property,
		ElementByIndex,
		ElementByName,
		ElementById,
		AllElements
	}

	/// <summary>One link in an object specifier: a property of what came before, or an element chosen out of it.</summary>
	internal sealed class AESpecifierStep
	{
		internal AESpecifierKind Kind;
		internal uint ClassCode;
		internal string ClassName;
		internal uint PropertyCode;
		internal string PropertyName;
		internal long Index;
		internal string Name;
		internal object Id;
	}

	/// <summary>
	/// Object specifiers are queries the target application resolves, not handles it hands out, so a chain is kept
	/// as a plain list of steps and turned into descriptors only for the moment an event is sent. That keeps a long
	/// lived script object from owning native memory, and makes the chain printable for error messages.
	/// </summary>
	internal static class AESpecifiers
	{
		/// <summary>Builds the descriptor for a chain. The caller owns the result.</summary>
		internal static AEValue Build(IReadOnlyList<AESpecifierStep> steps)
		{
			// The empty container is the application the event is addressed to; every step hangs off it.
			var container = AE.Null();

			try
			{
				if (steps != null)
					foreach (var step in steps)
					{
						using var keyData = MakeKeyData(step);
						var next = AE.MakeSpecifier(DesiredClass(step), container, KeyForm(step), keyData);
						container.Dispose();
						container = next;
					}
			}
			catch
			{
				container.Dispose();
				throw;
			}

			return container;
		}

		private static uint DesiredClass(AESpecifierStep step)
			=> step.Kind == AESpecifierKind.Property ? AE.CProperty : step.ClassCode;

		private static uint KeyForm(AESpecifierStep step) => step.Kind switch
		{
			AESpecifierKind.Property => AE.FormPropertyID,
			AESpecifierKind.ElementByName => AE.FormName,
			AESpecifierKind.ElementById => AE.FormUniqueID,
			_ => AE.FormAbsolutePosition
		};

		private static AEValue MakeKeyData(AESpecifierStep step)
		{
			switch (step.Kind)
			{
				case AESpecifierKind.Property:
					return AE.FromCode(AE.TypeType, step.PropertyCode);

				case AESpecifierKind.ElementByName:
					return AE.FromString(step.Name);

				case AESpecifierKind.ElementById:
					return AEMarshal.ToDescriptor(step.Id, null, null);

				case AESpecifierKind.AllElements:
					// "every" is an absolute position whose ordinal says all of them rather than a number.
					return AE.FromCode(AE.TypeAbsoluteOrdinal, AE.KAEAll);

				default:
					// Apple events count elements from one and read a negative index from the end, which is also
					// how Keysharp indexes an Array, so the script's number goes out unchanged.
					return AE.FromInt32(checked((int)step.Index));
			}
		}

		/// <summary>
		/// Renders a chain the way AppleScript would write it. Used for ToString and, more importantly, in error
		/// messages, where naming the object a script actually addressed is most of the diagnosis.
		/// </summary>
		internal static string Render(IReadOnlyList<AESpecifierStep> steps, string target)
		{
			var text = target;

			if (steps != null)
				foreach (var step in steps)
					text = StepText(step) + " of " + text;

			return text;
		}

		private static string StepText(AESpecifierStep step) => step.Kind switch
		{
			AESpecifierKind.Property => step.PropertyName,
			AESpecifierKind.ElementByName => $"{step.ClassName} \"{step.Name}\"",
			AESpecifierKind.ElementById => $"{step.ClassName} id {step.Id}",
			AESpecifierKind.AllElements => $"every {step.ClassName}",
			_ => $"{step.ClassName} {step.Index}"
		};
	}
}
#endif
