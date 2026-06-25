$ErrorActionPreference = 'Stop'

$packageName = 'llmprobe'

# The package ships a single self-contained executable extracted into the tools
# directory by Install-ChocolateyZipPackage; Chocolatey auto-shims/auto-removes
# llmprobe.exe and cleans the tools directory on uninstall. Remove the shim
# defensively in case auto-uninstall is disabled.
try {
  Uninstall-BinFile -Name $packageName
} catch {
  Write-Verbose "No Chocolatey shim to remove for $packageName."
}
