
param cogSvcsPrincipalId string
param currentUserObjectId string
param aiFoundryPrincipalId string

var storageBlobDataContrib = {
  roleName: 'storageBlobDataContrib'
  roleId: 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'
}
var storageBlobDataReader = {
  roleName: 'storageBlobDataReader'
  roleId: '2a2b9908-6ea1-4ae2-8e65-a410df84e7d1'
}
var storageBlobDataOwner = {
  roleName: 'storageBlobDataOwner'
  roleId: 'b7e6dc6d-f1e8-4753-8033-0f276bb0955b'
}
var storageQueueDataContrib = {
  roleName: 'storageQueueDataContrib'
  roleId: '974c5e8b-45b9-4653-ba55-5f855dd0fb88'
}
var cogServicesContrib = {
  roleName: 'cogServicesContrib'
  roleId: '25fbc0a9-bd7c-42a3-aa1a-3b75d497ee68'
}
var aiSearchContrib = {
  roleName: 'aiSearchContrib'
  roleId: '8ebe5a00-799e-43f5-93ac-243d3dce84a7'
}
var searchServiceContrib = {
  roleName: 'searchServiceContrib'
  roleId: '7ca78c08-252a-4471-8644-bb5ff32d4ba0'
}
var asSearchReader = {
  roleName: 'asSearchReader'
  roleId: '1407120a-92aa-4202-b7e9-c0e197c71c8f'
}
var metricsPublisher = {
  roleName: 'metricsPublisher'
  roleId: '3913510d-42f4-4e42-8a64-420c390055eb'
}
var storageAcctContrib = {
  roleName: 'storageAcctContrib'
  roleId: '17d1049b-9a84-46fb-8f53-869881c3d3ab'
}

var roleAssignments = [
  storageBlobDataContrib
  storageBlobDataReader
  storageBlobDataOwner
  storageQueueDataContrib
  cogServicesContrib
  aiSearchContrib
  searchServiceContrib
  asSearchReader
  metricsPublisher
  storageAcctContrib
]


var userAssignments = [for role in roleAssignments: {
  owner: 'user'
  role: role  
  principalId: currentUserObjectId  
}]

var cogSvcsAssignments = [for role in roleAssignments: {
  owner: 'cogSvcs'
  role: role  
  principalId: cogSvcsPrincipalId  
}]

var aiFoundryAssignments = [for role in roleAssignments: {
  owner: 'aiFoundry'
  role: role  
  principalId: aiFoundryPrincipalId  
}]

var combined = union(userAssignments, cogSvcsAssignments, aiFoundryAssignments)

resource roleAssignmentsResource 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for (assignment, setIndex) in combined: {  
  name:  guid(assignment.owner, assignment.role.roleName, assignment.role.roleId,resourceGroup().id)
  scope: resourceGroup()  
  properties: {  
    description: '${assignment.owner}_${assignment.role.roleName}'
    roleDefinitionId: resourceId('Microsoft.Authorization/roleDefinitions', assignment.role.roleId)  
    principalId: assignment.principalId  
  }  
}]  


