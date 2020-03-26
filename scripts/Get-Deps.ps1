[CmdletBinding()]
Param(
    [switch]
    $Trace = $false
)

Set-StrictMode -Version Latest
if ($Trace) {
    Set-PSDebug -Trace 1
}

. $PSScriptRoot\helpers.ps1 | out-null

try {
    $destdir = Join-Path $rootDirectory 'lib'
    Push-Location $destdir
    Invoke-Command -Fatal { &'7z' -y -bb3 x 'deps.zip' }

} finally {
    Pop-Location
}
