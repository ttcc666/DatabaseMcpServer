export interface ApiResponse {
  success: boolean
  message?: string
}

export interface ConfigContext extends ApiResponse {
  configPath: string
  configSource: string
  configExists: boolean
}

export interface DatabaseSummary {
  name: string
  dbType: string
  description?: string | null
  connectionString: string
  isDefault: boolean
  enableDangerousOperations: boolean
  isCurrent: boolean
  optimizationSettings?: Record<string, string> | null
}

export interface DashboardResponse extends ApiResponse {
  configPath: string
  configSource: string
  configExists: boolean
  totalDatabases: number
  currentDefaultDatabase?: string | null
  currentDatabase?: string | null
  databases: DatabaseSummary[]
}

export interface DatabaseDetail {
  name: string
  connectionString: string
  dbType: string
  description?: string | null
  isDefault: boolean
  enableDangerousOperations: boolean
  optimizationSettings?: Record<string, string> | null
}

export interface DatabaseDetailResponse extends ApiResponse {
  configPath: string
  database?: DatabaseDetail
}

export interface PresetSummary {
  dbType: string
  exampleName: string
  description: string
}

export interface PresetListResponse extends ApiResponse {
  totalPresets: number
  presets: PresetSummary[]
}

export interface PresetDetail {
  dbType: string
  exampleName: string
  exampleConnectionString: string
  description: string
}

export interface PresetDetailResponse extends ApiResponse {
  preset?: PresetDetail
}

export interface NoticeState {
  tone: 'info' | 'success' | 'error'
  text: string
}

export type EditorMode = 'create' | 'preset' | 'edit' | 'clone'
export type WorkspaceMode = 'connections' | 'playground'

export interface EditorDraft {
  mode: EditorMode
  sourceName?: string | null
  presetDbType?: string | null
  maskedConnectionHint?: string | null
  name: string
  dbType: string
  connectionMode: 'unchanged' | 'wizard' | 'raw'
  connectionString: string
  connectionFields: Record<string, string>
  description: string
  clearDescription: boolean
  setDefault: boolean
  enableDangerousOperations: boolean
}
