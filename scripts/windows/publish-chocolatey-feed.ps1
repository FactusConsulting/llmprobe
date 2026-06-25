param(
    [Parameter(Mandatory=$true)]
    [string]$PackagePath,

    [string]$Repository = $env:GITHUB_REPOSITORY,
    [string]$Token = $(if ($env:GH_TOKEN) { $env:GH_TOKEN } else { $env:GITHUB_TOKEN }),
    [string]$FeedBranch = "gh-pages",
    [string]$FeedPath = "chocolatey",
    [string]$BaseUri = ""
)

$ErrorActionPreference = "Stop"

# Disable PowerShell's native-command error coupling so our explicit $LASTEXITCODE checks
# stay in charge: some native calls use a non-zero exit as a normal signal rather than a
# failure (e.g. `git diff --cached --quiet` returns 1 when there ARE staged changes, and
# `git rm -rf .` on a fresh orphan branch returns non-zero with nothing to remove). With
# $ErrorActionPreference = "Stop" those would otherwise throw on PowerShell 7.4+.
#
# $PSNativeCommandUseErrorActionPreference is a *preference* variable (designed to be set),
# not an automatic variable, and it was only introduced in PowerShell 7.3. We guard with
# Get-Variable so the script still runs on older PowerShell where the variable does not exist.
if (Get-Variable -Name PSNativeCommandUseErrorActionPreference -ErrorAction SilentlyContinue) {
    $PSNativeCommandUseErrorActionPreference = $false
}

if (-not $Repository) {
    throw "Repository is required. Set GITHUB_REPOSITORY or pass -Repository owner/name."
}

$package = Get-Item $PackagePath
if (-not $package) {
    throw "Chocolatey package not found: $PackagePath"
}

$repoParts = $Repository.Split("/")
if ($repoParts.Count -ne 2) {
    throw "Repository must be owner/name, got: $Repository"
}

if (-not $BaseUri) {
    $owner = $repoParts[0].ToLowerInvariant()
    $repoName = $repoParts[1]
    $BaseUri = "https://$owner.github.io/$repoName/$FeedPath/"
}
if (-not $BaseUri.EndsWith("/")) {
    $BaseUri = "$BaseUri/"
}

$workRoot = Join-Path ([System.IO.Path]::GetTempPath()) "llmprobe-chocolatey-feed-$([guid]::NewGuid())"
$toolPath = Join-Path $workRoot ".tools"
$feedRoot = Join-Path $workRoot "pages"
$feedDir = Join-Path $feedRoot $FeedPath
$configPath = Join-Path $workRoot "sleet.json"

New-Item -ItemType Directory -Force $workRoot | Out-Null

try {
    dotnet tool install --tool-path $toolPath Sleet --version "7.*"
    if ($LASTEXITCODE -ne 0) { throw "dotnet tool install Sleet failed" }
    $sleet = Join-Path $toolPath "sleet"
    if ($IsWindows) {
        $sleet = Join-Path $toolPath "sleet.exe"
    }

    $repoUrl = "https://github.com/$Repository.git"
    if ($Token) {
        $repoUrl = "https://x-access-token:$Token@github.com/$Repository.git"
    }

    git clone --depth 1 --branch $FeedBranch $repoUrl $feedRoot
    if ($LASTEXITCODE -ne 0) {
        Remove-Item $feedRoot -Recurse -Force -ErrorAction SilentlyContinue
        git clone --depth 1 $repoUrl $feedRoot
        if ($LASTEXITCODE -ne 0) { throw "git clone failed" }
        git -C $feedRoot checkout --orphan $FeedBranch
        if ($LASTEXITCODE -ne 0) { throw "git checkout --orphan $FeedBranch failed" }
        git -C $feedRoot rm -rf . 2>$null
    }

    New-Item -ItemType Directory -Force $feedDir | Out-Null
    New-Item -ItemType File -Force (Join-Path $feedRoot ".nojekyll") | Out-Null

    @{
        username = "github-actions[bot]"
        useremail = "41898282+github-actions[bot]@users.noreply.github.com"
        sources = @(
            @{
                name = "githubPages"
                type = "local"
                path = $feedDir
                baseURI = $BaseUri
            }
        )
    } | ConvertTo-Json -Depth 5 | Set-Content $configPath -Encoding utf8

    if (-not (Test-Path (Join-Path $feedDir "index.json"))) {
        & $sleet init --config $configPath --source githubPages
        if ($LASTEXITCODE -ne 0) { throw "sleet init failed" }
    }

    & $sleet push --config $configPath --source githubPages --force $package.FullName
    if ($LASTEXITCODE -ne 0) { throw "sleet push failed" }

    & $sleet validate --config $configPath --source githubPages
    if ($LASTEXITCODE -ne 0) { throw "sleet validate failed" }

    git -C $feedRoot config user.name "github-actions[bot]"
    git -C $feedRoot config user.email "41898282+github-actions[bot]@users.noreply.github.com"
    git -C $feedRoot add .nojekyll
    git -C $feedRoot add $FeedPath
    git -C $feedRoot diff --cached --quiet
    if ($LASTEXITCODE -eq 0) {
        Write-Host "Chocolatey feed already up to date at $BaseUri"
        exit 0
    }

    git -C $feedRoot commit -m "chore: publish chocolatey package $($package.BaseName)"
    if ($LASTEXITCODE -ne 0) { throw "git commit failed" }
    git -C $feedRoot push origin "HEAD:$FeedBranch"
    if ($LASTEXITCODE -ne 0) { throw "git push $FeedBranch failed" }

    Write-Host "Published Chocolatey feed package to $BaseUri"
}
finally {
    Remove-Item $workRoot -Recurse -Force -ErrorAction SilentlyContinue
}
