$net = "net10.0-windows"
$FullPath = [System.IO.Path]::GetFullPath("$PWD\bin\release\$net\Keysharp.dll")
$DLLBytes = [System.IO.File]::ReadAllBytes($FullPath)
$Assembly = [System.Reflection.Assembly]::Load($DLLBytes)
$AssemblyVersion = $Assembly.GetName().Version.ToString()

Copy-Item ".\Keysharp.Install\Release\Keysharp.msi" -Destination ".\Keysharp_$AssemblyVersion.msi" -Force

# Compress-Archive is severely deficient because it doesn't allow for specifying specific paths within the zip file.
# So directly call the underlying .NET compression libraries.
# Gotten from: https://stackoverflow.com/a/52395011
# Load the .NET assembly
Add-Type -Assembly 'System.IO.Compression'
Add-Type -Assembly 'System.IO.Compression.FileSystem'

# Must be used for relative file locations with .NET functions instead of Set-Location:
[System.IO.Directory]::SetCurrentDirectory('.\')

# Create the zip file and open it:
$zfile = ".\Keysharp_$AssemblyVersion.zip"

if (Test-Path $zfile)
{
    Remove-Item $zfile
}

$z = [System.IO.Compression.ZipFile]::Open($zfile, [System.IO.Compression.ZipArchiveMode]::Create)

# Add a compressed file to the zip file:
[System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($z, ".\bin\release\$net\Keysharp.exe", "Keysharp.exe")
[System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($z, ".\bin\release\$net\Keyview.exe", "Keyview.exe")

[System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($z, ".\bin\release\$net\BitFaster.Caching.dll", "BitFaster.Caching.dll")
[System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($z, ".\bin\release\$net\Interop.IWshRuntimeLibrary.dll", "Interop.IWshRuntimeLibrary.dll")
[System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($z, ".\bin\release\$net\Keysharp.Core.dll", "Keysharp.Core.dll")
[System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($z, ".\bin\release\$net\Keysharp.dll", "Keysharp.dll")
[System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($z, ".\bin\release\$net\Keysharp.manifest", "Keysharp.manifest")
[System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($z, ".\bin\release\$net\Keyview.dll", "Keyview.dll")
[System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($z, ".\bin\release\$net\license.txt", "license.txt")
[System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($z, ".\README.md", "README.md")
[System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($z, ".\bin\release\$net\Microsoft.CodeAnalysis.CSharp.dll", "Microsoft.CodeAnalysis.CSharp.dll")
[System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($z, ".\bin\release\$net\Microsoft.CodeAnalysis.dll", "Microsoft.CodeAnalysis.dll")
[System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($z, ".\bin\release\$net\Microsoft.CodeDom.Providers.DotNetCompilerPlatform.dll", "Microsoft.CodeDom.Providers.DotNetCompilerPlatform.dll")
[System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($z, ".\bin\release\$net\Microsoft.Extensions.DependencyModel.dll", "Microsoft.Extensions.DependencyModel.dll")
[System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($z, ".\bin\release\$net\Microsoft.Extensions.Primitives.dll", "Microsoft.Extensions.Primitives.dll")
[System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($z, ".\bin\release\$net\Microsoft.NET.HostModel.dll", "Microsoft.NET.HostModel.dll")
[System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($z, ".\bin\release\$net\PCRE.NET.dll", "PCRE.NET.dll")
[System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($z, ".\bin\release\$net\Scintilla.NET.dll", "Scintilla.NET.dll")
[System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($z, ".\bin\release\$net\Semver.dll", "Semver.dll")
[System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($z, ".\bin\release\$net\System.Management.dll", "System.Management.dll")

[System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($z, ".\bin\release\$net\Keysharp.Core.deps.json", "Keysharp.Core.deps.json")
[System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($z, ".\bin\release\$net\Keysharp.deps.json", "Keysharp.deps.json")
[System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($z, ".\bin\release\$net\Keysharp.runtimeconfig.json", "Keysharp.runtimeconfig.json")
[System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($z, ".\bin\release\$net\Keyview.deps.json", "Keyview.deps.json")
[System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($z, ".\bin\release\$net\Keyview.runtimeconfig.json", "Keyview.runtimeconfig.json")

[System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($z, ".\bin\release\$net\runtimes\win-x64\native\Lexilla.dll", "runtimes\win-x64\native\Lexilla.dll")
[System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($z, ".\bin\release\$net\runtimes\win-x64\native\Scintilla.dll", "runtimes\win-x64\native\Scintilla.dll")

[System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($z, ".\bin\release\$net\Scripts\WindowSpy.ks", "Scripts\WindowSpy.ks")

# Close the file
$z.Dispose()
