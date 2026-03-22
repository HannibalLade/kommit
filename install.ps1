$ErrorActionPreference = "Stop"

$repo = "HannibalLade/kommit"
$rid = "win-x64"
$assetName = "kommit-$rid.exe"

Write-Host "Detecting platform: $rid"

# Get latest release
$releaseUrl = "https://api.github.com/repos/$repo/releases/latest"
$release = Invoke-RestMethod -Uri $releaseUrl -Headers @{ "User-Agent" = "kommit-installer" }

$asset = $release.assets | Where-Object { $_.name -eq $assetName } | Select-Object -First 1

if (-not $asset) {
    Write-Error "No binary found for your platform ($rid)."
    Write-Error "Check https://github.com/$repo/releases for available downloads."
    exit 1
}

# Install to user's local AppData
$installDir = "$env:LOCALAPPDATA\kommit"
if (-not (Test-Path $installDir)) {
    New-Item -ItemType Directory -Path $installDir | Out-Null
}

$installPath = "$installDir\kommit.exe"

Write-Host "Downloading kommit for $rid..."
Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $installPath

Write-Host "Installed kommit to $installPath"

# Check if install dir is in PATH
$userPath = [Environment]::GetEnvironmentVariable("Path", "User")
if ($userPath -notlike "*$installDir*") {
    [Environment]::SetEnvironmentVariable("Path", "$userPath;$installDir", "User")
    Write-Host ""
    Write-Host "Added $installDir to your PATH."
    Write-Host "Restart your terminal for the change to take effect."
}

Write-Host ""

# First-time setup
$kommitDir = "$env:USERPROFILE\.kommit"
$configFile = "$kommitDir\config.json"
if (-not (Test-Path $kommitDir)) { New-Item -ItemType Directory -Path $kommitDir | Out-Null }
if (-not (Test-Path $configFile)) {
    Write-Host "Let's configure kommit!"
    Write-Host ""

    $autoAdd = Read-Host "Automatically stage all files when none are staged? [y/N]"
    $autoAddVal = if ($autoAdd -eq "y" -or $autoAdd -eq "Y") { "true" } else { "false" }

    $autoPush = Read-Host "Automatically push after each commit? [y/N]"
    $autoPushVal = if ($autoPush -eq "y" -or $autoPush -eq "Y") { "true" } else { "false" }

    $autoPull = Read-Host "Automatically pull before each commit? [y/N]"
    $autoPullVal = if ($autoPull -eq "y" -or $autoPull -eq "Y") { "true" } else { "false" }

    $config = @"
{
  "autoAdd": $autoAddVal,
  "autoPush": $autoPushVal,
  "autoPull": $autoPullVal,
  "pullStrategy": "rebase",
  "pushStrategy": "simple",
  "defaultScope": null,
  "maxCommitLength": 72,
  "maxStagedFiles": null,
  "maxStagedLines": null,
  "apiToken": null
}
"@
    Set-Content -Path $configFile -Value $config

    Write-Host ""
    Write-Host "Config saved to ~/.kommit/config.json"
    Write-Host "You can change these anytime with 'kommit config'."
} else {
    Write-Host "Existing config found at ~/.kommit/config.json"
}

Write-Host ""
Write-Host "Run 'kommit --version' to verify."
