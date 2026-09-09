param(
    [string]$LocalizationDirectory = "$PSScriptRoot\..\Orynivo\Localization\Overrides"
)

$ru = (Get-Content (Join-Path $LocalizationDirectory 'ru-RU.json') -Raw | ConvertFrom-Json).PSObject.Properties.Name
$zh = (Get-Content (Join-Path $LocalizationDirectory 'zh-CN.json') -Raw | ConvertFrom-Json).PSObject.Properties.Name
$missingInChinese = @($ru | Where-Object { $_ -notin $zh })
$missingInRussian = @($zh | Where-Object { $_ -notin $ru })

if ($missingInChinese.Count -or $missingInRussian.Count) {
    if ($missingInChinese.Count) { Write-Error "Missing in zh-CN.json: $($missingInChinese -join ', ')" }
    if ($missingInRussian.Count) { Write-Error "Missing in ru-RU.json: $($missingInRussian -join ', ')" }
    exit 1
}

Write-Output "Russian/Chinese override key parity verified ($($ru.Count) keys)."
