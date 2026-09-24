namespace Keysharp.Runtime
{
	/// <summary>
	/// The text of the files a script was compiled from, which a compile that is about to run embeds in the assembly so
	/// that Error.Stack and the error dialog can quote the lines they name. Compiled output does not carry it.
	/// </summary>
	internal static class SourceText
	{
		internal const string ResourceName = "Keysharp.SourceText";

		/// <summary>Deflates the text of each file, in file-index order.</summary>
		internal static byte[] Pack(IReadOnlyList<string> texts)
		{
			using var packed = new MemoryStream();

			using (var writer = new BinaryWriter(new DeflateStream(packed, CompressionLevel.Fastest, leaveOpen: true), Encoding.UTF8))
			{
				writer.Write(texts.Count);

				foreach (var text in texts)
					writer.Write(text ?? "");
			}

			return packed.ToArray();
		}

		/// <summary>The lines of each file an assembly carries the text of, by file index; empty when it carries none.</summary>
		internal static string[][] Lines(Assembly assembly)
		{
			using var stream = assembly?.GetManifestResourceStream(ResourceName);

			if (stream == null)
				return [];

			using var reader = new BinaryReader(new DeflateStream(stream, CompressionMode.Decompress), Encoding.UTF8);
			var files = new string[reader.ReadInt32()][];

			// The lexer counts lines at each '\n' alone.
			for (var i = 0; i < files.Length; i++)
				files[i] = reader.ReadString().Split('\n');

			return files;
		}
	}
}
