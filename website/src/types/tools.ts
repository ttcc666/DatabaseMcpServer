import type { ApiResponse } from "@/types"

export type ToolCategory = "all" | "connection" | "schema" | "query" | "command" | "other"
export type ToolParameterType = "string" | "int" | "bool" | "json"

export interface ToolParameterMetadata {
  name: string
  optionName: string
  description: string
  type: ToolParameterType
  required: boolean
  defaultValue: unknown
}

export interface ToolMetadata {
  name: string
  description: string
  category: Exclude<ToolCategory, "all">
  requiresConfirmation: boolean
  parameters: ToolParameterMetadata[]
}

export interface ToolCatalogResponse extends ApiResponse {
  tools: ToolMetadata[]
}

export interface ToolInvocationResponse extends ApiResponse {
  toolName: string
  durationMs: number
  toolSuccess: boolean
  result: unknown
}

export type ToolArgumentValue = string | number | boolean
