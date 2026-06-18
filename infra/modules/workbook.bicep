@description('Azure region.')
param location string
@description('Tags.')
param tags object

@description('Application Insights resource ID for workbook source binding.')
param appInsightsId string
@description('Application Insights resource name.')
param appInsightsName string

@description('API container app name.')
param apiAppName string
@description('Web MVC container app name.')
param webMvcAppName string
@description('MCP container app name.')
param mcpAppName string
@description('Application Gateway name.')
param appGatewayName string

var workbookTemplate = loadTextContent('../workbooks/stocky-observability.workbook.json')
var workbookData = replace(
  replace(
    replace(
      replace(
        replace(
          replace(workbookTemplate, '__APPINSIGHTS_ID__', appInsightsId),
          '__APPINSIGHTS_NAME__', appInsightsName),
        '__API_APP_NAME__', apiAppName),
      '__WEBMVC_APP_NAME__', webMvcAppName),
    '__MCP_APP_NAME__', mcpAppName),
  '__APPGATEWAY_NAME__', appGatewayName)

resource workbook 'Microsoft.Insights/workbooks@2022-04-01' = {
  name: guid(appInsightsId, 'stocky-observability-workbook')
  location: location
  kind: 'shared'
  tags: tags
  properties: {
    displayName: 'Stocky - Observability'
    sourceId: appInsightsId
    category: 'workbook'
    serializedData: workbookData
    version: '1.0'
  }
}

output workbookId string = workbook.id
output workbookName string = workbook.name
output workbookDisplayName string = workbook.properties.displayName
