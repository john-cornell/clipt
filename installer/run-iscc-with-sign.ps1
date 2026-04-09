param(
    [Parameter(Mandatory)][string]$IsccExe,
    [Parameter(Mandatory)][string]$IssPath,
    [Parameter(Mandatory)][string]$SignToolExe,
    [Parameter(Mandatory)][string]$SignCert,
    [string]$PasswordFile
)

$ErrorActionPreference = 'Stop'
$pfx = $SignCert.Replace('/', '\')
$st = $SignToolExe

if ($PasswordFile -and (Test-Path -LiteralPath $PasswordFile)) {
    $pw = (Get-Content -LiteralPath $PasswordFile -Raw).Trim()
    $signDef = "`"$st`" sign /f `$q$pfx`$q /p `$q$pw`$q /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 `$f"
}
else {
    $signDef = "`"$st`" sign /f `$q$pfx`$q /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 `$f"
}

& $IsccExe @('/DUSINGSIGNTOOL', "/SCliptSign=$signDef", $IssPath)
exit $LASTEXITCODE
