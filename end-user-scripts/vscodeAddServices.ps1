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

# update the workspace file with found services
foreach ($serviceProject in $serviceProjects) {
    Update-WorkspaceFile -workspaceFilePath $workspaceFilePath -serviceName $serviceProject.Name
}
