// Internal-only Container Apps environment, locked to the workload VNet.
@description('Resource name prefix.')
param prefix string
@description('Azure region.')
param location string
@description('Tags.')
param tags object
@description('Subnet id for ACA infrastructure.')
param infrastructureSubnetId string
@description('Log Analytics workspace id.')
param lawCustomerId string
@secure()
@description('Log Analytics primary shared key.')
param lawPrimaryKey string

resource env 'Microsoft.App/managedEnvironments@2024-10-02-preview' = {
  name: 'cae-${prefix}'
  location: location
  tags: tags
  properties: {
    vnetConfiguration: {
      infrastructureSubnetId: infrastructureSubnetId
      internal: true
    }
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: lawCustomerId
        sharedKey: lawPrimaryKey
      }
    }
    workloadProfiles: [
      {
        name: 'Consumption'
        workloadProfileType: 'Consumption'
      }
    ]
    zoneRedundant: false
  }
}

output envId string = env.id
output envName string = env.name
output envDefaultDomain string = env.properties.defaultDomain
output envStaticIp string = env.properties.staticIp
