param([switch]$SkipTests)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = $PSScriptRoot
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:NUGET_PACKAGES = Join-Path $repo '.tools/packages'
$localSdk = Join-Path $repo '.tools/dotnet/dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $localSdk) { $localSdk } else { (Get-Command dotnet).Source }
function Invoke-Dotnet([string[]]$Arguments) {
    & $dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet command failed with exit code $LASTEXITCODE" }
}
Push-Location $repo
try {
    Invoke-Dotnet -Arguments @('restore', 'tests/MyKadReader.Tests/MyKadReader.Tests.csproj')
    Invoke-Dotnet -Arguments @('build', 'tests/MyKadReader.Tests/MyKadReader.Tests.csproj', '-c', 'Release', '--no-restore')
    if (!$SkipTests) {
        & './tests/MyKadReader.Tests/bin/Release/net10.0-windows/win-x64/MyKadReader.Tests.exe'
        if ($LASTEXITCODE -ne 0) { throw 'Tests failed; release was not packaged.' }
    }
    # Unique staging folder avoids overwriting any user's portable Data folder.
    $stage = Join-Path $repo ('artifacts/staging-' + [Guid]::NewGuid().ToString('N'))
    Invoke-Dotnet -Arguments @('publish', 'src/MyKadReader/MyKadReader.csproj', '-c', 'Release', '--no-restore', '-o', $stage)
    Copy-Item -LiteralPath 'README.md', 'THIRD-PARTY-NOTICES.md' -Destination $stage
    Copy-Item -LiteralPath 'licenses', 'docs' -Destination $stage -Recurse
    $runtimeNotices = @(
        @('microsoft.netcore.app.runtime.win-x64', 'LICENSE.TXT', 'dotnet-runtime-LICENSE.txt'),
        @('microsoft.netcore.app.runtime.win-x64', 'THIRD-PARTY-NOTICES.TXT', 'dotnet-runtime-NOTICES.txt'),
        @('microsoft.windowsdesktop.app.runtime.win-x64', 'LICENSE', 'windowsdesktop-LICENSE.txt')
    )
    foreach ($entry in $runtimeNotices) {
        $packageRoot = Join-Path $env:NUGET_PACKAGES $entry[0]
        $packageVersion = Get-ChildItem -LiteralPath $packageRoot -Directory | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
        Copy-Item -LiteralPath (Join-Path $packageVersion.FullName $entry[1]) -Destination (Join-Path $stage ('licenses/' + $entry[2]))
    }
    if (Test-Path -LiteralPath (Join-Path $stage 'Data')) { throw 'Unexpected Data folder in release staging.' }
    $zip = Join-Path $repo 'artifacts/MyKadReader-portable-win-x64.zip'
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -Force
    Get-FileHash -LiteralPath $zip -Algorithm SHA256 | Format-List
    Write-Output "Portable release: $zip"
    Write-Output "Extracted release: $stage"
}
finally { Pop-Location }
