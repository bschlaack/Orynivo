$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot

function Get-Matches([string]$relativePath, [string]$pattern) {
    $content = Get-Content -Raw -LiteralPath (Join-Path $root $relativePath)
    return [regex]::Matches($content, $pattern).Groups |
        Where-Object Name -eq 1 |
        ForEach-Object Value
}

$contracts = [ordered]@{
    MCP        = Get-Matches 'Orynivo/Mcp/McpTools.cs' 'McpServerTool\(Name = "([^"]+)"'
    ChatSchema = Get-Matches 'Orynivo/AI/AiToolDefinitions.cs' 'Make\("([^"]+)"'
    Dispatcher = Get-Matches 'Orynivo/AI/AiToolExecutor.cs' '"([a-z_]+)"\s*=>'
    Settings   = Get-Matches 'Orynivo/SettingsView.axaml.cs' '\("([a-z_]+)",\s+nameof\(McpTool'
}

$expected = @($contracts.MCP | Sort-Object -Unique)
foreach ($entry in $contracts.GetEnumerator()) {
    $actual = @($entry.Value | Sort-Object -Unique)
    $difference = Compare-Object $expected $actual
    if ($difference) {
        $details = $difference | Out-String
        throw "MCP tool parity failed for $($entry.Key):`n$details"
    }
}

Write-Host "MCP tool parity verified: $($expected.Count) tools."
