using Keysharp.Builtins;
namespace Keysharp.Internals.Mapper
{
	internal class MapperBase<T> where T : struct, IConvertible
	{
		protected Dictionary<T, string> clrMappingTable = [];

		internal MapperBase() => SetUpMappingTable();

		internal virtual T? LookUpCLRType(string keyword)
		{
			T? res = null;

			foreach (var kv in clrMappingTable)
			{
				if (string.Equals(kv.Value, keyword, StringComparison.OrdinalIgnoreCase))
				{
					res = kv.Key;
					break;
				}
			}

			return res;
		}

		internal virtual string LookUpKeysharpType(T clrType) => clrMappingTable.TryGetValue(clrType, out var val) ? val : "";

		internal virtual void SetUpMappingTable()
		{
		}
	}
}