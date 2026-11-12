using Keysharp.Builtins;
namespace Keysharp.Internals.Mapper
{
	/// <summary>
	/// Maps DriveTypes
	/// </summary>
	internal class DriveTypeMapper : MapperBase<DriveType>
	{
		internal override DriveType? LookUpCLRType(string keyword) => base.LookUpCLRType(keyword);

		internal override string LookUpKeysharpType(DriveType clrType)
		{
			// A path that names no drive has no type, which AutoHotkey reports as an empty string.
			if (clrType == DriveType.NoRootDirectory)
				return "";

			var str = base.LookUpKeysharpType(clrType);
			return string.IsNullOrEmpty(str) ? Keyword_UNKNOWN : str;
		}

		internal override void SetUpMappingTable()
		{
			clrMappingTable.Add(DriveType.CDRom, Keyword_CDROM);
			clrMappingTable.Add(DriveType.Fixed, Keyword_FIXED);
			clrMappingTable.Add(DriveType.Network, Keyword_NETWORK);
			clrMappingTable.Add(DriveType.Ram, Keyword_RAMDISK);
			clrMappingTable.Add(DriveType.Removable, Keyword_REMOVABLE);
			clrMappingTable.Add(DriveType.Unknown, Keyword_UNKNOWN);
		}
	}
}