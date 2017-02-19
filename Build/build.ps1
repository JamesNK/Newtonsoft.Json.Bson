properties { 
  $zipFileName = "JsonBson10r1.zip"
  $majorVersion = "1.0"
  $majorWithReleaseVersion = "1.0.1"
  $nugetPrerelease = "beta1"
  $version = GetVersion $majorWithReleaseVersion
  $packageId = "Newtonsoft.Json.Bson"
  $signAssemblies = $false
  $signKeyPath = "C:\Development\Releases\newtonsoft.snk"
  $buildNuGet = $true
  $treatWarningsAsErrors = $false
  $workingName = if ($workingName) {$workingName} else {"Working"}
  $netCliVersion = "1.0.0-rc4-004771"
  $nugetUrl = "http://dist.nuget.org/win-x86-commandline/latest/nuget.exe"
  
  $baseDir  = resolve-path ..
  $buildDir = "$baseDir\Build"
  $sourceDir = "$baseDir\Src"
  $toolsDir = "$baseDir\Tools"
  $docDir = "$baseDir\Doc"
  $releaseDir = "$baseDir\Release"
  $workingDir = "$baseDir\$workingName"
  $workingSourceDir = "$workingDir\Src"
  $nugetPath = "$buildDir\nuget.exe"
  $builds = @(
    @{Name = "Newtonsoft.Json.Bson"; TestsName = "Newtonsoft.Json.Bson.Tests"; BuildFunction = "NetCliBuild"; TestsFunction = "NetCliTests"; NuGetDir = "net45,netstandard1.0,netstandard1.1"; Framework=$null; Enabled=$true},
    @{Name = "Newtonsoft.Json.Bson.Net45"; TestsName = "Newtonsoft.Json.Bson.Tests.Net45"; BuildFunction = $null; TestsFunction = "NUnitTests"; NuGetDir = "net45"; Framework="net-4.0"; Enabled=$true}
  )
}

framework '4.6x86'

task default -depends Test

# Ensure a clean working directory
task Clean {
  Write-Host "Setting location to $baseDir"
  Set-Location $baseDir
  
  if (Test-Path -path $workingDir)
  {
    Write-Host "Deleting existing working directory $workingDir"
    
    Execute-Command -command { del $workingDir -Recurse -Force }
  }
  
  Write-Host "Creating working directory $workingDir"
  New-Item -Path $workingDir -ItemType Directory
}

# Build each solution, optionally signed
task Build -depends Clean {
  EnsureNuGetExists

  Write-Host "Copying source to working source directory $workingSourceDir"
  robocopy $sourceDir $workingSourceDir /MIR /NP /XD bin obj TestResults AppPackages $packageDirs .vs artifacts /XF *.suo *.user *.lock.json | Out-Default

  Write-Host -ForegroundColor Green "Updating assembly version"
  Write-Host
  Update-AssemblyInfoFiles $workingSourceDir ($majorVersion + '.0.0') $version

  Write-Host -ForegroundColor Green "Updating package version"
  Write-Host

  $projectPath = "$workingSourceDir\Newtonsoft.Json.Bson\Newtonsoft.Json.Bson.csproj"

  $xml = [xml](Get-Content $projectPath)
  Edit-XmlNodes -doc $xml -xpath "/Project/PropertyGroup/VersionPrefix/text()" -value $majorWithReleaseVersion
  Edit-XmlNodes -doc $xml -xpath "/Project/PropertyGroup/VersionSuffix/text()" -value $nugetPrerelease

  Write-Host $xml.OuterXml

  $xml.save($projectPath)

  foreach ($build in $builds)
  {
    $name = $build.Name
    $enabled = $build.Enabled
    if ($build.BuildFunction -ne $null)
    {
      Write-Host -ForegroundColor Green "Building " $name
      Write-Host -ForegroundColor Green "Signed " $signAssemblies
      Write-Host -ForegroundColor Green "Key " $signKeyPath
      Write-Host -ForegroundColor Green "Enabled " $enabled

      if ($enabled)
      {
        & $build.BuildFunction $build
      }
    }
  }
}

