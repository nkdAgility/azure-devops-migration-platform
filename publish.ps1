# Configuration
$configuration = "Release"
$outputRoot = ".\output"

# Project configurations
$publishConfigs = @(
    @{
        Project     = "src\TfsObjectModel\MigrationPlatform.CLI.TfsExport\MigrationPlatform.CLI.TfsExport.csproj"
        ProductName = "TfsExport"
        Rids        = @("win-x86")
        Platforms   = @{ "win-x86" = "x86" }
    },
    @{
        Project     = "src\AzureDevOps\MigrationPlatform.CLI.Migration\MigrationPlatform.CLI.Migration.csproj"
        ProductName = "DevOpsMigration"
        Rids        = @("win-x64", "win-arm64", "linux-x64", "linux-arm64")
        Platforms   = @{
            "win-x64"     = "win-x64"
            "win-arm64"   = "win-arm64"
            "linux-x64"   = "linux-x64"
            "linux-arm64" = "linux-arm64"
        }
    }
)

# Ensure output folder
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null

foreach ($config in $publishConfigs) {
    foreach ($rid in $config.Rids) {
        $platformName = $config.Platforms[$rid]
        $tempOutput = Join-Path $outputRoot "temp\$($config.ProductName)-$platformName"

        Write-Host "`nPublishing $($config.ProductName) for RID: $rid" -ForegroundColor Cyan

        # Publish project
        dotnet publish $config.Project `
            -c $configuration `
            -r $rid `
            --self-contained false `
            -o $tempOutput `
            --nologo

        if ($LASTEXITCODE -ne 0) {
            Write-Error "Publish failed: $($config.Project) for RID $rid"
            exit $LASTEXITCODE
        }

        # Extract InformationalVersion safely with try-catch
        try {
            $assemblyFile = Get-ChildItem $tempOutput -Filter "$($config.ProductName).*" | 
            Where-Object { $_.Extension -in ".dll", ".exe" } | 
            Select-Object -First 1

            if (-not $assemblyFile) {
                throw "Could not find DLL/EXE for $($config.ProductName)"
            }

            $infoVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($assemblyFile.FullName).ProductVersion

            if (-not $infoVersion) {
                throw "InformationalVersion not found for $($assemblyFile.Name)"
            }

            # Strip everything after "+" safely
            $version = if ($infoVersion.Contains('+')) { 
                $infoVersion.Split('+')[0] 
            }
            else { 
                $infoVersion 
            }
        }
        catch {
            Write-Error "Error extracting InformationalVersion: $_"
            exit 1
        }


        # Create ZIP package
        $zipOutput = Join-Path $outputRoot "$($config.ProductName)-$version-$platformName.zip"

        if (Test-Path $zipOutput) { Remove-Item $zipOutput -Force }
        Compress-Archive -Path "$tempOutput\*" -DestinationPath $zipOutput

        Write-Host "Packaged: $zipOutput" -ForegroundColor Green

        # Cleanup temporary output
        Remove-Item -Path $tempOutput -Recurse -Force
    }
    Remove-Item (Join-Path $outputRoot "temp")

}

Write-Host "`nAll projects published and packaged successfully." -ForegroundColor Green
