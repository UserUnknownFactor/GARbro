@powershell.exe -ExecutionPolicy Bypass -Command "$_=((Get-Content \"%~f0\") -join \"`n\");iex $_.Substring($_.IndexOf(\"goto :\"+\"EOF\")+9)" %*
@goto :EOF

# Script to increment .NET assembly revision

param(
    [Parameter(Mandatory=$false, Position=0)]
    [string]$ProjectPath,
    
    [Parameter(Mandatory=$true, Position=1)]
    [string]$Config,
    
    [Parameter(Position=2)]
    [string]$RootDir
)

function Test-Version {
    param([string]$Version)
    if ($Version -match '^(\d+)\.(\d+)(?:\.(\d+)\.(\d+))?') {
        return @($Matches[1], $Matches[2], $Matches[3], $Matches[4])
    }
    return $null
}

# Check arguments
if ($PSBoundParameters.Count -lt 2) {
    Write-Host "usage: inc-revision.ps1 PROJECT-FILE CONFIG [ROOT-DIR]"
    exit 0
}

$ProjectDir = Split-Path -Parent $ProjectPath
if (-not $RootDir) {
    $RootDir = $ProjectDir
}
$IsRelease = $Config.ToLower() -eq 'release'

# Change to root directory
try {
    Set-Location $RootDir
} catch {
    Write-Error "$RootDir: $_"
    exit 1
}

# Get git revision count
$GitExe = "git.exe"
try {
    $Revision = & $GitExe rev-list HEAD --count .
    if ($LASTEXITCODE -ne 0) {
        throw "git.exe failed"
    }
    $Revision = $Revision.Trim()
} catch {
    Write-Error $_
    exit 1
}

$PropDir = Join-Path $ProjectDir 'Properties'
$AssemblyInfo = Join-Path $PropDir 'AssemblyInfo.cs'

$VersionChanged = $false
$TmpFilename = Join-Path $PropDir ([System.IO.Path]::GetRandomFileName())

try {
    $AssemblyContent = Get-Content $AssemblyInfo -Raw
    $Lines = $AssemblyContent -split "`r?`n"
    $NewLines = @()
    
    foreach ($Line in $Lines) {
        # Skip comment lines
        if ($Line -match '^//') {
            $NewLines += $Line
            continue
        }
        
        # Check for assembly version attributes
        if ($Line -match '^\[assembly:\s*(Assembly(?:File)?Version)\s*\("(.*?)"\)\]') {
            $Attr = $Matches[1]
            $Version = $Matches[2]
            $VersionParts = Test-Version $Version
            
            if ($VersionParts) {
                $Major = $VersionParts[0]
                $Minor = $VersionParts[1]
                $Build = if ($VersionParts[2]) { [int]$VersionParts[2] } else { 0 }
                
                if ($IsRelease) {
                    $Build++
                }
                
                $NewVersion = "$Major.$Minor.$Build.$Revision"
                $Line = "[assembly: $Attr (`"$NewVersion`")]"
                
                if ($Attr -eq 'AssemblyVersion') {
                    Write-Host "AssemblyVersion: $NewVersion"
                }
                
                if ($Version -ne $NewVersion) {
                    $VersionChanged = $true
                }
            }
        }
        
        $NewLines += $Line
    }
    
    # Write to temporary file with CRLF line endings
    $NewContent = $NewLines -join "`r`n"
    [System.IO.File]::WriteAllText($TmpFilename, $NewContent, [System.Text.Encoding]::UTF8)
    
    if ($VersionChanged) {
        # Backup original and replace with new version
        Move-Item $AssemblyInfo "$AssemblyInfo~" -Force
        Move-Item $TmpFilename $AssemblyInfo -Force
    } else {
        # Remove temporary file if no changes
        Remove-Item $TmpFilename -Force -ErrorAction SilentlyContinue
    }
    
} catch {
    Write-Error $_
    # Clean up temporary file on error
    if (Test-Path $TmpFilename) {
        Remove-Item $TmpFilename -Force -ErrorAction SilentlyContinue
    }
    exit 1
}