# Gates — the machine-checkable idiom battery for Phase 1 steps.
# Every gate below encodes a review rejection from this work.
# Usage: pwsh audits/gates.ps1 <file.fs> [<file.fs> ...]
# Orchestrator rule: zero unexplained hits on NEW code before a step
# reports as verified. The battery never replaces the read; it aims it.

param([Parameter(Mandatory, Position = 0, ValueFromRemainingArguments = $true)][string[]]$Files)

$gates = @(
  @{ Name = "bare-yield (only yield! may splice collections)"
     Pattern = 'yield'; CaseSensitive = $true
     Filter = { -not ($_.Line -match 'yield!') } }
  @{ Name = "pipe-ignore (interrupted chains; each survivor needs a reason)"
     Pattern = '|> ignore'; Simple = $true }
  @{ Name = "direct content assignment on a built control (chain it)"
     Pattern = '\.Content <-'; CaseSensitive = $true
     Filter = { $_.Line -notmatch 'this\.Content <-|root\.Content <-' } }
  @{ Name = "direct option comparison (compare the inner value)"
     Pattern = '(<>|=)\s+Some'; CaseSensitive = $true }
  @{ Name = "option re-wrap arm (Some-in Some-out; use Option.filter/map)"
     Pattern = '\|\s*Some\s+(\w+)\s+.*->\s*Some\s+\1'; CaseSensitive = $true }
  @{ Name = "private under a signature"
     Pattern = 'private'; CaseSensitive = $true }
  @{ Name = "let mutable (each needs a reason)"
     Pattern = 'let mutable'; CaseSensitive = $true }
  @{ Name = "collection round-trip on a fresh build"
     Pattern = 'List\.toArray|Array\.ofList|Seq\.toList'; CaseSensitive = $true }
  @{ Name = "logger created per message"
     Pattern = 'CreateLogger'; CaseSensitive = $true }
)

$total = 0
foreach ($file in $Files) {
  if (-not (Test-Path $file)) { "MISSING  $file"; continue }
  $hits = 0
  foreach ($gate in $gates) {
    $matches =
      if ($gate.Simple) {
        Select-String -Path $file -Pattern $gate.Pattern -SimpleMatch
      } else {
        Select-String -Path $file -Pattern $gate.Pattern -CaseSensitive:$gate.CaseSensitive
      }
    if ($gate.Filter) { $matches = $matches | Where-Object $gate.Filter }
    foreach ($m in $matches) {
      $hits++
      "{0}:{1}: [{2}] {3}" -f $file, $m.LineNumber, $gate.Name, $m.Line.Trim()
    }
  }
  "{0}  {1} hits" -f ($hits -eq 0 ? "CLEAN    " : "FINDINGS "), $file
  $total += $hits
}
""
"{0} total hits — every hit is explained or fixed before verify passes" -f $total
