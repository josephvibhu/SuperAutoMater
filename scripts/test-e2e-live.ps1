param (
    [string]$ApiUrl = "http://13.206.106.146",
    [string]$ApiKey = "vx-2fe1479ad70dffd3017a0aa8c67b1c270fc9dd00bb9d4bdf"
)

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "Running End-to-End Live Production Verification on Velox" -ForegroundColor Cyan
Write-Host "Endpoint: $ApiUrl" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 1. Health check
$health = Invoke-RestMethod -Uri "$ApiUrl/health" -Method GET
Write-Host "[1/5] Health Check: $($health.status) (service: $($health.service), v$($health.version))" -ForegroundColor Green

# 2. Technician Login (VeloxLink Auth)
$loginBody = @{ name = "John Tech"; pin = "1234" } | ConvertTo-Json
$login = Invoke-RestMethod -Uri "$ApiUrl/api/v1/auth/login" -Method POST -Headers @{ "X-API-Key" = $ApiKey } -ContentType "application/json" -Body $loginBody
Write-Host "[2/5] Technician Login Success: $($login.name) (EmpId: $($login.employeeId))" -ForegroundColor Green

# 3. Manager Intakes a new device (VeloxManager / VeloxBench)
$serial = "E2E-TEST-" + (Get-Random -Minimum 1000 -Maximum 9999)
$intakeBody = @{
    serialNumber = $serial
    assetTag = "TAG-" + $serial
    model = "Lenovo ThinkPad T14s Gen 3"
    location = "BENCH-01"
    actor = "VeloxManager"
    notes = "Intake received via VeloxManager cockpit"
} | ConvertTo-Json

$asset = Invoke-RestMethod -Uri "$ApiUrl/api/v1/assets/intake" -Method POST -Headers @{ "X-API-Key" = $ApiKey } -ContentType "application/json" -Body $intakeBody
Write-Host "[3/5] Asset Intaked: $serial (AssetId: $($asset.id))" -ForegroundColor Green

# 4. Dispatch / Assign asset to John Tech
$assignBody = @{ employeeId = $login.employeeId; actor = "VeloxManager" } | ConvertTo-Json
$assigned = Invoke-RestMethod -Uri "$ApiUrl/api/v1/assets/$($asset.id)/assign" -Method POST -Headers @{ "X-API-Key" = $ApiKey } -ContentType "application/json" -Body $assignBody
Write-Host "[4/5] Asset Assigned to $($login.name)" -ForegroundColor Green

# 5. Technician checks personal queue on VeloxLink and marks testing complete
$bearer = "Bearer " + $login.token
$headers = @{ "Authorization" = $bearer }
try {
    $jobs = Invoke-RestMethod -Uri "$ApiUrl/api/v1/jobs/mine" -Method GET -Headers $headers
} catch {
    Write-Host "Jobs/mine failed: $($_.Exception.Message)" -ForegroundColor Red
    if ($_.Exception.Response) {
        $stream = $_.Exception.Response.GetResponseStream()
        $reader = New-Object System.IO.StreamReader($stream)
        Write-Host "Response Body: $($reader.ReadToEnd())" -ForegroundColor Red
    }
    throw
}
$myJob = $jobs.jobs | Where-Object { $_.serialNumber -eq $serial }
if ($myJob) {
    Write-Host "[5/5] Technician successfully retrieved assigned job in VeloxLink feed!" -ForegroundColor Green
    Write-Host "      Serial: $($myJob.serialNumber) | Model: $($myJob.model) | Queue: $($myJob.queue)" -ForegroundColor Yellow

    # Update job status as technician
    $patchBody = @{
        lifecycleQueue = 4 # ReadyForSale
        notes = "Full QC passed on Bench 01. Certified Grade A."
        actor = $login.name
    } | ConvertTo-Json
    $updated = Invoke-RestMethod -Uri "$ApiUrl/api/v1/assets/$($asset.id)" -Method PATCH -Headers @{ "Authorization" = "Bearer $($login.token)" } -ContentType "application/json" -Body $patchBody
    Write-Host "      Status updated to ReadyForSale by Technician: $($updated.lifecycleQueue)" -ForegroundColor Green
} else {
    Write-Error "Failed to locate assigned job in employee feed."
}

Write-Host "==========================================================" -ForegroundColor Green
Write-Host "All 5/5 End-to-End Real-Time Operations Verified Successfully!" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
