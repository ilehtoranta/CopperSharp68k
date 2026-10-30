param(
	[Parameter(Mandatory = $true)]
	[string] $CoreLibAssembly,

	[Parameter(Mandatory = $true)]
	[string] $OutputDirectory
)

$ErrorActionPreference = 'Stop'
$expectedSha256 = 'dc1945de746f94987ec705a1f27d512abf72a41a1da37e414d1951e4e823037a'
$sourcePath = (Resolve-Path -LiteralPath $CoreLibAssembly).Path
$outputRoot = [System.IO.Path]::GetFullPath($OutputDirectory)
[System.IO.Directory]::CreateDirectory($outputRoot) | Out-Null
$assemblyPath = Join-Path $outputRoot 'System.Private.CoreLib.dll'

if ([System.IO.Path]::GetFullPath($sourcePath) -ne [System.IO.Path]::GetFullPath($assemblyPath)) {
	if (Test-Path -LiteralPath $assemblyPath -PathType Leaf) {
		$existingHash = (Get-FileHash -LiteralPath $assemblyPath -Algorithm SHA256).Hash.ToLowerInvariant()
		if ($existingHash -ne $expectedSha256) {
			throw "Refusing to replace existing '$assemblyPath' because its SHA-256 is not the pinned .NET 10.0.9 CoreLib hash."
		}
	} else {
		Copy-Item -LiteralPath $sourcePath -Destination $assemblyPath
	}
}

$sha256 = (Get-FileHash -LiteralPath $assemblyPath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($sha256 -ne $expectedSha256) {
	throw "CoreLib input hash '$sha256' does not match the pinned .NET 10.0.9 hash '$expectedSha256'."
}
$identity = [System.Reflection.AssemblyName]::GetAssemblyName($assemblyPath)
$publicKeyToken = [System.Convert]::ToHexString($identity.GetPublicKeyToken()).ToLowerInvariant()
if ($identity.Name -ne 'System.Private.CoreLib' -or
	$identity.Version.ToString() -ne '10.0.0.0' -or
	$publicKeyToken -ne '7cec85d7bea7798e') {
	throw "Pinned CoreLib assembly identity mismatch: $($identity.FullName)."
}

$manifest = [ordered] @{
	schemaVersion = 1
	packId = 'Microsoft.NETCore.App.Runtime.win-x64'
	packVersion = '10.0.9'
	runtimeIdentifier = 'win-x64'
	targetFramework = 'net10.0'
	referencePack = 'Microsoft.NETCore.App.Ref'
	referencePackVersion = '10.0.9'
	implementationProfile = 'corelib-common-il-v1'
	assemblies = @([ordered] @{
		name = 'System.Private.CoreLib'
		file = 'System.Private.CoreLib.dll'
		version = '10.0.0.0'
		publicKeyToken = '7cec85d7bea7798e'
		mvid = '62eafe31-9268-4ab3-97e6-d291cf7a762b'
		sha256 = $sha256
	})
}

$manifestPath = Join-Path $outputRoot 'corelib-pack.json'
$temporaryPath = $manifestPath + ".$( [Guid]::NewGuid().ToString('N') ).tmp"
try {
	$json = ConvertTo-Json -InputObject $manifest -Depth 10
	[System.IO.File]::WriteAllText($temporaryPath, $json, [System.Text.UTF8Encoding]::new($false))
	[System.IO.File]::Move($temporaryPath, $manifestPath, $true)
} finally {
	if (Test-Path -LiteralPath $temporaryPath) {
		Remove-Item -LiteralPath $temporaryPath -Force
	}
}

Write-Host "Wrote pinned CoreLib 10.0.9 manifest to $manifestPath"
