@description('Resource name prefix.')
param prefix string
@description('Tags.')
param tags object

@description('API container app resource ID.')
param apiAppId string
@description('Web MVC container app resource ID.')
param webMvcAppId string
@description('MCP container app resource ID.')
param mcpAppId string
@description('Application Gateway resource ID.')
param appGatewayId string

@description('Optional Action Group resource ID for notifications.')
param actionGroupId string = ''

var hasActionGroup = !empty(actionGroupId)
var alertActions = hasActionGroup ? [
  {
    actionGroupId: actionGroupId
    webHookProperties: {}
  }
] : []

resource webLatency 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: 'ma-${prefix}-webmvc-latency'
  location: 'global'
  tags: tags
  properties: {
    description: 'Web MVC ResponseTime average is high.'
    severity: 2
    enabled: true
    scopes: [ webMvcAppId ]
    evaluationFrequency: 'PT5M'
    windowSize: 'PT15M'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [
        {
          name: 'webmvc-response-time'
          metricName: 'ResponseTime'
          metricNamespace: 'Microsoft.App/containerApps'
          operator: 'GreaterThan'
          threshold: 2000
          timeAggregation: 'Average'
          criterionType: 'StaticThresholdCriterion'
        }
      ]
    }
    autoMitigate: true
    actions: alertActions
  }
}

resource apiRestarts 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: 'ma-${prefix}-api-restarts'
  location: 'global'
  tags: tags
  properties: {
    description: 'API container restarts detected.'
    severity: 2
    enabled: true
    scopes: [ apiAppId ]
    evaluationFrequency: 'PT5M'
    windowSize: 'PT15M'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [
        {
          name: 'api-restart-count'
          metricName: 'RestartCount'
          metricNamespace: 'Microsoft.App/containerApps'
          operator: 'GreaterThan'
          threshold: 0
          timeAggregation: 'Total'
          criterionType: 'StaticThresholdCriterion'
        }
      ]
    }
    autoMitigate: true
    actions: alertActions
  }
}

resource mcpLatency 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: 'ma-${prefix}-mcp-latency'
  location: 'global'
  tags: tags
  properties: {
    description: 'MCP ResponseTime average is high.'
    severity: 3
    enabled: true
    scopes: [ mcpAppId ]
    evaluationFrequency: 'PT5M'
    windowSize: 'PT15M'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [
        {
          name: 'mcp-response-time'
          metricName: 'ResponseTime'
          metricNamespace: 'Microsoft.App/containerApps'
          operator: 'GreaterThan'
          threshold: 1000
          timeAggregation: 'Average'
          criterionType: 'StaticThresholdCriterion'
        }
      ]
    }
    autoMitigate: true
    actions: alertActions
  }
}

resource agwFailedRequests 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: 'ma-${prefix}-agw-failed-requests'
  location: 'global'
  tags: tags
  properties: {
    description: 'Application Gateway failed requests are elevated.'
    severity: 2
    enabled: true
    scopes: [ appGatewayId ]
    evaluationFrequency: 'PT5M'
    windowSize: 'PT15M'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [
        {
          name: 'agw-failed-requests'
          metricName: 'FailedRequests'
          metricNamespace: 'Microsoft.Network/applicationGateways'
          operator: 'GreaterThan'
          threshold: 20
          timeAggregation: 'Total'
          criterionType: 'StaticThresholdCriterion'
        }
      ]
    }
    autoMitigate: true
    actions: alertActions
  }
}

output webLatencyAlertId string = webLatency.id
output apiRestartsAlertId string = apiRestarts.id
output mcpLatencyAlertId string = mcpLatency.id
output agwFailedRequestsAlertId string = agwFailedRequests.id