# Optional build documentation, add files to final zip
task Package -depends Build {
  foreach ($build in $builds)
  {
    $name = $build.TestsName
    $finalDirs = $build.NuGetDir.Split(",")
    $enabled = $build.Enabled

    if ($enabled)
    {
      foreach ($finalDir in $finalDirs)
      {
        robocopy "$workingSourceDir\Newtonsoft.Json.Bson\bin\Release\$finalDir" $workingDir\Package\Bin\$finalDir *.dll *.pdb *.xml /NFL /NDL /NJS /NC /NS /NP /XO /XF *.CodeAnalysisLog.xml Newtonsoft.Json.dll | Out-Default
      }
    }
  }
  
  if ($buildNuGet)
  {
    New-Item -Path $workingDir\NuGet -ItemType Directory

    exec { dotnet pack $workingSourceDir\Newtonsoft.Json.Bson\Newtonsoft.Json.Bson.csproj --no-build --configuration Release --output $workingDir\NuGet }
    #exec { dotnet pack $workingSourceDir\Newtonsoft.Json.Bson\Newtonsoft.Json.Bson.csproj --no-build --configuration Release --output $workingDir\NuGet --include-symbols --include-source }
  }
  
  Copy-Item -Path $baseDir\LICENSE.md -Destination $workingDir\Package\

  robocopy $workingSourceDir $workingDir\Package\Source\Src /MIR /NFL /NDL /NJS /NC /NS /NP /XD bin obj TestResults AppPackages .vs artifacts /XF *.suo *.user *.lock.json | Out-Default
  robocopy $buildDir $workingDir\Package\Source\Build /MIR /NFL /NDL /NJS /NC /NS /NP /XF runbuild.txt nuget.exe | Out-Default
  robocopy $toolsDir $workingDir\Package\Source\Tools /MIR /NFL /NDL /NJS /NC /NS /NP | Out-Default
  
  exec { .\Tools\7-zip\7za.exe a -tzip $workingDir\$zipFileName $workingDir\Package\* | Out-Default } "Error zipping"
}

# Unzip package to a location
task Deploy -depends Package {
  exec { .\Tools\7-zip\7za.exe x -y "-o$workingDir\Deployed" $workingDir\$zipFileName | Out-Default } "Error unzipping"
}

# Run tests on deployed files
task Test -depends Deploy {

  foreach ($build in $builds)
  {
    if ($build.Enabled -and $build.TestsFunction -ne $null)
    {
      & $build.TestsFunction $build
    }
  }
}

function EnsureNuGetExists()
{
  if (!(Test-Path $nugetPath))
  {
    Write-Host "Couldn't find nuget.exe. Downloading from $nugetUrl to $nugetPath"
    (New-Object System.Net.WebClient).DownloadFile($nugetUrl, $nugetPath)
  }
}

function NetCliBuild($build)
{
  $name = $build.Name
  $framework = $build.NuGetDir
  $projectPath = "$workingSourceDir\Newtonsoft.Json.Bson.sln"
  $location = "$workingSourceDir\Newtonsoft.Json.Bson"
  $additionalConstants = switch($signAssemblies) { $true { "SIGNED" } default { "" } }

  exec { .\Tools\Dotnet\dotnet-install.ps1 -Version $netCliVersion | Out-Default }

  try
  {
    Set-Location $location

    exec { dotnet --version | Out-Default }

    Write-Host -ForegroundColor Green "Restoring packages for $name"
    Write-Host

    exec { dotnet restore $projectPath }

    Write-Host -ForegroundColor Green "Building for $name"
    Write-Host

    exec { dotnet build $projectPath "/p:Configuration=Release" "/p:AssemblyOriginatorKeyFile=$signKeyPath" "/p:SignAssembly=$signAssemblies" "/p:TreatWarningsAsErrors=$treatWarningsAsErrors" "/p:AdditionalConstants=$additionalConstants" }
  }
  finally
  {
    Set-Location $baseDir
  }
}

