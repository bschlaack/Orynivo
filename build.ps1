$ErrorActionPreference = 'Stop'

$msbuild = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\amd64\MSBuild.exe'

& $msbuild '.\Native\AsioBridge\AsioBridge.vcxproj' /p:Configuration=Debug /p:Platform=x64
dotnet build '.\Player\Player.csproj' -c Debug
