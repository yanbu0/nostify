# This script will add all found service projects to a Visual Studio Code workspace file.
# Usage: Run this script in the root folder containing the microservices projects.

# Format-Json original source:
#    https://stackoverflow.com/questions/56322993/proper-formating-of-json-using-powershell
#    https://stackoverflow.com/a/69925287
function Format-Json {
    Param(
        [psobject]$InputObject,
        [int]$Indentation = 2
    )
    $Json = $InputObject | ConvertTo-Json -Depth 10

    $indent = 0
    $regexUnlessQuoted = '(?=([^"]*"[^"]*")*[^"]*$)'

    $result = $Json -split '\r?\n' |
    ForEach-Object {
        # If the line contains a ] or } character, 
        # we need to decrement the indentation level unless it is inside quotes.
        if ($_ -match "[}\]]$regexUnlessQuoted") {

            [int[]] $indentArray = ($indent - $Indentation), 0

            if ($indentArray[0] -gt $indentArray[1]) { 
                $indent = $indentArray[0] 
            }
            else { 
                $indent = 0
            }
        }

        # Replace all colon-space combinations by ": " unless it is inside quotes.
        $line = (' ' * $indent) + ($_.TrimStart() -replace ":\s+$regexUnlessQuoted", ': ')

        # If the line contains a [ or { character, 
        # we need to increment the indentation level unless it is inside quotes.
        if ($_ -match "[\{\[]$regexUnlessQuoted") {
            $indent += $Indentation
        }

        $line
    }

    return $result -Join [Environment]::NewLine
}

function Update-WorkspaceFile {
    param (
        [string]$workspaceFilePath,
        [string]$serviceName
    )
    $workspaceContent = Get-Content $workspaceFilePath -Raw | ConvertFrom-Json

    # add service folder
    $folderPath = "$serviceName"
    $folderExists = $workspaceContent.folders | Where-Object { $_.path -eq $folderPath }
    if (-not $folderExists) {
        $workspaceContent.folders += @{"path" = $folderPath }
    }

    # add "Attach to Microservices" compound if it doesn't exist
    $compoundConfig = $workspaceContent.launch.compounds | Where-Object { $_.name -eq "Attach to Microservices" }
    if (-not $compoundConfig) {
        $compoundConfig = [PSCustomObject]@{
            name           = "Attach to Microservices"
            configurations = @()
        }
        $workspaceContent.launch.compounds += $compoundConfig
    }

    # add service launch name to the compound if it doesn't exist
    if (-not ($compoundConfig.configurations -contains "Attach to $serviceName")) {
        $compoundConfig.configurations += "Attach to $serviceName"
        Write-Output "Added configuration for '$serviceName' to workspace."
    }
    
    # save as formatted json
    $formattedJson = Format-Json -InputObject $workspaceContent
    Set-Content -Path $workspaceFilePath -Value $formattedJson -Force
}

function New-WorkspaceFile {
    param (
        [string]$workspaceFilePath
    )

    $workspaceContent = [PSCustomObject]@{
        folders  = @()
        settings = [PSCustomObject]@{
            "debug.internalConsoleOptions"                  = "neverOpen"
            "dotnet.automaticallyCreateSolutionInWorkspace" = $false
        }
        launch   = [PSCustomObject]@{
            configurations = @()
            compounds      = @(
                [PSCustomObject]@{
                    name           = "Attach to Microservices"
                    configurations = @()
                }
            )
        }
    }
    $formattedJson = Format-Json -InputObject $workspaceContent
    Set-Content -Path $workspaceFilePath -Value $formattedJson -Force
    Write-Output "New workspace file created."
}

function New-SolutionFile {
    param (
        [string]$solutionFile
    )
    $solutionGuid = [guid]::NewGuid().ToString()

    $solutionContent = @"
Microsoft Visual Studio Solution File, Format Version 12.00
# Visual Studio Version 17
VisualStudioVersion = 17.0.0.0
MinimumVisualStudioVersion = 10.0.40219.1
Global
    GlobalSection(SolutionConfigurationPlatforms) = preSolution
        Debug|Any CPU = Debug|Any CPU
        Release|Any CPU = Release|Any CPU
    EndGlobalSection
    GlobalSection(ProjectConfigurationPlatforms) = postSolution
    EndGlobalSection
    GlobalSection(SolutionProperties) = preSolution
        HideSolutionNode = FALSE
    EndGlobalSection
    GlobalSection(ExtensibilityGlobals) = postSolution
        SolutionGuid = {$solutionGuid}
    EndGlobalSection
EndGlobal
"@

    Set-Content $solutionFile -Value $solutionContent

    Write-Output "Solution file created successfully: $solutionFile"
}

