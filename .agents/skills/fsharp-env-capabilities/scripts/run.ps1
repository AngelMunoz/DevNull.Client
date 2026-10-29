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
Invoke-Proof -Script 'Environment.fsx'
Invoke-Proof -Script 'FlatEnv.fsx'

Invoke-Proof -Commands @'
#load "Environment.fsx"
open Environment
changePassword;;
audit;;
Db.updateUser;;
#q;;
'@

Invoke-Proof -Commands @'
#load "FlatEnv.fsx"
open FlatEnv
logThenRead;;
cacheThenDirect;;
#q;;
'@

Invoke-Proof -Script 'breaks/MissingCapability.fsx' -ExpectedError FS0193
Invoke-Proof -Script 'breaks/PinnedLeaf.fsx' -ExpectedError FS0193 -ExpectedWarning FS0064
} finally {
    Pop-Location
}
