#Requires -Version 7.2
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

function Invoke-Proof {
    param([string]$Script, [string]$Commands, [string]$ExpectedError, [string]$ExpectedWarning)

    if ($Commands) {
        $output = @($Commands | dotnet fsi --nowarn:3535 --nologo 2>&1)
    } else {
        $output = @(dotnet fsi --nowarn:3535 $Script 2>&1)
    }
    $exitCode = $LASTEXITCODE
    $text = ($output | ForEach-Object { "$_" }) -join "`n"
    if ($ExpectedError) {
        $errors = @([regex]::Matches($text, 'error (FS[0-9]+):') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
        if ($exitCode -eq 0 -or $errors.Count -ne 1 -or $errors[0] -ne $ExpectedError) {
            throw "$Script expected $ExpectedError and a failed compilation. Exit: $exitCode`n$text"
        }
        if ($ExpectedWarning -and $text -notmatch "warning ${ExpectedWarning}:") {
            throw "$Script expected warning $ExpectedWarning.`n$text"
        }
    } elseif ($exitCode -ne 0 -or $text -match 'error FS[0-9]+:') {
        throw "FSI failed. Exit: $exitCode`n$text"
    }
    $output
}

Push-Location $PSScriptRoot
try {
Invoke-Proof -Script 'Shapes.fsx'
Invoke-Proof -Script 'Effects.fsx'

Invoke-Proof -Commands @'
#load "Domain.fsx"
open Domain
reserveStock;;
chargeCard;;
scheduleShipment;;
placeOrder;;
fulfillBasket;;
checkout;;
#q;;
'@

Invoke-Proof -Script 'breaks/AddLeaf.fsx' -ExpectedError FS0366
Invoke-Proof -Script 'breaks/BypassConstructor.fsx' -ExpectedError FS1093
Invoke-Proof -Script 'breaks/Unresolved.fsx' -ExpectedError FS0331
Invoke-Proof -Script 'breaks/WrongEdge.fsx' -ExpectedError FS0001
} finally {
    Pop-Location
}
