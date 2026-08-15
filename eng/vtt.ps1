[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet(
        "doctor",
        "bootstrap",
        "restore",
        "build",
        "test",
        "frontend",
        "verify",
        "core-up",
        "app-smoke",
        "apps-up",
        "observability-up",
        "dev-tools-up",
        "platform-up",
        "platform-test",
        "down",
        "reset",
        "images"
    )]
    [string]$Command = "doctor",

    [switch]$ConfirmReset
)

$ErrorActionPreference = "Stop"
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$composeFile = Join-Path $repositoryRoot "deploy\compose\compose.yaml"
$environmentFile = Join-Path $repositoryRoot ".env"

function Invoke-External {
    param(
        [Parameter(Mandatory)]
        [string]$Executable,

        [Parameter()]
        [string[]]$Arguments = @()
    )

    & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "'$Executable $($Arguments -join ' ')' failed with exit code $LASTEXITCODE."
    }
}

function Get-ComposeArguments {
    $arguments = @("compose")

    if (Test-Path -LiteralPath $environmentFile) {
        $arguments += @("--env-file", $environmentFile)
    }

    $arguments += @("-f", $composeFile)
    return $arguments
}

function Invoke-Compose {
    param(
        [Parameter()]
        [string[]]$Arguments = @()
    )

    Invoke-External "docker" ((Get-ComposeArguments) + $Arguments)
}

function Assert-Tooling {
    $requiredCommands = @("dotnet", "node", "pnpm", "docker")
    foreach ($requiredCommand in $requiredCommands) {
        if (-not (Get-Command $requiredCommand -ErrorAction SilentlyContinue)) {
            throw "Missing '$requiredCommand'. See docs/development/onboarding.md."
        }
    }

    $dotnetVersion = (& dotnet --version).Trim()
    if (-not $dotnetVersion.StartsWith("10.0.", [StringComparison]::Ordinal)) {
        throw "Expected .NET SDK 10.0.x, found $dotnetVersion. global.json requires 10.0.400."
    }

    Invoke-External "node" @(
        "-e",
        "const [major, minor] = process.versions.node.split('.').map(Number); if (major < 22 || major >= 25 || (major === 22 && minor < 12)) process.exit(1)"
    )

    $pnpmVersion = (& pnpm --version).Trim()
    if (-not $pnpmVersion.StartsWith("11.", [StringComparison]::Ordinal)) {
        throw "Expected pnpm 11.x, found $pnpmVersion."
    }

    Invoke-External "docker" @("compose", "version")
    Invoke-External "docker" @("info", "--format", "{{.ServerVersion}}")

    Write-Host "Tooling check passed: .NET $dotnetVersion, Node $(& node --version), pnpm $pnpmVersion."
}

function Restore-Repository {
    Invoke-External "dotnet" @(
        "restore",
        (Join-Path $repositoryRoot "Vtt.slnx"),
        "--configfile",
        (Join-Path $repositoryRoot "NuGet.config")
    )
    Invoke-External "pnpm" @("install", "--frozen-lockfile")
}

function Build-Backend {
    Invoke-External "dotnet" @(
        "build",
        (Join-Path $repositoryRoot "Vtt.slnx"),
        "--configuration",
        "Release",
        "--no-restore"
    )
}

function Test-Backend {
    Invoke-External "dotnet" @(
        "test",
        (Join-Path $repositoryRoot "Vtt.slnx"),
        "--configuration",
        "Release",
        "--no-build"
    )
}

function Test-Frontend {
    Invoke-External "pnpm" @("lint")
    Invoke-External "pnpm" @("typecheck")
    Invoke-External "pnpm" @("test")
    Invoke-External "pnpm" @("build")
}

Push-Location $repositoryRoot
try {
    switch ($Command) {
        "doctor" {
            Assert-Tooling
        }
        "bootstrap" {
            Assert-Tooling
            Restore-Repository
            Write-Host "Bootstrap complete. Run '.\eng\vtt.cmd core-up'."
        }
        "restore" {
            Restore-Repository
        }
        "build" {
            Build-Backend
            Invoke-External "pnpm" @("build")
        }
        "test" {
            Build-Backend
            Test-Backend
            Invoke-External "pnpm" @("test")
        }
        "frontend" {
            Test-Frontend
        }
        "verify" {
            Assert-Tooling
            Restore-Repository
            Invoke-External "dotnet" @(
                "format",
                (Join-Path $repositoryRoot "Vtt.slnx"),
                "--verify-no-changes",
                "--no-restore"
            )
            Build-Backend
            Test-Backend
            Test-Frontend
            Invoke-External "pnpm" @("format:check")
            Invoke-External "pnpm" @("security:scan")
            Invoke-Compose @("--profile", "*", "config", "--quiet")
        }
        "core-up" {
            Invoke-Compose @(
                "up",
                "--detach",
                "--wait",
                "postgres",
                "nats",
                "redis",
                "minio"
            )
            Invoke-Compose @("run", "--rm", "postgres-init")
            Invoke-Compose @("run", "--rm", "nats-init")
            Invoke-Compose @("run", "--rm", "minio-init")
        }
        "platform-up" {
            Invoke-Compose @(
                "up",
                "--detach",
                "--wait",
                "postgres",
                "nats"
            )
            Invoke-Compose @("run", "--rm", "postgres-init")
            Invoke-Compose @("run", "--rm", "nats-init")
            Invoke-Compose @(
                "--profile",
                "platform-tests",
                "up",
                "--detach",
                "--build",
                "--wait",
                "engineering-fixture"
            )
        }
        "platform-test" {
            Invoke-External "dotnet" @(
                "test",
                (Join-Path $repositoryRoot "src\backend\PlatformFixtures\Engineering\Vtt.EngineeringFixture.IntegrationTests\Vtt.EngineeringFixture.IntegrationTests.csproj")
            )
            Invoke-External "pnpm" @("contracts:check")
        }
        "app-smoke" {
            Invoke-Compose @(
                "--profile",
                "apps",
                "up",
                "--detach",
                "--build",
                "--wait",
                "edge",
                "web"
            )
        }
        "apps-up" {
            Invoke-Compose @(
                "--profile",
                "apps",
                "up",
                "--detach",
                "--build",
                "--wait",
                "edge",
                "identity",
                "campaign",
                "ruleset",
                "compendium",
                "character",
                "media",
                "scene",
                "session",
                "gameplay",
                "chat-dice",
                "search",
                "web"
            )
        }
        "observability-up" {
            Invoke-Compose @(
                "--profile",
                "apps",
                "--profile",
                "observability",
                "up",
                "--detach",
                "--build",
                "--wait",
                "edge",
                "grafana"
            )
        }
        "dev-tools-up" {
            Invoke-Compose @(
                "--profile",
                "dev-tools",
                "up",
                "--detach",
                "--wait",
                "mailpit"
            )
        }
        "down" {
            Invoke-Compose @("--profile", "*", "down", "--remove-orphans")
        }
        "reset" {
            if (-not $ConfirmReset) {
                throw "Reset deletes only VTT Compose volumes. Re-run with -ConfirmReset."
            }

            Invoke-Compose @("--profile", "*", "down", "--volumes", "--remove-orphans")
            Write-Host "Removed containers and named volumes declared by deploy/compose/compose.yaml."
        }
        "images" {
            Invoke-Compose @(
                "--profile",
                "apps",
                "build",
                "edge",
                "identity",
                "campaign",
                "ruleset",
                "compendium",
                "character",
                "media",
                "scene",
                "session",
                "gameplay",
                "chat-dice",
                "search",
                "web"
            )
        }
    }
}
finally {
    Pop-Location
}
