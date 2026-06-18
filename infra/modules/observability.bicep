@description('Resource name prefix.')
param prefix string
@description('Azure region.')
param location string
@description('Tags.')
param tags object

resource law 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: 'log-${prefix}'
  location: location
  tags: tags
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

resource appi 'Microsoft.Insights/components@2020-02-02' = {
  name: 'appi-${prefix}'
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: law.id
  }
}

output lawId string = law.id
output lawCustomerId string = law.properties.customerId
@secure()
output lawPrimaryKey string = law.listKeys().primarySharedKey
output appiId string = appi.id
output appiName string = appi.name
output appiConnectionString string = appi.properties.ConnectionString
