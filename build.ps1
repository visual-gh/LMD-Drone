# Builds the mod and puts the release zip in dist\ (Mods\LMDDrone.dll, README).
#   -Install   also copies the DLL into the game's Mods folder, for test loops
#   -GameDir   where the game is installed (the project references MelonLoader's generated assemblies there)
param(
    [string]$Dotnet = "dotnet",
    [string]$GameDir = "C:\Program Files (x86)\Steam\steamapps\common\Lonely Mountains - Downhill",
    [switch]$Install
)
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
& $Dotnet build (Join-Path $root "src\LMDDrone\LMDDrone.csproj") -c Release -nologo -v minimal "-p:GameDir=$GameDir"
if ($LASTEXITCODE -ne 0) { throw "build failed" }

$dll = Join-Path $root "src\LMDDrone\bin\Release\net6.0\LMDDrone.dll"
$version = ([xml](Get-Content (Join-Path $root "src\LMDDrone\LMDDrone.csproj"))).Project.PropertyGroup.Version | Select-Object -First 1
$stage = Join-Path $root "dist\stage"
Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force (Join-Path $stage "Mods") | Out-Null
Copy-Item $dll (Join-Path $stage "Mods")
Copy-Item (Join-Path $root "README.md") (Join-Path $stage "LMDDrone-README.md")
$zip = Join-Path $root "dist\LMD-Drone-$version.zip"
Remove-Item $zip -ErrorAction SilentlyContinue
# Entries named here with "/": PowerShell 5.1's Compress-Archive (and ZipFile.CreateFromDirectory on its .NET
# Framework) write "Mods\...", which some unzip tools (Linux, Steam Deck, macOS) extract as one file named with a backslash.
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::Open($zip, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in Get-ChildItem $stage -Recurse -File) {
        $entry = $file.FullName.Substring($stage.Length + 1).Replace('\', '/')
        [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $entry)
    }
} finally {
    $archive.Dispose()
}
Remove-Item $stage -Recurse -Force
"built $zip"

if ($Install) {
    Copy-Item $dll (Join-Path $GameDir "Mods") -Force
    "installed to $GameDir\Mods"
}
