$ErrorActionPreference = "Continue"
$key = "sk-cqnuhdqptl0bpeu83kk5t2oackg9t8he9qvwn6ooiad7oz6r"
$h = @{ "Authorization" = "Bearer $key"; "Content-Type" = "application/json" }
$url = "https://api.xiaomimimo.com/v1/chat/completions"

# A: minimal ping - measures network + queue + first-token time
$sw = [Diagnostics.Stopwatch]::StartNew()
$bodyA = @{ model = "mimo-v2.5"; max_tokens = 1; messages = @(@{ role = "user"; content = "ping" }) } | ConvertTo-Json -Depth 5
try {
    $r = Invoke-RestMethod -Uri $url -Method Post -Headers $h -Body $bodyA -TimeoutSec 120
    $sw.Stop()
    "A minimal(max_tokens=1): $($sw.ElapsedMilliseconds) ms"
} catch {
    $sw.Stop()
    "A failed: $($sw.ElapsedMilliseconds) ms $($_.Exception.Message)"
}

# B: realistic check request - measures generation speed
$sw2 = [Diagnostics.Stopwatch]::StartNew()
$sys = "You are a construction review expert. Output only one JSON object."
$usr = "Sheet note says cable penetration seal fire resistance >= 0.50h. Code clause 11.4.2 requires >= 1.00h. Return JSON: results array with clause_number, verdict (OK/BAD/NA/UNKNOWN), evidence, analysis, suggestion."
$bodyB = @{ model = "mimo-v2.5"; max_tokens = 1200; temperature = 0.3; messages = @(@{ role = "system"; content = $sys }, @{ role = "user"; content = $usr }) } | ConvertTo-Json -Depth 5
try {
    $r2 = Invoke-RestMethod -Uri $url -Method Post -Headers $h -Body $bodyB -TimeoutSec 120
    $sw2.Stop()
    "B check(max_tokens=1200): $($sw2.ElapsedMilliseconds) ms"
    "  out_len=$($r2.choices[0].message.content.Length) finish=$($r2.choices[0].finish_reason) completion_tokens=$($r2.usage.completion_tokens)"
} catch {
    $sw2.Stop()
    "B failed: $($sw2.ElapsedMilliseconds) ms $($_.Exception.Message)"
}
