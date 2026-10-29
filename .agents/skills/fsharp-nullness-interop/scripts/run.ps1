#Requires -Version 7.2
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

$expectedClean = @(
    'tryHost-valid | Some "example.com"'
    'tryHost-invalid | None'
    'tryName-null | None'
    'tryName-value | Some "Ada"'
    'envOr-missing | fallback'
    'rootMessage-single | outer'
    'rootMessage-nested | inner'
    'tryParent-root | None'
    'tryParent-bare | None'
    'getDirectoryName-bare | ""'
    'tryLoad-null | Error "empty document"'
    'tryFirstLine-empty | None'
    'someLeak-empty | Some null'
    'stringCoerce-null | ""'
    'delay-3 | 00:00:04'
    'fromHours-int | 01:00:00'
    'fromDays-int | 1.00:00:00'
    'nonNull-null | NullReferenceException'
    'uncheckedNonNull-null | null passes'
)

function Invoke-Build([string]$ProjectDir) {
    Push-Location $ProjectDir
    try {
        $output = dotnet build -c Release 2>&1 | ForEach-Object { "$_" }
        [pscustomobject]@{ ExitCode = $LASTEXITCODE; Text = ($output -join "`n") }
    } finally { Pop-Location }
}

$failures = @()
$work = Join-Path ([IO.Path]::GetTempPath()) "fsharp-nullness-interop-proof-$PID"
$artifacts = Join-Path ([IO.Path]::GetTempPath()) "fsharp-nullness-interop-proof-artifacts-$PID"
$env:FSHARP_NULLNESS_PROOF_ROOT = $artifacts

try {
    $clean = Invoke-Build (Join-Path $PSScriptRoot 'Clean')
    if ($clean.ExitCode -ne 0) {
        $failures += "Clean build failed with nullness and TreatWarningsAsErrors on:`n$($clean.Text)"
    } else {
        Push-Location (Join-Path $PSScriptRoot 'Clean')
        try {
            $lines = @(dotnet run -c Release --no-build 2>&1 | ForEach-Object { "$_".TrimEnd() })
            $runExit = $LASTEXITCODE
        } finally { Pop-Location }
        if ($runExit -ne 0) { $failures += "Clean run exited with $runExit`n$($lines -join "`n")" }
        foreach ($want in $expectedClean) {
            $key = ($want -split ' \| ', 2)[0]
            $got = $lines | Where-Object { $_.StartsWith("$key | ") } | Select-Object -First 1
            Write-Output ("clean  {0,-24} {1}" -f $key, $(if ($got) { ($got -split ' \| ', 2)[1] } else { '<missing>' }))
            if ($got -cne $want) { $failures += "Clean '$key' expected '$want', got '$got'" }
        }
    }

    $template = Get-Content -Raw (Join-Path $PSScriptRoot 'breaks/Break.fsproj.txt')
    foreach ($file in Get-ChildItem (Join-Path $PSScriptRoot 'breaks') -Filter *.fs) {
        $source = Get-Content -Raw $file.FullName
        $texts = @([regex]::Matches($source, '//\s*expect-text:\s*(.+?)\s*$', 'Multiline') | ForEach-Object { $_.Groups[1].Value })
        $codes = @([regex]::Matches($source, '//\s*expect:\s*(FS\d+)') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
        $dir = Join-Path $work $file.BaseName
        New-Item -ItemType Directory -Force -Path $dir | Out-Null
        foreach ($name in 'global.json', 'Directory.Build.props', 'Directory.Build.targets', 'Directory.Packages.props') {
            Copy-Item (Join-Path $PSScriptRoot $name) $dir
        }
        Copy-Item $file.FullName (Join-Path $dir 'Program.fs')
        Set-Content (Join-Path $dir "$($file.BaseName).fsproj") $template
        $r = Invoke-Build $dir
        $reported = @([regex]::Matches($r.Text, 'error (FS\d+)') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
        Write-Output ("break  {0,-24} exit={1} reported={2} expected={3}" -f $file.BaseName, $r.ExitCode, ($reported -join ','), ($codes -join ','))
        $missing = @($texts | Where-Object { -not $r.Text.Contains($_) })
        if ($codes.Count -eq 0) { $failures += "$($file.Name) has no // expect: line." }
        elseif ($r.ExitCode -eq 0) { $failures += "$($file.Name) compiled. Expected $($codes -join ', ')." }
        else {
            if ($missing) { $failures += "$($file.Name) did not report: $($missing -join ' | ')" }
            if (($codes -join ',') -cne ($reported -join ',')) { $failures += "$($file.Name) reported $($reported -join ', '). Expected $($codes -join ', ').`n$($r.Text)" }
        }
    }
} finally {
    Remove-Item Env:FSHARP_NULLNESS_PROOF_ROOT -ErrorAction SilentlyContinue
    foreach ($path in $work, $artifacts) {
        if (Test-Path $path) { Remove-Item -Recurse -Force -LiteralPath $path -ErrorAction SilentlyContinue }
    }
}

if ($failures) {
    $failures | ForEach-Object { Write-Error $_ -ErrorAction Continue }
    exit 1
}
Write-Output 'All nullness and overload proofs passed.'
