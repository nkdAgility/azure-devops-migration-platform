<#
.SYNOPSIS
    Creates or updates the platform's live integration test environment. Safe to re-run.

.DESCRIPTION
    Mirrors azure-devops-migration-tools/infra/test-env/Setup-TestEnvironment.ps1 and reuses the same
    Entra app ('adomt-integration-tests') and the same Azure DevOps orgs.

    1. Deploys main.bicep, which adds GitHub OIDC federated credentials for this repository to the
       shared Entra app. The app itself is owned by azure-devops-migration-tools; deploy that
       repository's setup first if the app does not exist yet.
    2. Ensures the app's service principal is a Project Collection Administrator in both orgs. This is
       idempotent, and normally already true from the Migration Tools setup.
    3. Configures GitHub: the integration-tests and integration-tests-forks environments (the latter
       with required reviewers), plus the repository variables the workflow reads.

    Prerequisites (one-off): you are signed in with 'az login', can manage the app registration, and
    are an admin of both orgs. You are also signed in with 'gh auth login' as an admin of this
    repository.

    No secrets are created, printed or stored. Tests authenticate with an Entra access token, used
    wherever a PAT would be used:
      - CI: the shared app, through GitHub OIDC.
      - Locally: your own 'az login'.

    -LocalOnly is for contributors running live tests from their own machine. It skips the Entra and
    GitHub steps, checks that your 'az login' can reach both orgs, and prints the environment
    variables the tests read. The token expires after about an hour, so re-export it for each session.

.EXAMPLE
    # Maintainers: the project's CI environment
    ./Setup-TestEnvironment.ps1 -SourceOrg nkdagility-adomt-source -TargetOrg nkdagility-adomt-target -Reviewers MrHinsh

.EXAMPLE
    # Contributors: run live tests locally with your own Entra sign-in
    ./Setup-TestEnvironment.ps1 -LocalOnly -SourceOrg nkdagility-adomt-source -TargetOrg nkdagility-adomt-target
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $SourceOrg,
    [Parameter(Mandatory)] [string] $TargetOrg,
    [string[]] $Reviewers,
    [string] $Repository = 'nkdAgility/azure-devops-migration-platform',
    [string] $ResourceGroup = 'rg-adomt-integration-tests',
    [string] $Location = 'westeurope',
    [switch] $SkipGitHub,
    [switch] $LocalOnly
)

$ErrorActionPreference = 'Stop'
$AzureDevOpsResource = '499b84ac-1321-427f-aa17-267ca6975798'
if (-not $LocalOnly -and -not $Reviewers) { throw '-Reviewers is required (GitHub logins who approve fork runs), unless -LocalOnly.' }
$spObjectId = $null

function Invoke-Ado {
    param([string] $Method = 'GET', [string] $Uri, $Body, [string] $ContentType = 'application/json')
    $token = az account get-access-token --resource $AzureDevOpsResource --query accessToken -o tsv
    if ($LASTEXITCODE -ne 0 -or -not $token) { throw "Could not get an Azure DevOps token. Run 'az login'." }
    $params = @{ Method = $Method; Uri = $Uri; Headers = @{ Authorization = "Bearer $token" }; ContentType = $ContentType }
    if ($null -ne $Body) { $params.Body = (ConvertTo-Json -InputObject $Body -Depth 20) }
    Invoke-RestMethod @params
}

