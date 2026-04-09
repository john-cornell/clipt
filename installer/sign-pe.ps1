param(
    [Parameter(Mandatory)][string]$SignTool,
    [Parameter(Mandatory)][string]$Cert,
    [Parameter(Mandatory)][string]$Target,
    [string]$PasswordFile
)

$ErrorActionPreference = 'Stop'
$stArgs = @('sign', '/f', $Cert, '/fd', 'SHA256', '/tr', 'http://timestamp.digicert.com', '/td', 'SHA256')
if ($PasswordFile -and (Test-Path -LiteralPath $PasswordFile)) {
    $pw = (Get-Content -LiteralPath $PasswordFile -Raw).Trim()
    $stArgs += @('/p', $pw)
}
$stArgs += $Target
& $SignTool @stArgs
exit $LASTEXITCODE
