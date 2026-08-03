import type { ApiResponse, DatabaseSummary } from "@/types"

export interface ConnectionHealthResult {
  name: string
  dbType: string
  isHealthy: boolean
  responseTimeMs: number
  errorMessage: string
  checkedAt: string
}

export interface ConnectionHealthResponse extends ApiResponse {
  overallHealth: boolean
  totalConnections: number
  healthyConnections: number
  unhealthyConnections: number
  results: readonly ConnectionHealthResult[]
}

export type ConnectionStatusFilter = "all" | "default" | "current" | "dangerous" | "healthy" | "unhealthy" | "unchecked"
export type ConnectionSortKey = "name" | "dbType" | "status" | "latency"

export interface ConnectionTableItem extends DatabaseSummary {
  health?: ConnectionHealthResult
}

export type ConnectionStringInputType = "text" | "password" | "number" | "boolean"
export type ConnectionStringFormat = "keyValue" | "uri" | "raw"

export interface ConnectionStringFieldDefinition {
  key: string
  label: string
  inputType: ConnectionStringInputType
  required: boolean
  sensitive: boolean
  advanced: boolean
  defaultValue?: string | null
}

export interface ConnectionStringProfile {
  dbType: string
  format: ConnectionStringFormat
  supportsWizard: boolean
  fields: ConnectionStringFieldDefinition[]
}

export interface ConnectionStringProfileResponse extends ApiResponse {
  profile: ConnectionStringProfile
}

export type ConnectionEditorMode = "unchanged" | "wizard" | "raw"
