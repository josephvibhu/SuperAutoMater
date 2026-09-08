param(
    [string]$Message = ""
)

$ErrorActionPreference = "Stop"

Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "    AUTOMATER DIAGNOSTIC TOOL — GITHUB SYNC ENGINE          " -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host ""

# Verify git
if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    Write-Host "[ERROR] Git is not installed or not in PATH." -ForegroundColor Red
    exit 1
}

# Verify / set remote
$targetRemote = "https://github.com/josephvibhu/SuperAutoMater.git"
$currentRemote = git remote get-url origin 2>$null
if ($LASTEXITCODE -ne 0 -or -not $currentRemote) {
    Write-Host "[INFO] Setting up git remote 'origin' -> $targetRemote" -ForegroundColor Yellow
    git remote add origin $targetRemote
} elseif ($currentRemote -ne $targetRemote) {
    Write-Host "[INFO] Updating git remote 'origin' -> $targetRemote" -ForegroundColor Yellow
    git remote set-url origin $targetRemote
}

# Check status
Write-Host "[STATUS] Inspecting repository state..." -ForegroundColor Cyan
$status = git status --porcelain
if (-not $status) {
    Write-Host "[INFO] Working tree is clean. Checking for remote updates..." -ForegroundColor Green
    try {
        git fetch origin main 2>$null
        git pull --rebase origin main
    } catch { }
    Write-Host "[DONE] Everything up to date!" -ForegroundColor Green
    exit 0
}

Write-Host "Modified / untracked files:" -ForegroundColor Yellow
git status -s

if ([string]::IsNullOrWhiteSpace($Message)) {
    $defaultMsg = "Update AutoMater v6.4 source code [$(Get-Date -Format 'yyyy-MM-dd HH:mm')]"
    $inputMsg = Read-Host "`nEnter commit message (press Enter for default: '$defaultMsg')"
    if ([string]::IsNullOrWhiteSpace($inputMsg)) {
        $Message = $defaultMsg
    } else {
        $Message = $inputMsg
    }
}

Write-Host "`n[1/3] Staging changes..." -ForegroundColor Cyan
git add -A

Write-Host "[2/3] Committing changes: '$Message'..." -ForegroundColor Cyan
git commit -m "$Message"

Write-Host "[3/3] Pushing to GitHub (origin main)..." -ForegroundColor Cyan
try {
    git push -u origin main
    if ($LASTEXITCODE -eq 0) {
        Write-Host "`n============================================================" -ForegroundColor Green
        Write-Host " [SUCCESS] Synced cleanly to GitHub: $targetRemote" -ForegroundColor Green
        Write-Host "============================================================" -ForegroundColor Green
    } else {
        Write-Host "`n[NOTICE] Push completed with notice. Verify remote repository permissions." -ForegroundColor Yellow
    }
} catch {
    Write-Host "`n[ERROR] Push failed: $_" -ForegroundColor Red
    Write-Host "Tip: Ensure you have write access to $targetRemote or pull latest changes." -ForegroundColor Yellow
}
