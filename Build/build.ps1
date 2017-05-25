﻿properties { 
  $zipFileName = "JsonBson10r2.zip"
  $majorVersion = "1.0"
  $majorWithReleaseVersion = "1.0.2"
  $nugetPrerelease = $null
  $version = GetVersion $majorWithReleaseVersion
  $packageId = "Newtonsoft.Json.Bson"
  $signAssemblies = $false
  $signKeyPath = "C:\Development\Releases\newtonsoft.snk"
  $buildNuGet = $false
  $treatWarningsAsErrors = $false
  $workingName = if ($workingName) {$workingName} else {"Working"}
  $netCliVersion = "1.0.4"
  $nugetUrl = "http://dist.nuget.org/win-x86-commandline/latest/nuget.exe"
  
  $baseDir  = resolve-path ..
  $buildDir = "$baseDir\Build"
  $sourceDir = "$baseDir\Src"
  $toolsDir = "$baseDir\Tools"
  $docDir = "$baseDir\Doc"
  $releaseDir = "$baseDir\Release"
  $workingDir = "$baseDir\$workingName"
  $workingSourceDir = "$workingDir\Src"

  $nugetPath = "$buildDir\Temp\nuget.exe"
  $vswhereVersion = "1.0.58"
  $vswherePath = "$buildDir\Temp\vswhere.$vswhereVersion"
  $nunitConsoleVersion = "3.6.1"
  $nunitConsolePath = "$buildDir\Temp\NUnit.ConsoleRunner.$nunitConsoleVersion"

  $builds = @(
    @{Framework = "netstandard1.3"; TestsFunction = "NetCliTests"; TestFramework = "netcoreapp1.1"; Enabled=$true},
    @{Framework = "net45"; TestsFunction = "NUnitTests"; NUnitFramework="net-4.0"; Enabled=$true}
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
  $script:enabledBuilds = $builds | ? {$_.Enabled}
  Write-Host -ForegroundColor Green "Found $($script:enabledBuilds.Length) enabled builds"

  mkdir "$buildDir\Temp" -Force
  EnsureNuGetExists
  EnsureNuGetPacakge "vswhere" $vswherePath $vswhereVersion
  EnsureNuGetPacakge "NUnit.ConsoleRunner" $nunitConsolePath $nunitConsoleVersion

  $script:msBuildPath = GetMsBuildPath
  Write-Host "MSBuild path $script:msBuildPath"

  Write-Host "Copying source to working source directory $workingSourceDir"
  robocopy $sourceDir $workingSourceDir /MIR /NP /XD bin obj TestResults AppPackages $packageDirs .vs artifacts /XF *.suo *.user *.lock.json | Out-Default
  Copy-Item -Path $baseDir\LICENSE.md -Destination $workingDir\

  Write-Host -ForegroundColor Green "Updating package version"
  Write-Host

  $projectPath = "$workingSourceDir\Newtonsoft.Json.Bson\Newtonsoft.Json.Bson.csproj"

  $xml = [xml](Get-Content $projectPath)
  Edit-XmlNodes -doc $xml -xpath "/Project/PropertyGroup/VersionPrefix" -value $majorWithReleaseVersion
  Edit-XmlNodes -doc $xml -xpath "/Project/PropertyGroup/VersionSuffix" -value $nugetPrerelease
  Edit-XmlNodes -doc $xml -xpath "/Project/PropertyGroup/AssemblyVersion" -value ($majorVersion + '.0.0')
  Edit-XmlNodes -doc $xml -xpath "/Project/PropertyGroup/FileVersion" -value $version

  Write-Host $xml.OuterXml

  $xml.save($projectPath)

  NetCliBuild
}

# Optional build documentation, add files to final zip
task Package -depends Build {
  foreach ($build in $script:enabledBuilds)
  {
    $finalDir = $build.Framework

    $sourcePath = "$workingSourceDir\Newtonsoft.Json.Bson\bin\Release\$finalDir"

    if (!(Test-Path -path $sourcePath))
    {
      throw "Could not find $sourcePath"
    }

    robocopy $sourcePath $workingDir\Package\Bin\$finalDir Newtonsoft.Json.Bson.dll Newtonsoft.Json.Bson.pdb Newtonsoft.Json.Bson.xml /NFL /NDL /NJS /NC /NS /NP /XO /XF *.CodeAnalysisLog.xml | Out-Default
  }
  
  if ($buildNuGet)
  {
    Write-Host -ForegroundColor Green "Creating NuGet package"

    $targetFrameworks = ($script:enabledBuilds | Select-Object @{Name="Framework";Expression={$_.Framework}} | select -expand Framework) -join ";"

    exec { & $script:msBuildPath "/t:pack" "/p:IncludeSource=true" "/p:Configuration=Release" "/p:TargetFrameworks=`"$targetFrameworks`"" "$workingSourceDir\Newtonsoft.Json.Bson\Newtonsoft.Json.Bson.csproj" }

    mkdir $workingDir\NuGet
    move -Path $workingSourceDir\Newtonsoft.Json.Bson\bin\Release\*.nupkg -Destination $workingDir\NuGet
  }
  
  Copy-Item -Path $baseDir\LICENSE.md -Destination $workingDir\Package\

  robocopy $workingSourceDir $workingDir\Package\Source\Src /MIR /NFL /NDL /NJS /NC /NS /NP /XD bin obj TestResults AppPackages .vs artifacts /XF *.suo *.user *.lock.json | Out-Default
  robocopy $buildDir $workingDir\Package\Source\Build /MIR /NFL /NDL /NJS /NC /NS /NP /XD Temp | Out-Default
  robocopy $toolsDir $workingDir\Package\Source\Tools /MIR /NFL /NDL /NJS /NC /NS /NP | Out-Default
  
  Compress-Archive -Path $workingDir\Package\* -DestinationPath $workingDir\$zipFileName
}

# Unzip package to a location
task Deploy -depends Package {
  Expand-Archive -Path $workingDir\$zipFileName -DestinationPath "$workingDir\Deployed" 
}

# Run tests on deployed files
task Test -depends Deploy {
  foreach ($build in $script:enabledBuilds)
  {
    Write-Host "Calling $($build.TestsFunction)"
    & $build.TestsFunction $build
  }
}

function NetCliBuild()
{
  $projectPath = "$workingSourceDir\Newtonsoft.Json.Bson.sln"
  $libraryFrameworks = ($script:enabledBuilds | Select-Object @{Name="Framework";Expression={$_.Framework}} | select -expand Framework) -join ";"
  $testFrameworks = ($script:enabledBuilds | Select-Object @{Name="Resolved";Expression={if ($_.TestFramework -ne $null) { $_.TestFramework } else { $_.Framework }}} | select -expand Resolved) -join ";"

  $additionalConstants = switch($signAssemblies) { $true { "SIGNED" } default { "" } }

  Write-Host -ForegroundColor Green "Restoring packages for $libraryFrameworks in $projectPath"
  Write-Host

  exec { & $script:msBuildPath "/t:restore" "/p:Configuration=Release" "/p:LibraryFrameworks=`"$libraryFrameworks`"" "/p:TestFrameworks=`"$testFrameworks`"" $projectPath | Out-Default } "Error restoring $projectPath"

  Write-Host -ForegroundColor Green "Building $libraryFrameworks in $projectPath"
  Write-Host

  exec { & $script:msBuildPath "/t:build" $projectPath "/p:Configuration=Release" "/p:LibraryFrameworks=`"$libraryFrameworks`"" "/p:TestFrameworks=`"$testFrameworks`"" "/p:AssemblyOriginatorKeyFile=$signKeyPath" "/p:SignAssembly=$signAssemblies" "/p:TreatWarningsAsErrors=$treatWarningsAsErrors" "/p:AdditionalConstants=$additionalConstants" }
}

function EnsureNuGetExists()
{
  if (!(Test-Path $nugetPath)) {
    Write-Host "Couldn't find nuget.exe. Downloading from $nugetUrl to $nugetPath"
    (New-Object System.Net.WebClient).DownloadFile($nugetUrl, $nugetPath)
  }
}

function EnsureNuGetPacakge($packageName, $packagePath, $packageVersion)
{
  if (!(Test-Path $packagePath))
  {
    Write-Host "Couldn't find $packagePath. Downloading with NuGet"
    exec { & $nugetPath install $packageName -OutputDirectory $buildDir\Temp -Version $packageVersion | Out-Default } "Error restoring $packagePath"
  }
}

function GetMsBuildPath()
{
  $path = & $vswherePath\tools\vswhere.exe -latest -products * -requires Microsoft.Component.MSBuild -property installationPath
  if (!($path)) {
    throw "Could not find Visual Studio install path"
  }
  return join-path $path 'MSBuild\15.0\Bin\MSBuild.exe'
}

function NetCliTests($build)
{
  $projectPath = "$workingSourceDir\Newtonsoft.Json.Bson.Tests\Newtonsoft.Json.Bson.Tests.csproj"
  $location = "$workingSourceDir\Newtonsoft.Json.Bson.Tests"
  $testDir = if ($build.TestFramework -ne $null) { $build.TestFramework } else { $build.Framework }

  exec { .\Tools\Dotnet\dotnet-install.ps1 -Version $netCliVersion | Out-Default }

  try
  {
    Set-Location $location

    exec { dotnet --version | Out-Default }

    Write-Host -ForegroundColor Green "Running tests for $testDir"
    Write-Host

    exec { dotnet test $projectPath -f $testDir -c Release -l trx | Out-Default }
    copy-item -Path "$location\TestResults\*.trx" -Destination $workingDir
  }
  finally
  {
    Set-Location $baseDir
  }
}

function NUnitTests($build)
{
  $testDir = if ($build.TestFramework -ne $null) { $build.TestFramework } else { $build.Framework }
  $framework = $build.NUnitFramework
  $testRunDir = "$workingDir\Deployed\Bin\$($build.Framework)"

  Write-Host -ForegroundColor Green "Copying test assembly $testDir to deployed directory"
  Write-Host
  robocopy "$workingSourceDir\Newtonsoft.Json.Bson.Tests\bin\Release\$testDir" $testRunDir /MIR /NFL /NDL /NJS /NC /NS /NP /XO | Out-Default

  Write-Host -ForegroundColor Green "Running NUnit tests $testDir"
  Write-Host
  try
  {
    Set-Location $testRunDir
    exec { & $nunitConsolePath\tools\nunit3-console.exe "$testRunDir\Newtonsoft.Json.Bson.Tests.dll" --framework=$framework --result=$workingDir\$testDir.xml | Out-Default } "Error running $testDir tests"
  }
  finally
  {
    Set-Location $baseDir
  }
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