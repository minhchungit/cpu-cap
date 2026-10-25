$dir = Split-Path $MyInvocation.MyCommand.Path
$log = "$dir\state.txt"
$BOOST = 'be337238-0d82-4146-a960-4f3749d470c7'
$EPP   = '36687f9e-e3a5-4dbf-b1dc-15eb381c6863'
$FMAX  = '75b0ae3f-bce0-45a7-8c89-c9611c25e100'
$TMAX  = 'bc5038f7-23e0-4960-96da-33abaf5935ec'
$TMIN  = '893dee8e-2bef-41e0-89c6-b55d0929964c'
try {
    # fan control: dam bao driver AppleSMC dang chay
    $smc = Get-Service AppleSMC -ErrorAction SilentlyContinue
    if ($smc -and $smc.Status -ne 'Running') { Start-Service AppleSMC -ErrorAction SilentlyContinue }

    $desired = "$dir\desired.json"
    if (Test-Path $desired) {
        $p = Get-Content $desired -Raw | ConvertFrom-Json
    } else {
        $cfg = Get-Content "$dir\profiles.json" -Raw | ConvertFrom-Json
        $p = $cfg.profiles | Where-Object { $_.name -eq $cfg.active } | Select-Object -First 1
    }
    if (-not $p) { throw "no profile to apply" }
    $out = @()
    $out += powercfg /setdcvalueindex SCHEME_CURRENT SUB_PROCESSOR $BOOST $p.dcBoost 2>&1
    $out += powercfg /setacvalueindex SCHEME_CURRENT SUB_PROCESSOR $BOOST $p.acBoost 2>&1
    if ($null -ne $p.dcEpp) { $out += powercfg /setdcvalueindex SCHEME_CURRENT SUB_PROCESSOR $EPP $p.dcEpp 2>&1 }
    if ($null -ne $p.acEpp) { $out += powercfg /setacvalueindex SCHEME_CURRENT SUB_PROCESSOR $EPP $p.acEpp 2>&1 }
    if ($p.dcMin) { $out += powercfg /setdcvalueindex SCHEME_CURRENT SUB_PROCESSOR $TMIN $p.dcMin 2>&1 }
    if ($p.acMin) { $out += powercfg /setacvalueindex SCHEME_CURRENT SUB_PROCESSOR $TMIN $p.acMin 2>&1 }
    # clear MHz/% caps (ignored on HWP anyway)
    $out += powercfg /setdcvalueindex SCHEME_CURRENT SUB_PROCESSOR $FMAX 0 2>&1
    $out += powercfg /setacvalueindex SCHEME_CURRENT SUB_PROCESSOR $FMAX 0 2>&1
    $out += powercfg /setdcvalueindex SCHEME_CURRENT SUB_PROCESSOR $TMAX 100 2>&1
    $out += powercfg /setacvalueindex SCHEME_CURRENT SUB_PROCESSOR $TMAX 100 2>&1
    $out += powercfg /setactive SCHEME_CURRENT 2>&1
    "[$(Get-Date -Format 'HH:mm:ss')] OK '$($p.name)': pin boost=$($p.dcBoost) epp=$($p.dcEpp) | sac boost=$($p.acBoost) epp=$($p.acEpp)" | Set-Content $log -Encoding UTF8
} catch {
    "[$(Get-Date -Format 'HH:mm:ss')] ERROR: $_" | Set-Content $log -Encoding UTF8
}
