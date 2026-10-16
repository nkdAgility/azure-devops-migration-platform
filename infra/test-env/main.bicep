// Entra identity used by the platform's live integration tests.
// It reuses the Entra app owned by azure-devops-migration-tools
// (infra/test-env/main.bicep there, uniqueName 'adomt-integration-tests'), which is already a
// Project Collection Administrator in the nkdagility-adomt-* orgs.
// This template only references that app and adds GitHub OIDC federated credentials for this
// repository, so the two repositories' deployments never overwrite each other.
// There is no secret and no certificate: only jobs running in the listed GitHub environments of this
// repository are trusted. Deploy with Setup-TestEnvironment.ps1.
extension microsoftGraphV1

@description('Stable unique name of the shared Entra application (owned by azure-devops-migration-tools).')
param appUniqueName string = 'adomt-integration-tests'

@description('GitHub repository, owner/name.')
param repository string = 'nkdAgility/azure-devops-migration-platform'

@description('GitHub environments whose jobs may sign in as this identity.')
param environments array = [
  'integration-tests'
  'integration-tests-forks'
]

resource app 'Microsoft.Graph/applications@v1.0' existing = {
  uniqueName: appUniqueName
}

resource federatedCredentials 'Microsoft.Graph/applications/federatedIdentityCredentials@v1.0' = [for env in environments: {
  name: '${app.uniqueName}/github-platform-${env}'
  issuer: 'https://token.actions.githubusercontent.com'
  subject: 'repo:${repository}:environment:${env}'
  audiences: [
    'api://AzureADTokenExchange'
  ]
  description: 'GitHub Actions jobs in the ${env} environment of ${repository}'
}]

resource servicePrincipal 'Microsoft.Graph/servicePrincipals@v1.0' existing = {
  appId: app.appId
}

output clientId string = app.appId
output servicePrincipalObjectId string = servicePrincipal.id
output tenantId string = tenant().tenantId