# --- 1. Entra identity (Bicep) -------------------------------------------------------------------
if (-not $LocalOnly) {
    Write-Host "== Adding federated credentials for $Repository (main.bicep) in resource group $ResourceGroup"
    az group create --name $ResourceGroup --location $Location --output none
    $deployment = az deployment group create `
        --resource-group $ResourceGroup `
        --name adomp-integration-tests `
        --template-file (Join-Path $PSScriptRoot 'main.bicep') `
        --parameters repository=$Repository `
        --query properties.outputs -o json | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0) { throw "Bicep deployment failed. If the app 'adomt-integration-tests' does not exist, run azure-devops-migration-tools/infra/test-env/Setup-TestEnvironment.ps1 first." }
    $clientId = $deployment.clientId.value
    $spObjectId = $deployment.servicePrincipalObjectId.value
    $tenantId = $deployment.tenantId.value
    Write-Host "   clientId=$clientId tenant=$tenantId"
}

# --- 2. Azure DevOps orgs ------------------------------------------------------------------------
function Set-OrgAccess {
    param([string] $Org)
    Write-Host "== Checking https://dev.azure.com/$Org/"
    Invoke-Ado -Uri "https://dev.azure.com/$Org/_apis/projects?`$top=1&api-version=7.1" | Out-Null
    Write-Host "   reachable with your 'az login'"
    if (-not $spObjectId) { return }

    # Add the service principal to the org (idempotent: returns the existing entitlement).
    $entitlement = Invoke-Ado -Method POST -Uri "https://vsaex.dev.azure.com/$Org/_apis/serviceprincipalentitlements?api-version=7.1-preview.1" -Body @{
        accessLevel      = @{ accountLicenseType = 'express' }
        servicePrincipal = @{ origin = 'aad'; originId = $spObjectId; subjectKind = 'servicePrincipal' }
    }
    $spDescriptor = @(
        $entitlement.servicePrincipalEntitlement.servicePrincipal.descriptor
        $entitlement.operationResult.result.servicePrincipal.descriptor
    ) | Where-Object { $_ } | Select-Object -First 1
    if (-not $spDescriptor) {
        $all = Invoke-Ado -Uri "https://vsaex.dev.azure.com/$Org/_apis/serviceprincipalentitlements?api-version=7.1-preview.1"
        $spDescriptor = ($all.items | Where-Object { $_.servicePrincipal.originId -eq $spObjectId } | Select-Object -First 1).servicePrincipal.descriptor
    }
    if (-not $spDescriptor) {
        $errors = ($entitlement.operationResult.errors | ForEach-Object { $_.value }) -join '; '
        throw "Could not add the service principal to $Org. $errors"
    }

    # Project Collection Administrators membership (PUT is idempotent). Live runs create and delete
    # their own adpe2e-<run-id> projects, so the identity needs collection-level rights.
    $groups = Invoke-Ado -Uri "https://vssps.dev.azure.com/$Org/_apis/graph/groups?scopeDescriptor=&api-version=7.1-preview.1"
    $pca = $groups.value | Where-Object { $_.principalName -eq "[$Org]\Project Collection Administrators" } | Select-Object -First 1
    if (-not $pca) { throw "Project Collection Administrators group not found in $Org." }
    Invoke-Ado -Method PUT -Uri "https://vssps.dev.azure.com/$Org/_apis/graph/memberships/$spDescriptor/$($pca.descriptor)?api-version=7.1-preview.1" | Out-Null
    Write-Host "   service principal is a Project Collection Administrator"
}

Set-OrgAccess -Org $SourceOrg
Set-OrgAccess -Org $TargetOrg

# --- 3. GitHub environments and variables --------------------------------------------------------
if (-not $SkipGitHub -and -not $LocalOnly) {
    Write-Host "== Configuring GitHub $Repository"
    function Assert-Gh([string] $What) { if ($LASTEXITCODE -ne 0) { throw "GitHub CLI failed: $What (exit $LASTEXITCODE). Check 'gh auth status' has admin rights on $Repository." } }

    '{}' | gh api -X PUT "repos/$Repository/environments/integration-tests" --input - | Out-Null
    Assert-Gh 'create environment integration-tests'

    $reviewerIds = foreach ($login in $Reviewers) {
        $id = gh api "users/$login" --jq .id
        Assert-Gh "look up reviewer $login"
        @{ type = 'User'; id = [int]$id }
    }
    @{ prevent_self_review = $true; reviewers = @($reviewerIds) } | ConvertTo-Json -Depth 5 |
        gh api -X PUT "repos/$Repository/environments/integration-tests-forks" --input - | Out-Null
    Assert-Gh 'create environment integration-tests-forks with required reviewers'
    Write-Host "   environments: integration-tests, integration-tests-forks (reviewers: $($Reviewers -join ', '))"

    $variables = [ordered]@{
        AZURE_TEST_CLIENT_ID       = $clientId
        AZURE_TEST_TENANT_ID       = $tenantId
        ADO_TEST_SOURCE_COLLECTION = "https://dev.azure.com/$SourceOrg/"
        ADO_TEST_TARGET_COLLECTION = "https://dev.azure.com/$TargetOrg/"
    }
    foreach ($name in $variables.Keys) {
        gh variable set $name --body $variables[$name] --repo $Repository
        Assert-Gh "set variable $name"
    }
    Write-Host "   variables: $($variables.Keys -join ', ')"
}

if ($LocalOnly) {
    Write-Host "== Done. In each new terminal, export an Entra token wherever the tests expect a PAT:"
    Write-Host "   `$env:AZDEVOPS_SYSTEM_TEST_ORG = 'https://dev.azure.com/$SourceOrg/'"
    Write-Host "   `$env:AZDEVOPS_SYSTEM_TEST_PAT = (az account get-access-token --resource $AzureDevOpsResource --query accessToken -o tsv)"
    Write-Host "   (Tokens expire after about an hour; re-run the last line if live tests start returning 401.)"
    return
}
Write-Host "== Done. Run the integration tests workflow to validate."