function Add-ProjectToSolution {
    param (
        [string]$solutionFile,
        [string]$projectFile,
        [string]$projectName
    )
    # check solution file exists, create if it doesn't
    if (-Not (Test-Path $solutionFile)) {
        Write-Output "Creating new solution file: $solutionFile"
        New-SolutionFile -SolutionFile $solutionFile
    }

    # read the solution file
    $solutionContent = Get-Content $solutionFile

    # check if the project is in the solution
    if ($solutionContent -join "`n" -match [regex]::Escape($projectFile)) {
        return
    }

    $projectGuid = [guid]::NewGuid().ToString()
    $projectEntry = @"
Project("{$projectGuid}") = "$projectName", "$projectFile", "{$projectGuid}"
EndProject
"@

    # find the Global section
    $globalSectionIndex = $solutionContent.IndexOf("Global")
    if ($globalSectionIndex -lt 0) {
        Write-Error "Global section not found in the solution file"
        return
    }

    # insert the project entry before the Global section
    $solutionContent = $solutionContent[0..($globalSectionIndex - 1)] + $projectEntry + $solutionContent[$globalSectionIndex..($solutionContent.Length - 1)]

    # find the ProjectConfigurationPlatforms section - need to use a regex over each line to match without leading indentation
    $projectConfigSectionIndex = -1
    for ($i = 0; $i -lt $solutionContent.Length; $i++) {
        if ($solutionContent[$i] -match "^\s*GlobalSection\(ProjectConfigurationPlatforms\) = postSolution") {
            $projectConfigSectionIndex = $i
            break
        }
    }
    if ($projectConfigSectionIndex -lt 0) {
        Write-Error "ProjectConfigurationPlatforms section not found in the solution file"
        return
    }

    # project configurations
    $projectConfigurations = @"
        {$projectGuid}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
        {$projectGuid}.Debug|Any CPU.Build.0 = Debug|Any CPU
        {$projectGuid}.Release|Any CPU.ActiveCfg = Release|Any CPU
        {$projectGuid}.Release|Any CPU.Build.0 = Release|Any CPU
"@

    # insert the project configurations before the EndGlobalSection of the ProjectConfigurationPlatforms section
    $endProjectConfigSectionIndex = $solutionContent[$projectConfigSectionIndex..($solutionContent.Length - 1)].IndexOf("EndGlobalSection") + $projectConfigSectionIndex
    $solutionContent = $solutionContent[0..($endProjectConfigSectionIndex - 1)] + $projectConfigurations + $solutionContent[$endProjectConfigSectionIndex..($solutionContent.Length - 1)]

    # write the updated solution file
    Set-Content $SolutionFile -Value $solutionContent

    Write-Output "Added '$projectName' to solution"
}

$rootFolder = Get-Location

# get all service folders containing a %ServiceName%_Service.csproj file
$serviceProjects = Get-ChildItem -Path $rootFolder -Recurse -Filter "*_Service.csproj" | ForEach-Object {
    $projectName = $_.BaseName -replace "_Service", ""
    [PSCustomObject]@{
        Path = $_.DirectoryName
        Name = $projectName
    }
}

# get the .code-workspace file in the root folder
$workspaceFile = Get-ChildItem -Path $rootFolder -Filter "*.code-workspace" -Force | Select-Object -First 1

if (-not $workspaceFile) {
    # createa default workspace file
    $workspaceFilePath = Join-Path $rootFolder "Microservices.code-workspace"
    New-WorkspaceFile -workspaceFilePath $workspaceFilePath
}
else {
    $workspaceFilePath = $workspaceFile.FullName
}

# get the .sln file in the root folder
$solutionFile = Get-ChildItem -Path $rootFolder -Filter "*.sln" -Force | Select-Object -First 1

if (-not $solutionFile) {
    # create a default solution file
    $solutionFilePath = Join-Path $rootFolder "Microservices.sln"
}
else {
    $solutionFilePath = $solutionFile.FullName
}

# update the workspace file with found services
foreach ($serviceProject in $serviceProjects) {
    Update-WorkspaceFile -workspaceFilePath $workspaceFilePath -serviceName $serviceProject.Name
}

# update the solution file with found services
foreach ($serviceProject in $serviceProjects) {
    $projectFile = Join-Path $serviceProject.Path "$($serviceProject.Name)_Service.csproj"
    Add-ProjectToSolution -solutionFile $solutionFilePath -projectFile $projectFile -projectName $serviceProject.Name
}
