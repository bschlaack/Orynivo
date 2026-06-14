param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    [string]$AsioSdkDir = $env:ASIO_SDK_DIR,

    [string]$MSBuildPath = $env:MSBUILD_EXE_PATH,

    [switch]$RequireAsio,

    [switch]$SkipAsio
)

$ErrorActionPreference = 'Stop'

if ($RequireAsio -and $SkipAsio) {
    throw '-RequireAsio and -SkipAsio cannot be used together.'
}

function Resolve-MSBuildPath {
    param([string]$ExplicitPath)

    if ($ExplicitPath) {
        if (-not (Test-Path -LiteralPath $ExplicitPath -PathType Leaf)) {
            throw "MSBuild was not found at '$ExplicitPath'."
        }
        return (Resolve-Path -LiteralPath $ExplicitPath).Path
    }

    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path -LiteralPath $vswhere -PathType Leaf) {
        $installationPath = & $vswhere `
            -latest `
            -products '*' `
            -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 `
            -property installationPath
        if ($installationPath) {
            foreach ($relativePath in @(
                'MSBuild\Current\Bin\amd64\MSBuild.exe',
                'MSBuild\Current\Bin\MSBuild.exe'
            )) {
                $candidate = Join-Path $installationPath.Trim() $relativePath
                if (Test-Path -LiteralPath $candidate -PathType Leaf) {
                    return $candidate
                }
            }
        }
    }

    $command = Get-Command MSBuild.exe -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    throw 'MSBuild with the Visual C++ x64 tools was not found. Install the Visual Studio "Desktop development with C++" workload or pass -MSBuildPath.'
}

function Resolve-AsioSdkPath {
    param([string]$ExplicitPath)

    if ($ExplicitPath) {
        $resolved = Resolve-Path -LiteralPath $ExplicitPath -ErrorAction SilentlyContinue
        if ($resolved -and
            (Test-Path -LiteralPath (Join-Path $resolved.Path 'common\asio.cpp')) -and
            (Test-Path -LiteralPath (Join-Path $resolved.Path 'host\asiodrivers.cpp')) -and
            (Test-Path -LiteralPath (Join-Path $resolved.Path 'host\pc\asiolist.cpp'))) {
            return $resolved.Path
        }

        throw "The ASIO SDK at '$ExplicitPath' is incomplete or does not exist."
    }

    $candidates = @(
        (Join-Path $PSScriptRoot 'third_party\asiosdk'),
        (Join-Path $PSScriptRoot 'external\asiosdk'),
        'C:\Dev\asiosdk_2.3'
    )

    foreach ($candidate in $candidates) {
        $resolved = Resolve-Path -LiteralPath $candidate -ErrorAction SilentlyContinue
        if ($resolved -and
            (Test-Path -LiteralPath (Join-Path $resolved.Path 'common\asio.cpp')) -and
            (Test-Path -LiteralPath (Join-Path $resolved.Path 'host\asiodrivers.cpp')) -and
            (Test-Path -LiteralPath (Join-Path $resolved.Path 'host\pc\asiolist.cpp'))) {
            return $resolved.Path
        }
    }

    return $null
}

$asioSdk = if ($SkipAsio) { $null } else { Resolve-AsioSdkPath $AsioSdkDir }
$nativeProject = Join-Path $PSScriptRoot 'Native\AsioBridge\AsioBridge.vcxproj'
$managedProject = Join-Path $PSScriptRoot 'Orynivo\Orynivo.csproj'

if ($asioSdk) {
    $msbuild = Resolve-MSBuildPath $MSBuildPath
    Write-Host "MSBuild: $msbuild"
    Write-Host "ASIO SDK: $asioSdk"

    & $msbuild $nativeProject `
        /p:Configuration=$Configuration `
        /p:Platform=x64 `
        "/p:AsioSdkDir=$asioSdk"
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}
elseif ($RequireAsio) {
    throw 'The Steinberg ASIO SDK was not found, but -RequireAsio was specified.'
}
else {
    Write-Host 'Building Orynivo without ASIO support.'
}

$includeAsioBridge = if ($null -ne $asioSdk) { 'true' } else { 'false' }
dotnet build $managedProject -c $Configuration "/p:IncludeAsioBridge=$includeAsioBridge"
exit $LASTEXITCODE
