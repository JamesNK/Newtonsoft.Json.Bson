properties { 
  $zipFileName = "JsonBson10r3.zip"
  $majorVersion = "1.0"
  $majorWithReleaseVersion = "1.0.3"
  $nugetPrerelease = $null
  $version = GetVersion $majorWithReleaseVersion
  $packageId = "Newtonsoft.Json.Bson"
  $signAssemblies = $false
  $signKeyPath = "C:\Development\Releases\newtonsoft.snk"
  $buildNuGet = $false
  $msbuildVerbosity = 'minimal'
  $treatWarningsAsErrors = $false
  $workingName = if ($workingName) {$workingName} else {"Working"}
  $netCliChannel = "Current"
  $netCliVersion = "6.0.300"
  $nugetUrl = "https://dist.nuget.org/win-x86-commandline/latest/nuget.exe"
  
  $baseDir  = resolve-path ..
  $buildDir = "$baseDir\Build"
  $sourceDir = "$baseDir\Src"
  $docDir = "$baseDir\Doc"
  $releaseDir = "$baseDir\Release"
  $workingDir = "$baseDir\$workingName"

  $nugetPath = "$buildDir\Temp\nuget.exe"
  $vswhereVersion = "3.0.3"
  $vswherePath = "$buildDir\Temp\vswhere.$vswhereVersion"
  $nunitConsoleVersion = "3.8.0"
  $nunitConsolePath = "$buildDir\Temp\NUnit.ConsoleRunner.$nunitConsoleVersion"

  $builds = @(
    @{Framework = "netstandard2.0"; TestsFunction = "NetCliTests"; TestFramework = "net6.0"; Enabled=$true},
    @{Framework = "netstandard1.3"; TestsFunction = "NetCliTests"; TestFramework = "netcoreapp3.1"; Enabled=$true},
    @{Framework = "net45"; TestsFunction = "NUnitTests"; TestFramework = "net46"; NUnitFramework="net-4.0"; Enabled=$true}
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

  EnsureDotNetCli
  EnsureNuGetExists
  EnsureNuGetPackage "vswhere" $vswherePath $vswhereVersion
  EnsureNuGetPackage "NUnit.ConsoleRunner" $nunitConsolePath $nunitConsoleVersion

  $script:msBuildPath = GetMsBuildPath
  Write-Host "MSBuild path $script:msBuildPath"

  NetCliBuild
}

# Optional build documentation, add files to final zip
task Package -depends Build {
  foreach ($build in $script:enabledBuilds)
  {
    $finalDir = $build.Framework

    $sourcePath = "$sourceDir\Newtonsoft.Json.Bson\bin\Release\$finalDir"

    if (!(Test-Path -path $sourcePath))
    {
      throw "Could not find $sourcePath"
    }

    robocopy $sourcePath $workingDir\Package\Bin\$finalDir Newtonsoft.Json.Bson.dll Newtonsoft.Json.Bson.pdb Newtonsoft.Json.Bson.xml /NFL /NDL /NJS /NC /NS /NP /XO /XF *.CodeAnalysisLog.xml | Out-Default
  }
  
  if ($buildNuGet)
  {
    Write-Host -ForegroundColor Green "Copy NuGet package"

    mkdir $workingDir\NuGet
    move -Path $sourceDir\Newtonsoft.Json.Bson\bin\Release\*.nupkg -Destination $workingDir\NuGet
  }
  
  Copy-Item -Path $baseDir\LICENSE.md -Destination $workingDir\Package\

  robocopy $sourceDir $workingDir\Package\Source\Src /MIR /NFL /NDL /NJS /NC /NS /NP /XD bin obj TestResults AppPackages .vs artifacts /XF *.suo *.user *.lock.json | Out-Default
  robocopy $buildDir $workingDir\Package\Source\Build /MIR /NFL /NDL /NJS /NC /NS /NP /XD Temp | Out-Default
  
  Compress-Archive -Path $workingDir\Package\* -DestinationPath $workingDir\$zipFileName
}


task Test -depends Build {
  foreach ($build in $script:enabledBuilds)
  {
    Write-Host "Calling $($build.TestsFunction)"
    & $build.TestsFunction $build
  }
}

function NetCliBuild()
{
  $projectPath = "$sourceDir\Newtonsoft.Json.Bson.sln"
  $libraryFrameworks = ($script:enabledBuilds | Select-Object @{Name="Framework";Expression={$_.Framework}} | select -expand Framework) -join ";"
  $testFrameworks = ($script:enabledBuilds | Select-Object @{Name="Resolved";Expression={if ($_.TestFramework -ne $null) { $_.TestFramework } else { $_.Framework }}} | select -expand Resolved) -join ";"

  $additionalConstants = switch($signAssemblies) { $true { "SIGNED" } default { "" } }

  Write-Host -ForegroundColor Green "Restoring packages for $libraryFrameworks in $projectPath"
  Write-Host

  exec { & $script:msBuildPath "/t:restore" "/v:$msbuildVerbosity" "/p:Configuration=Release" "/p:LibraryFrameworks=`"$libraryFrameworks`"" "/p:TestFrameworks=`"$testFrameworks`"" "/m" $projectPath | Out-Default } "Error restoring $projectPath"

  Write-Host -ForegroundColor Green "Building $libraryFrameworks $assemblyVersion in $projectPath"
  Write-Host

  exec { & $script:msBuildPath "/t:build" "/v:$msbuildVerbosity" $projectPath "/p:Configuration=Release" "/p:LibraryFrameworks=`"$libraryFrameworks`"" "/p:TestFrameworks=`"$testFrameworks`"" "/p:AssemblyOriginatorKeyFile=$signKeyPath" "/p:SignAssembly=$signAssemblies" "/p:TreatWarningsAsErrors=$treatWarningsAsErrors" "/p:AdditionalConstants=$additionalConstants" "/p:GeneratePackageOnBuild=$buildNuGet" "/p:ContinuousIntegrationBuild=true" "/p:PackageId=$packageId" "/p:VersionPrefix=$majorWithReleaseVersion" "/p:VersionSuffix=$nugetPrerelease" "/p:AssemblyVersion=$assemblyVersion" "/p:FileVersion=$version" "/m" }
}

function EnsureDotnetCli()
{
  Write-Host "Downloading dotnet-install.ps1"

  # https://stackoverflow.com/questions/36265534/invoke-webrequest-ssl-fails
  [Net.ServicePointManager]::SecurityProtocol = 'TLS12'
  Invoke-WebRequest `
    -Uri "https://dot.net/v1/dotnet-install.ps1" `
    -OutFile "$buildDir\Temp\dotnet-install.ps1"

  exec { & $buildDir\Temp\dotnet-install.ps1 -Channel $netCliChannel -Version $netCliVersion | Out-Default }
  exec { & $buildDir\Temp\dotnet-install.ps1 -Channel $netCliChannel -Version '3.1.402' | Out-Default }
}

function EnsureNuGetExists()
{
  if (!(Test-Path $nugetPath))
  {
    Write-Host "Couldn't find nuget.exe. Downloading from $nugetUrl to $nugetPath"
    (New-Object System.Net.WebClient).DownloadFile($nugetUrl, $nugetPath)
  }
}

function EnsureNuGetPackage($packageName, $packagePath, $packageVersion)
{
  if (!(Test-Path $packagePath))
  {
    Write-Host "Couldn't find $packagePath. Downloading with NuGet"
    exec { & $nugetPath install $packageName -OutputDirectory $buildDir\Temp -Version $packageVersion -ConfigFile "$sourceDir\nuget.config" | Out-Default } "Error restoring $packagePath"
  }
}

function GetMsBuildPath()
{
  $path = & $vswherePath\tools\vswhere.exe -latest -products * -requires Microsoft.Component.MSBuild -property installationPath -prerelease
  Write-Host "VS path is: $packagePath"
  if (!($path))
  {
    throw "Could not find Visual Studio install path"
  }

  $msBuildPath = join-path $path 'MSBuild\15.0\Bin\MSBuild.exe'
  if (Test-Path $msBuildPath)
  {
    return $msBuildPath
  }

  $msBuildPath = join-path $path 'MSBuild\Current\Bin\MSBuild.exe'
  if (Test-Path $msBuildPath)
  {
    return $msBuildPath
  }

  throw "Could not find MSBuild path"
}

function NetCliTests($build)
{
  $projectPath = "$sourceDir\Newtonsoft.Json.Bson.Tests\Newtonsoft.Json.Bson.Tests.csproj"
  $location = "$sourceDir\Newtonsoft.Json.Bson.Tests"
  $testDir = if ($build.TestFramework -ne $null) { $build.TestFramework } else { $build.Framework }

  try
  {
    Set-Location $location

    exec { dotnet --version | Out-Default }

    Write-Host -ForegroundColor Green "Running tests for $testDir"
    Write-Host "Location: $location"
    Write-Host "Project path: $projectPath"
    Write-Host

    exec { dotnet test $projectPath -f $testDir -c Release -l trx -r $workingDir --no-restore --no-build | Out-Default }
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
  $testRunDir = "$sourceDir\Newtonsoft.Json.Bson.Tests\bin\Release\$testDir"

  Write-Host -ForegroundColor Green "Running NUnit tests $testDir"
  Write-Host
  try
  {
    Set-Location $testRunDir
    exec { & $nunitConsolePath\tools\nunit3-console.exe "$testRunDir\Newtonsoft.Json.Bson.Tests.dll" --framework=$framework --result=$workingDir\$testDir.xml --out=$workingDir\$testDir.txt | Out-Default } "Error running $testDir tests"
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