function NetCliTests($build)
{
  $name = $build.TestsName
  $projectPath = "$workingSourceDir\Newtonsoft.Json.Bson.Tests\Newtonsoft.Json.Bson.Tests.csproj"
  $location = "$workingSourceDir\Newtonsoft.Json.Bson.Tests"

  exec { .\Tools\Dotnet\dotnet-install.ps1 -Version $netCliVersion | Out-Default }

  try
  {
    Set-Location $location

    exec { dotnet --version | Out-Default }

    Write-Host -ForegroundColor Green "Ensuring test project builds for $name"
    Write-Host

    exec { dotnet test $projectPath -f netcoreapp1.0 -c Release -l trx | Out-Default }
    copy-item -Path "$location\TestResults\*.trx" -Destination $workingDir
  }
  finally
  {
    Set-Location $baseDir
  }
}

function NUnitTests($build)
{
  $name = $build.TestsName
  $finalDir = $build.NuGetDir
  $framework = $build.Framework

  Write-Host -ForegroundColor Green "Copying test assembly $name to deployed directory"
  Write-Host
  robocopy "$workingSourceDir\Newtonsoft.Json.Bson.Tests\bin\Release\$finalDir" $workingDir\Deployed\Bin\$finalDir /MIR /NFL /NDL /NJS /NC /NS /NP /XO | Out-Default

  Copy-Item -Path "$workingSourceDir\Newtonsoft.Json.Bson.Tests\bin\Release\$finalDir\Newtonsoft.Json.Bson.Tests.dll" -Destination $workingDir\Deployed\Bin\$finalDir\

  Write-Host -ForegroundColor Green "Running NUnit tests " $name
  Write-Host
  exec { .\Tools\NUnit\nunit-console.exe "$workingDir\Deployed\Bin\$finalDir\Newtonsoft.Json.Bson.Tests.dll" /framework=$framework /xml:$workingDir\$name.xml | Out-Default } "Error running $name tests"
}

function GetVersion($majorVersion)
{
    $now = [DateTime]::Now
    
    $year = $now.Year - 2000
    $month = $now.Month
    $totalMonthsSince2000 = ($year * 12) + $month
    $day = $now.Day
    $minor = "{0}{1:00}" -f $totalMonthsSince2000, $day
    
    $hour = $now.Hour
    $minute = $now.Minute
    $revision = "{0:00}{1:00}" -f $hour, $minute
    
    return $majorVersion + "." + $minor
}

function Update-AssemblyInfoFiles ([string] $workingSourceDir, [string] $assemblyVersionNumber, [string] $fileVersionNumber)
{
    $assemblyVersionPattern = 'AssemblyVersion\("[0-9]+(\.([0-9]+|\*)){1,3}"\)'
    $fileVersionPattern = 'AssemblyFileVersion\("[0-9]+(\.([0-9]+|\*)){1,3}"\)'
    $assemblyVersion = 'AssemblyVersion("' + $assemblyVersionNumber + '")';
    $fileVersion = 'AssemblyFileVersion("' + $fileVersionNumber + '")';
    
    Get-ChildItem -Path $workingSourceDir -r -filter AssemblyInfo.cs | ForEach-Object {
        
        $filename = $_.Directory.ToString() + '\' + $_.Name
        Write-Host $filename
        $filename + ' -> ' + $version
    
        (Get-Content $filename) | ForEach-Object {
            % {$_ -replace $assemblyVersionPattern, $assemblyVersion } |
            % {$_ -replace $fileVersionPattern, $fileVersion }
        } | Set-Content $filename
    }
}

function Edit-XmlNodes {
    param (
        [xml] $doc,
        [string] $xpath = $(throw "xpath is a required parameter"),
        [string] $value = $(throw "value is a required parameter")
    )
    
    $nodes = $doc.SelectNodes($xpath)
    $count = $nodes.Count

    Write-Host "Found $count nodes with path '$xpath'"
    
    foreach ($node in $nodes) {
        if ($node -ne $null) {
            if ($node.NodeType -eq "Element")
            {
                $node.InnerXml = $value
            }
            else
            {
                $node.Value = $value
            }
        }
    }
}

function Execute-Command($command) {
    $currentRetry = 0
    $success = $false
    do {
        try
        {
            & $command
            $success = $true
        }
        catch [System.Exception]
        {
            if ($currentRetry -gt 5) {
                throw $_.Exception.ToString()
            } else {
                write-host "Retry $currentRetry"
                Start-Sleep -s 1
            }
            $currentRetry = $currentRetry + 1
        }
    } while (!$success)
}