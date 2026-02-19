import { createFileRoute } from '@tanstack/react-router'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useMemo, useRef, useState } from 'react'
import { env } from '#/env'

export const Route = createFileRoute('/')({ component: App })

const API_URL_STORAGE_KEY = 'image-processor.api-url'
const TOKEN_STORAGE_KEY = 'image-processor.token'
const DEFAULT_API_BASE_URL = env.VITE_API_BASE_URL ?? 'http://localhost:5434'
const PAGE_SIZE = 20
const MAX_UPLOAD_SIZE = 50 * 1024 * 1024
const ALLOWED_MIME_TYPES = new Set(['image/jpeg', 'image/png', 'image/webp'])
const ACTIVE_STATUSES = new Set(['Pending', 'Processing'])
const SUCCESS_STATUSES = new Set(['Completed', 'Finished'])

type AuthMode = 'login' | 'register'

interface AuthResponse {
  token: string
  email: string
  name: string
}

interface LoginRequest {
  email: string
  password: string
}

interface RegisterRequest extends LoginRequest {
  name: string
}

interface UploadResponse {
  id: string
  originalUrl: string
  status: string
}

interface JobMetadata {
  width: number
  height: number
  format: string
  fileSize: number
  exif: Record<string, string | null>
  dominantColors: string[]
}

interface JobAiTag {
  label: string
  confidence: number
}

interface JobAiSafety {
  adult: boolean
  violence: boolean
  selfHarm: boolean
}

interface JobAiMeta {
  model: string
  latencyMs: number
  inputTokens: number | null
  outputTokens: number | null
  estimatedCostUsd: number | null
}

interface JobAiAnalysis {
  summary: string
  ocrText: string | null
  tags: JobAiTag[]
  safety: JobAiSafety
  meta: JobAiMeta
}

interface JobResponse {
  id: string
  userId: string
  status: string
  originalUrl: string
  originalFilename: string
  fileSize: number
  createdAt: string
  startedAt: string | null
  completedAt: string | null
  errorMessage: string | null
  retryCount: number
  thumbnails: Record<string, string> | null
  optimized: Record<string, string> | null
  metadata: JobMetadata | null
  aiStatus: string
  aiStartedAt: string | null
  aiCompletedAt: string | null
  aiErrorMessage: string | null
  aiRetryCount: number
  aiAnalysis: JobAiAnalysis | null
}

interface PagedResponse<T> {
  items: T[]
  page: number
  pageSize: number
  totalCount: number
  totalPages: number
}

function normalizeApiBaseUrl(url: string): string {
  return url.trim().replace(/\/+$/, '')
}

async function readError(response: Response): Promise<string> {
  const body = await response.text()
  if (!body) {
    return `Request failed (${response.status})`
  }

  try {
    const parsed = JSON.parse(body) as {
      title?: string
      detail?: string
      message?: string
    }

    if (parsed.detail) {
      return parsed.detail
    }

    if (parsed.message) {
      return parsed.message
    }

    if (parsed.title) {
      return parsed.title
    }
  } catch {
    return body
  }

  return body
}

async function requestJson<T>({
  baseUrl,
  path,
  method = 'GET',
  token,
  body,
}: {
  baseUrl: string
  path: string
  method?: 'GET' | 'POST'
  token?: string
  body?: BodyInit | null
}): Promise<T> {
  const headers = new Headers()
  if (token) {
    headers.set('Authorization', `Bearer ${token}`)
  }

  const isFormData = body instanceof FormData
  if (body && !isFormData) {
    headers.set('Content-Type', 'application/json')
  }

  const response = await fetch(`${baseUrl}${path}`, {
    method,
    headers,
    body,
  })

  if (!response.ok) {
    throw new Error(await readError(response))
  }

  return (await response.json()) as T
}

function isActiveStatus(status: string): boolean {
  return ACTIVE_STATUSES.has(status)
}

function isSuccessStatus(status: string): boolean {
  return SUCCESS_STATUSES.has(status)
}

function formatDateTime(value: string | null): string {
  if (!value) {
    return 'Not available'
  }

  const parsed = new Date(value)
  if (Number.isNaN(parsed.getTime())) {
    return 'Not available'
  }

  return parsed.toLocaleString()
}

function formatBytes(value: number): string {
  if (value === 0) {
    return '0 B'
  }

  const units = ['B', 'KB', 'MB', 'GB']
  const index = Math.min(Math.floor(Math.log(value) / Math.log(1024)), units.length - 1)
  const scaled = value / 1024 ** index
  return `${scaled.toFixed(scaled >= 100 ? 0 : 1)} ${units[index]}`
}

function getStatusClasses(status: string): string {
  switch (status) {
    case 'Completed':
    case 'Finished':
      return 'border-emerald-200 text-emerald-700'
    case 'Processing':
      return 'border-blue-200 text-blue-700'
    case 'Pending':
      return 'border-amber-200 text-amber-700'
    case 'Error':
      return 'border-rose-200 text-rose-700'
    default:
      return 'border-neutral-200 text-neutral-700'
  }
}

function resolveOverallStatus(job: JobResponse): string {
  if (job.status === 'Error' || job.aiStatus === 'Error') {
    return 'Error'
  }

  if (job.status === 'Pending' || job.status === 'Processing') {
    return 'Processing'
  }

  if (
    SUCCESS_STATUSES.has(job.status) &&
    (job.aiStatus === 'Pending' || job.aiStatus === 'Processing')
  ) {
    return 'AI Processing'
  }

  if (SUCCESS_STATUSES.has(job.status) && job.aiStatus === 'Completed') {
    return 'Completed'
  }

  if (SUCCESS_STATUSES.has(job.status) && job.aiStatus === 'Skipped') {
    return 'Completed'
  }

  return 'Processing'
}

function getDurationSeconds(job: JobResponse): number | null {
  if (!job.startedAt || !job.completedAt) {
    return null
  }

  const start = new Date(job.startedAt).getTime()
  const end = new Date(job.completedAt).getTime()
  if (Number.isNaN(start) || Number.isNaN(end) || end <= start) {
    return null
  }

  return (end - start) / 1000
}

function getErrorMessage(error: unknown): string {
  return error instanceof Error ? error.message : 'Unexpected error'
}

async function login(
  baseUrl: string,
  payload: LoginRequest,
): Promise<AuthResponse> {
  return requestJson<AuthResponse>({
    baseUrl,
    path: '/api/auth/login',
    method: 'POST',
    body: JSON.stringify(payload),
  })
}

async function register(
  baseUrl: string,
  payload: RegisterRequest,
): Promise<AuthResponse> {
  return requestJson<AuthResponse>({
    baseUrl,
    path: '/api/auth/register',
    method: 'POST',
    body: JSON.stringify(payload),
  })
}

async function uploadImage(
  baseUrl: string,
  token: string,
  file: File,
): Promise<UploadResponse> {
  const formData = new FormData()
  formData.append('file', file)

  return requestJson<UploadResponse>({
    baseUrl,
    path: '/api/images/upload',
    method: 'POST',
    token,
    body: formData,
  })
}

async function fetchJobs(
  baseUrl: string,
  token: string,
  page: number,
  pageSize: number,
): Promise<PagedResponse<JobResponse>> {
  return requestJson<PagedResponse<JobResponse>>({
    baseUrl,
    path: `/api/images?page=${page}&pageSize=${pageSize}`,
    token,
  })
}

async function fetchJobById(
  baseUrl: string,
  token: string,
  jobId: string,
): Promise<JobResponse> {
  return requestJson<JobResponse>({
    baseUrl,
    path: `/api/images/${jobId}`,
    token,
  })
}

function App() {
  const queryClient = useQueryClient()
  const detailSectionRef = useRef<HTMLElement | null>(null)
  const [apiBaseUrl, setApiBaseUrl] = useState(DEFAULT_API_BASE_URL)
  const [apiBaseUrlDraft, setApiBaseUrlDraft] = useState(DEFAULT_API_BASE_URL)
  const [token, setToken] = useState('')
  const [authMode, setAuthMode] = useState<AuthMode>('login')
  const [name, setName] = useState('')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [selectedFile, setSelectedFile] = useState<File | null>(null)
  const [selectedJobId, setSelectedJobId] = useState<string | null>(null)
  const [page, setPage] = useState(1)
  const [sessionName, setSessionName] = useState('')
  const [sessionEmail, setSessionEmail] = useState('')
  const [uploadMessage, setUploadMessage] = useState<string | null>(null)
  const [uploadError, setUploadError] = useState<string | null>(null)

  useEffect(() => {
    if (typeof window === 'undefined') {
      return
    }

    const storedApiUrl = window.localStorage.getItem(API_URL_STORAGE_KEY)
    if (storedApiUrl) {
      const normalized = normalizeApiBaseUrl(storedApiUrl)
      setApiBaseUrl(normalized)
      setApiBaseUrlDraft(normalized)
    }

    const storedToken = window.localStorage.getItem(TOKEN_STORAGE_KEY)
    if (storedToken) {
      setToken(storedToken)
    }
  }, [])

  useEffect(() => {
    if (typeof window === 'undefined') {
      return
    }

    if (token) {
      window.localStorage.setItem(TOKEN_STORAGE_KEY, token)
      return
    }

    window.localStorage.removeItem(TOKEN_STORAGE_KEY)
  }, [token])

  const loginMutation = useMutation({
    mutationFn: async () => {
      return login(apiBaseUrl, { email, password })
    },
    onSuccess: (result) => {
      setToken(result.token)
      setSessionEmail(result.email)
      setSessionName(result.name)
      setPassword('')
      setUploadMessage('Signed in successfully.')
      setUploadError(null)
    },
    onError: (error) => {
      setUploadError(getErrorMessage(error))
      setUploadMessage(null)
    },
  })

  const registerMutation = useMutation({
    mutationFn: async () => {
      return register(apiBaseUrl, { name, email, password })
    },
    onSuccess: (result) => {
      setToken(result.token)
      setSessionEmail(result.email)
      setSessionName(result.name)
      setPassword('')
      setUploadMessage('Account created and signed in.')
      setUploadError(null)
    },
    onError: (error) => {
      setUploadError(getErrorMessage(error))
      setUploadMessage(null)
    },
  })

  const uploadMutation = useMutation({
    mutationFn: async (file: File) => uploadImage(apiBaseUrl, token, file),
    onSuccess: (response) => {
      setSelectedFile(null)
      setSelectedJobId(response.id)
      setPage(1)
      setUploadMessage(`Job queued: ${response.id}`)
      setUploadError(null)

      void queryClient.invalidateQueries({
        queryKey: ['jobs', apiBaseUrl, token],
      })
      void queryClient.invalidateQueries({
        queryKey: ['job', apiBaseUrl, token, response.id],
      })
    },
    onError: (error) => {
      setUploadError(getErrorMessage(error))
      setUploadMessage(null)
    },
  })

  const jobsQuery = useQuery({
    queryKey: ['jobs', apiBaseUrl, token, page],
    queryFn: () => fetchJobs(apiBaseUrl, token, page, PAGE_SIZE),
    enabled: token.length > 0,
    refetchInterval: (query) => {
      const jobs = query.state.data?.items ?? []
      return jobs.some((job) => isActiveStatus(job.status)) ? 2500 : false
    },
  })

  const selectedJobQuery = useQuery({
    queryKey: ['job', apiBaseUrl, token, selectedJobId],
    queryFn: () => fetchJobById(apiBaseUrl, token, selectedJobId as string),
    enabled: token.length > 0 && selectedJobId !== null,
    refetchInterval: (query) => {
      const status = query.state.data?.status
      return status && isActiveStatus(status) ? 2000 : false
    },
  })

  const jobs = jobsQuery.data?.items ?? []
  const selectedJob =
    selectedJobQuery.data ??
    jobs.find((job) => job.id === selectedJobId) ??
    null

  const metrics = useMemo(() => {
    const today = new Date().toDateString()
    const jobsToday = jobs.filter(
      (job) => new Date(job.createdAt).toDateString() === today,
    ).length

    const inProgress = jobs.filter((job) => isActiveStatus(job.status)).length
    const terminalJobs = jobs.filter((job) => !isActiveStatus(job.status))
    const successfulJobs = terminalJobs.filter((job) =>
      isSuccessStatus(job.status),
    ).length

    const successRate =
      terminalJobs.length > 0 ? (successfulJobs / terminalJobs.length) * 100 : null

    const durations = jobs
      .map((job) => getDurationSeconds(job))
      .filter((value): value is number => value !== null)

    const averageDurationSeconds =
      durations.length > 0
        ? durations.reduce((sum, current) => sum + current, 0) / durations.length
        : null

    return {
      jobsToday,
      inProgress,
      successRate,
      averageDurationSeconds,
    }
  }, [jobs])

  const isAuthLoading = loginMutation.isPending || registerMutation.isPending

  const refreshAll = async () => {
    if (!token) {
      return
    }

    await jobsQuery.refetch()
    if (selectedJobId) {
      await selectedJobQuery.refetch()
    }
  }

  return (
    <main className="mx-auto max-w-7xl px-4 py-8 sm:px-6 lg:px-8">
      <section className="flex flex-wrap items-start justify-between gap-4">
        <div className="space-y-2">
          <h1 className="text-3xl font-semibold tracking-tight text-neutral-950">
            ImageProcessor Pipeline Console
          </h1>
          <p className="max-w-3xl text-sm text-neutral-600">
            Sign in, upload an image, and inspect job processing and AI results.
          </p>
        </div>
        <button
          type="button"
          onClick={() => {
            void refreshAll()
          }}
          disabled={!token || jobsQuery.isFetching || selectedJobQuery.isFetching}
          className="h-10 rounded-lg border border-neutral-900 px-4 text-sm font-medium text-neutral-900 transition hover:bg-neutral-900 hover:text-white disabled:cursor-not-allowed disabled:opacity-50"
        >
          {jobsQuery.isFetching || selectedJobQuery.isFetching
            ? 'Refreshing...'
            : 'Refresh data'}
        </button>
      </section>

      <section className="mt-6 grid gap-4 md:grid-cols-2 lg:grid-cols-4">
        <MetricCard label="Jobs today" value={String(metrics.jobsToday)} />
        <MetricCard label="In progress" value={String(metrics.inProgress)} />
        <MetricCard
          label="Success rate"
          value={metrics.successRate === null ? '--' : `${metrics.successRate.toFixed(1)}%`}
        />
        <MetricCard
          label="Avg processing"
          value={
            metrics.averageDurationSeconds === null
              ? '--'
              : `${metrics.averageDurationSeconds.toFixed(1)}s`
          }
        />
      </section>

      <section className="mt-6 grid gap-6 xl:grid-cols-[1.3fr_1fr]">
        <div className="space-y-6">
          <form
            className="rounded-2xl border border-neutral-200 p-5"
            onSubmit={(event) => {
              event.preventDefault()
              const normalized = normalizeApiBaseUrl(apiBaseUrlDraft)
              setApiBaseUrl(normalized)
              setApiBaseUrlDraft(normalized)

              if (typeof window !== 'undefined') {
                window.localStorage.setItem(API_URL_STORAGE_KEY, normalized)
              }
            }}
          >
            <h2 className="text-base font-semibold text-neutral-950">Connection</h2>
            <p className="mt-1 text-sm text-neutral-600">
              Point the UI to your API service URL.
            </p>
            <div className="mt-4 flex flex-col gap-3 sm:flex-row">
              <input
                value={apiBaseUrlDraft}
                onChange={(event) => setApiBaseUrlDraft(event.target.value)}
                placeholder="https://api.example.com"
                className="h-10 flex-1 rounded-lg border border-neutral-300 px-3 text-sm outline-none transition focus:border-neutral-500"
              />
              <button
                type="submit"
                className="h-10 rounded-lg border border-neutral-900 px-4 text-sm font-medium text-neutral-900 transition hover:bg-neutral-900 hover:text-white"
              >
                Save endpoint
              </button>
            </div>
            <p className="mt-2 text-xs text-neutral-500">
              Active endpoint: {apiBaseUrl}
            </p>
          </form>

          <section className="rounded-2xl border border-neutral-200 p-5">
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div>
                <h2 className="text-base font-semibold text-neutral-950">Upload</h2>
                <p className="mt-1 text-sm text-neutral-600">
                  Allowed: JPG, PNG, WEBP up to 50MB.
                </p>
              </div>
              <button
                type="button"
                onClick={() => jobsQuery.refetch()}
                disabled={!token || jobsQuery.isFetching}
                className="h-9 rounded-lg border border-neutral-300 px-3 text-sm text-neutral-700 transition hover:border-neutral-500 disabled:cursor-not-allowed disabled:opacity-50"
              >
                Refresh jobs
              </button>
            </div>

            {!token && (
              <p className="mt-4 rounded-lg border border-amber-200 px-3 py-2 text-sm text-amber-700">
                Sign in first to upload and fetch jobs.
              </p>
            )}

            {uploadMessage && (
              <p className="mt-4 rounded-lg border border-emerald-200 px-3 py-2 text-sm text-emerald-700">
                {uploadMessage}
              </p>
            )}

            {uploadError && (
              <p className="mt-4 rounded-lg border border-rose-200 px-3 py-2 text-sm text-rose-700">
                {uploadError}
              </p>
            )}

            <form
              className="mt-4 flex flex-col gap-3 sm:flex-row sm:items-center"
              onSubmit={(event) => {
                event.preventDefault()

                if (!selectedFile) {
                  setUploadError('Choose an image before uploading.')
                  setUploadMessage(null)
                  return
                }

                if (!token) {
                  setUploadError('You need to sign in first.')
                  setUploadMessage(null)
                  return
                }

                uploadMutation.mutate(selectedFile)
              }}
            >
              <label className="inline-flex h-10 cursor-pointer items-center rounded-lg border border-neutral-300 px-3 text-sm text-neutral-700 transition hover:border-neutral-500">
                <input
                  type="file"
                  accept="image/jpeg,image/png,image/webp"
                  className="hidden"
                  onChange={(event) => {
                    const file = event.target.files?.[0] ?? null
                    setSelectedFile(null)

                    if (!file) {
                      return
                    }

                    if (!ALLOWED_MIME_TYPES.has(file.type)) {
                      setUploadError('Only JPEG, PNG, and WEBP images are allowed.')
                      setUploadMessage(null)
                      return
                    }

                    if (file.size > MAX_UPLOAD_SIZE) {
                      setUploadError('File size exceeds 50MB.')
                      setUploadMessage(null)
                      return
                    }

                    setUploadError(null)
                    setUploadMessage(null)
                    setSelectedFile(file)
                  }}
                />
                Choose image
              </label>

              <button
                type="submit"
                disabled={!selectedFile || uploadMutation.isPending || !token}
                className="h-10 rounded-lg border border-neutral-900 px-4 text-sm font-medium text-neutral-900 transition hover:bg-neutral-900 hover:text-white disabled:cursor-not-allowed disabled:opacity-50"
              >
                {uploadMutation.isPending ? 'Uploading...' : 'Upload and queue'}
              </button>
            </form>

            <p className="mt-2 text-xs text-neutral-500">
              Selected file:{' '}
              {selectedFile
                ? `${selectedFile.name} (${formatBytes(selectedFile.size)})`
                : 'None'}
            </p>
          </section>

          <section className="rounded-2xl border border-neutral-200 p-5">
            <div className="flex flex-wrap items-center justify-between gap-3">
              <h2 className="text-base font-semibold text-neutral-950">
                Job history
              </h2>
              {jobsQuery.data && (
                <p className="text-xs text-neutral-500">
                  Page {jobsQuery.data.page} of {jobsQuery.data.totalPages} (
                  {jobsQuery.data.totalCount} total)
                </p>
              )}
            </div>

            {jobsQuery.isLoading && (
              <p className="mt-4 text-sm text-neutral-500">Loading jobs...</p>
            )}

            {jobsQuery.isError && (
              <p className="mt-4 rounded-lg border border-rose-200 px-3 py-2 text-sm text-rose-700">
                {getErrorMessage(jobsQuery.error)}
              </p>
            )}

            {!jobsQuery.isLoading && jobs.length === 0 && token && (
              <p className="mt-4 text-sm text-neutral-500">
                No jobs yet. Upload an image to start.
              </p>
            )}

            {jobs.length > 0 && (
              <>
                <div className="mt-4 overflow-x-auto">
                  <table className="min-w-full border-collapse text-left text-sm">
                    <thead>
                      <tr className="border-b border-neutral-200 text-xs uppercase tracking-wide text-neutral-500">
                        <th className="py-3 pr-4 font-medium">Job</th>
                        <th className="py-3 pr-4 font-medium">File</th>
                        <th className="py-3 pr-4 font-medium">Status</th>
                        <th className="py-3 pr-4 font-medium">Created</th>
                        <th className="py-3 font-medium">Action</th>
                      </tr>
                    </thead>
                    <tbody>
                      {jobs.map((job) => {
                        const overallStatus = resolveOverallStatus(job)

                        return (
                          <tr
                            key={job.id}
                            className={
                              job.id === selectedJobId
                                ? 'border-b border-neutral-100 bg-neutral-50'
                                : 'border-b border-neutral-100'
                            }
                          >
                            <td className="py-3 pr-4 align-top">
                              <p className="font-medium text-neutral-900">
                                {job.id.slice(0, 8)}...
                              </p>
                              <p className="mt-1 text-xs text-neutral-500">
                                {formatBytes(job.fileSize)}
                              </p>
                            </td>
                            <td className="py-3 pr-4 align-top text-neutral-700">
                              <div className="max-w-[180px] truncate">
                                {job.originalFilename}
                              </div>
                            </td>
                            <td className="py-3 pr-4 align-top">
                              <span
                                className={`inline-flex rounded-full border px-2.5 py-1 text-xs font-medium ${getStatusClasses(overallStatus)}`}
                              >
                                {overallStatus}
                              </span>
                            </td>
                            <td className="py-3 pr-4 align-top text-neutral-600">
                              {formatDateTime(job.createdAt)}
                            </td>
                            <td className="py-3 align-top">
                              <button
                                type="button"
                                onClick={() => {
                                  setSelectedJobId(job.id)
                                  detailSectionRef.current?.scrollIntoView({
                                    behavior: 'smooth',
                                    block: 'start',
                                  })
                                }}
                                className="rounded-md border border-neutral-300 px-2.5 py-1 text-xs font-medium text-neutral-700 transition hover:border-neutral-500"
                              >
                                Inspect
                              </button>
                            </td>
                          </tr>
                        )
                      })}
                    </tbody>
                  </table>
                </div>

                <div className="mt-4 flex items-center justify-between">
                  <button
                    type="button"
                    onClick={() => setPage((current) => Math.max(current - 1, 1))}
                    disabled={page <= 1 || jobsQuery.isFetching}
                    className="h-9 rounded-lg border border-neutral-300 px-3 text-sm text-neutral-700 transition hover:border-neutral-500 disabled:cursor-not-allowed disabled:opacity-50"
                  >
                    Previous
                  </button>
                  <button
                    type="button"
                    onClick={() =>
                      setPage((current) =>
                        jobsQuery.data?.totalPages
                          ? Math.min(current + 1, jobsQuery.data.totalPages)
                          : current + 1,
                      )
                    }
                    disabled={
                      !jobsQuery.data ||
                      page >= jobsQuery.data.totalPages ||
                      jobsQuery.isFetching
                    }
                    className="h-9 rounded-lg border border-neutral-300 px-3 text-sm text-neutral-700 transition hover:border-neutral-500 disabled:cursor-not-allowed disabled:opacity-50"
                  >
                    Next
                  </button>
                </div>
              </>
            )}
          </section>
        </div>

        <div className="space-y-6">
          <section
            ref={detailSectionRef}
            className="rounded-2xl border border-neutral-200 p-5"
          >
            <h2 className="text-base font-semibold text-neutral-950">Session</h2>
            {token ? (
              <div className="mt-3 space-y-3">
                <p className="text-sm text-neutral-600">
                  Signed in {sessionName ? `as ${sessionName}` : 'with JWT token'}.
                </p>
                {sessionEmail && (
                  <p className="text-sm text-neutral-600">Email: {sessionEmail}</p>
                )}
                <button
                  type="button"
                  onClick={() => {
                    setToken('')
                    setSessionEmail('')
                    setSessionName('')
                    setSelectedJobId(null)
                    setUploadMessage('Signed out.')
                    setUploadError(null)
                    void queryClient.removeQueries({
                      queryKey: ['jobs', apiBaseUrl],
                    })
                  }}
                  className="h-10 rounded-lg border border-neutral-900 px-4 text-sm font-medium text-neutral-900 transition hover:bg-neutral-900 hover:text-white"
                >
                  Sign out
                </button>
              </div>
            ) : (
              <form
                className="mt-4 space-y-3"
                onSubmit={(event) => {
                  event.preventDefault()
                  if (authMode === 'login') {
                    loginMutation.mutate()
                    return
                  }
                  registerMutation.mutate()
                }}
              >
                <div className="inline-flex rounded-lg border border-neutral-300 p-1">
                  <button
                    type="button"
                    className={`rounded-md px-3 py-1.5 text-sm transition ${
                      authMode === 'login'
                        ? 'border border-neutral-900 text-neutral-900'
                        : 'text-neutral-500'
                    }`}
                    onClick={() => setAuthMode('login')}
                  >
                    Login
                  </button>
                  <button
                    type="button"
                    className={`rounded-md px-3 py-1.5 text-sm transition ${
                      authMode === 'register'
                        ? 'border border-neutral-900 text-neutral-900'
                        : 'text-neutral-500'
                    }`}
                    onClick={() => setAuthMode('register')}
                  >
                    Register
                  </button>
                </div>

                {authMode === 'register' && (
                  <input
                    value={name}
                    onChange={(event) => setName(event.target.value)}
                    placeholder="Name"
                    className="h-10 w-full rounded-lg border border-neutral-300 px-3 text-sm outline-none transition focus:border-neutral-500"
                    required
                  />
                )}

                <input
                  value={email}
                  onChange={(event) => setEmail(event.target.value)}
                  placeholder="Email"
                  type="email"
                  className="h-10 w-full rounded-lg border border-neutral-300 px-3 text-sm outline-none transition focus:border-neutral-500"
                  required
                />

                <input
                  value={password}
                  onChange={(event) => setPassword(event.target.value)}
                  placeholder="Password"
                  type="password"
                  className="h-10 w-full rounded-lg border border-neutral-300 px-3 text-sm outline-none transition focus:border-neutral-500"
                  required
                  minLength={8}
                />

                <button
                  type="submit"
                  disabled={isAuthLoading}
                  className="h-10 rounded-lg border border-neutral-900 px-4 text-sm font-medium text-neutral-900 transition hover:bg-neutral-900 hover:text-white disabled:cursor-not-allowed disabled:opacity-50"
                >
                  {isAuthLoading
                    ? 'Working...'
                    : authMode === 'login'
                      ? 'Sign in'
                      : 'Create account'}
                </button>
              </form>
            )}
          </section>

          <section className="rounded-2xl border border-neutral-200 p-5">
            <h2 className="text-base font-semibold text-neutral-950">Job detail</h2>

            {!selectedJobId && (
              <p className="mt-3 text-sm text-neutral-500">
                Pick a job from the table to inspect outputs and metadata.
              </p>
            )}

            {selectedJobQuery.isLoading && selectedJobId && (
              <p className="mt-3 text-sm text-neutral-500">Loading job detail...</p>
            )}

            {selectedJobQuery.isError && (
              <p className="mt-3 rounded-lg border border-rose-200 px-3 py-2 text-sm text-rose-700">
                {getErrorMessage(selectedJobQuery.error)}
              </p>
            )}

            {selectedJob && (
              <div className="mt-4 space-y-5">
                <div className="rounded-xl border border-neutral-200 p-3">
                  <div className="flex flex-wrap items-center justify-between gap-2">
                    <p className="text-xs uppercase tracking-wide text-neutral-500">
                      {selectedJob.id}
                    </p>
                    <span
                      className={`inline-flex rounded-full border px-2.5 py-1 text-xs font-medium ${getStatusClasses(selectedJob.status)}`}
                    >
                      {selectedJob.status}
                    </span>
                  </div>

                  <dl className="mt-3 grid gap-2 text-sm sm:grid-cols-2">
                    <div>
                      <dt className="text-neutral-500">Created</dt>
                      <dd className="text-neutral-800">
                        {formatDateTime(selectedJob.createdAt)}
                      </dd>
                    </div>
                    <div>
                      <dt className="text-neutral-500">Started</dt>
                      <dd className="text-neutral-800">
                        {formatDateTime(selectedJob.startedAt)}
                      </dd>
                    </div>
                    <div>
                      <dt className="text-neutral-500">Completed</dt>
                      <dd className="text-neutral-800">
                        {formatDateTime(selectedJob.completedAt)}
                      </dd>
                    </div>
                    <div>
                      <dt className="text-neutral-500">Retry count</dt>
                      <dd className="text-neutral-800">{selectedJob.retryCount}</dd>
                    </div>
                    <div>
                      <dt className="text-neutral-500">AI status</dt>
                      <dd className="text-neutral-800">{selectedJob.aiStatus}</dd>
                    </div>
                    <div>
                      <dt className="text-neutral-500">AI started</dt>
                      <dd className="text-neutral-800">
                        {formatDateTime(selectedJob.aiStartedAt)}
                      </dd>
                    </div>
                    <div>
                      <dt className="text-neutral-500">AI completed</dt>
                      <dd className="text-neutral-800">
                        {formatDateTime(selectedJob.aiCompletedAt)}
                      </dd>
                    </div>
                    <div>
                      <dt className="text-neutral-500">AI retry count</dt>
                      <dd className="text-neutral-800">{selectedJob.aiRetryCount}</dd>
                    </div>
                  </dl>

                  {selectedJob.errorMessage && (
                    <p className="mt-3 rounded-lg border border-rose-200 px-3 py-2 text-sm text-rose-700">
                      {selectedJob.errorMessage}
                    </p>
                  )}

                  {selectedJob.aiErrorMessage && (
                    <p className="mt-3 rounded-lg border border-rose-200 px-3 py-2 text-sm text-rose-700">
                      AI: {selectedJob.aiErrorMessage}
                    </p>
                  )}
                </div>

                <div className="space-y-2">
                  <h3 className="text-sm font-semibold text-neutral-900">Original</h3>
                  <img
                    src={selectedJob.originalUrl}
                    alt={selectedJob.originalFilename}
                    className="max-h-56 w-full rounded-lg border border-neutral-200 object-contain"
                  />
                  <a
                    href={selectedJob.originalUrl}
                    target="_blank"
                    rel="noreferrer"
                    className="text-sm text-neutral-700 underline decoration-neutral-300 underline-offset-3 transition hover:decoration-neutral-700"
                  >
                    Open original asset
                  </a>
                </div>

                {selectedJob.thumbnails &&
                  Object.keys(selectedJob.thumbnails).length > 0 && (
                    <div className="space-y-2">
                      <h3 className="text-sm font-semibold text-neutral-900">
                        Thumbnails
                      </h3>
                      <div className="grid gap-3 sm:grid-cols-2">
                        {Object.entries(selectedJob.thumbnails).map(
                          ([name, url]) => (
                            <article
                              key={name}
                              className="rounded-lg border border-neutral-200 p-2"
                            >
                              <img
                                src={url}
                                alt={name}
                                className="h-28 w-full rounded-md object-cover"
                              />
                              <div className="mt-2 flex items-center justify-between gap-2 text-xs">
                                <span className="font-medium text-neutral-700">
                                  {name}
                                </span>
                                <a
                                  href={url}
                                  target="_blank"
                                  rel="noreferrer"
                                  className="text-neutral-600 underline decoration-neutral-300 underline-offset-3 transition hover:decoration-neutral-700"
                                >
                                  Open
                                </a>
                              </div>
                            </article>
                          ),
                        )}
                      </div>
                    </div>
                  )}

                {selectedJob.optimized &&
                  Object.keys(selectedJob.optimized).length > 0 && (
                    <div className="space-y-2">
                      <h3 className="text-sm font-semibold text-neutral-900">
                        Optimized assets
                      </h3>
                      <ul className="space-y-2 text-sm">
                        {Object.entries(selectedJob.optimized).map(
                          ([format, url]) => (
                            <li
                              key={format}
                              className="flex items-center justify-between gap-3 rounded-lg border border-neutral-200 px-3 py-2"
                            >
                              <span className="font-medium text-neutral-700">
                                {format}
                              </span>
                              <a
                                href={url}
                                target="_blank"
                                rel="noreferrer"
                                className="text-neutral-600 underline decoration-neutral-300 underline-offset-3 transition hover:decoration-neutral-700"
                              >
                                Open
                              </a>
                            </li>
                          ),
                        )}
                      </ul>
                    </div>
                  )}

                {selectedJob.metadata && (
                  <div className="space-y-3">
                    <h3 className="text-sm font-semibold text-neutral-900">
                      Metadata
                    </h3>
                    <dl className="grid gap-2 text-sm sm:grid-cols-2">
                      <div className="rounded-lg border border-neutral-200 px-3 py-2">
                        <dt className="text-neutral-500">Dimensions</dt>
                        <dd className="text-neutral-800">
                          {selectedJob.metadata.width} x {selectedJob.metadata.height}
                        </dd>
                      </div>
                      <div className="rounded-lg border border-neutral-200 px-3 py-2">
                        <dt className="text-neutral-500">Format</dt>
                        <dd className="text-neutral-800">
                          {selectedJob.metadata.format}
                        </dd>
                      </div>
                      <div className="rounded-lg border border-neutral-200 px-3 py-2">
                        <dt className="text-neutral-500">Original size</dt>
                        <dd className="text-neutral-800">
                          {formatBytes(selectedJob.metadata.fileSize)}
                        </dd>
                      </div>
                    </dl>

                    {selectedJob.metadata.dominantColors.length > 0 && (
                      <div>
                        <p className="mb-2 text-xs uppercase tracking-wide text-neutral-500">
                          Dominant colors
                        </p>
                        <div className="flex flex-wrap gap-2">
                          {selectedJob.metadata.dominantColors.map((color) => (
                            <span
                              key={color}
                              className="inline-flex items-center gap-2 rounded-full border border-neutral-200 px-2.5 py-1 text-xs text-neutral-700"
                            >
                              <span
                                className="h-3 w-3 rounded-full border border-neutral-300"
                                style={{ backgroundColor: color }}
                              />
                              {color}
                            </span>
                          ))}
                        </div>
                      </div>
                    )}

                    {Object.keys(selectedJob.metadata.exif).length > 0 && (
                      <div>
                        <p className="mb-2 text-xs uppercase tracking-wide text-neutral-500">
                          EXIF (top 8)
                        </p>
                        <ul className="space-y-1 text-sm">
                          {Object.entries(selectedJob.metadata.exif)
                            .slice(0, 8)
                            .map(([key, value]) => (
                              <li
                                key={key}
                                className="flex items-start justify-between gap-3 rounded-md border border-neutral-200 px-3 py-2"
                              >
                                <span className="text-neutral-600">{key}</span>
                                <span className="text-right text-neutral-800">
                                  {value ?? 'N/A'}
                                </span>
                              </li>
                            ))}
                        </ul>
                      </div>
                    )}
                  </div>
                )}

                {selectedJob.aiAnalysis && (
                  <div className="space-y-3">
                    <h3 className="text-sm font-semibold text-neutral-900">
                      AI analysis
                    </h3>

                    <div className="rounded-lg border border-neutral-200 px-3 py-2">
                      <p className="text-xs uppercase tracking-wide text-neutral-500">
                        Summary
                      </p>
                      <p className="mt-1 text-sm text-neutral-800">
                        {selectedJob.aiAnalysis.summary}
                      </p>
                    </div>

                    <div className="rounded-lg border border-neutral-200 px-3 py-2">
                      <p className="text-xs uppercase tracking-wide text-neutral-500">
                        OCR text
                      </p>
                      <p className="mt-1 whitespace-pre-wrap text-sm text-neutral-800">
                        {selectedJob.aiAnalysis.ocrText ?? 'No text detected.'}
                      </p>
                    </div>

                    {selectedJob.aiAnalysis.tags.length > 0 && (
                      <div>
                        <p className="mb-2 text-xs uppercase tracking-wide text-neutral-500">
                          Structured tags
                        </p>
                        <div className="flex flex-wrap gap-2">
                          {selectedJob.aiAnalysis.tags.map((tag) => (
                            <span
                              key={`${tag.label}-${tag.confidence}`}
                              className="inline-flex items-center gap-2 rounded-full border border-neutral-200 px-2.5 py-1 text-xs text-neutral-700"
                            >
                              <span>{tag.label}</span>
                              <span className="text-neutral-500">
                                {(tag.confidence * 100).toFixed(0)}%
                              </span>
                            </span>
                          ))}
                        </div>
                      </div>
                    )}

                    <div>
                      <p className="mb-2 text-xs uppercase tracking-wide text-neutral-500">
                        Safety flags
                      </p>
                      <ul className="space-y-1 text-sm">
                        <li className="flex items-center justify-between rounded-md border border-neutral-200 px-3 py-2">
                          <span className="text-neutral-600">Adult</span>
                          <span className="text-neutral-800">
                            {selectedJob.aiAnalysis.safety.adult
                              ? 'Detected'
                              : 'Not detected'}
                          </span>
                        </li>
                        <li className="flex items-center justify-between rounded-md border border-neutral-200 px-3 py-2">
                          <span className="text-neutral-600">Violence</span>
                          <span className="text-neutral-800">
                            {selectedJob.aiAnalysis.safety.violence
                              ? 'Detected'
                              : 'Not detected'}
                          </span>
                        </li>
                        <li className="flex items-center justify-between rounded-md border border-neutral-200 px-3 py-2">
                          <span className="text-neutral-600">Self harm</span>
                          <span className="text-neutral-800">
                            {selectedJob.aiAnalysis.safety.selfHarm
                              ? 'Detected'
                              : 'Not detected'}
                          </span>
                        </li>
                      </ul>
                    </div>

                    <div>
                      <p className="mb-2 text-xs uppercase tracking-wide text-neutral-500">
                        AI meta
                      </p>
                      <ul className="space-y-1 text-sm">
                        <li className="flex items-center justify-between rounded-md border border-neutral-200 px-3 py-2">
                          <span className="text-neutral-600">Model</span>
                          <span className="text-neutral-800">
                            {selectedJob.aiAnalysis.meta.model}
                          </span>
                        </li>
                        <li className="flex items-center justify-between rounded-md border border-neutral-200 px-3 py-2">
                          <span className="text-neutral-600">Latency</span>
                          <span className="text-neutral-800">
                            {selectedJob.aiAnalysis.meta.latencyMs}ms
                          </span>
                        </li>
                        <li className="flex items-center justify-between rounded-md border border-neutral-200 px-3 py-2">
                          <span className="text-neutral-600">Input tokens</span>
                          <span className="text-neutral-800">
                            {selectedJob.aiAnalysis.meta.inputTokens ?? '--'}
                          </span>
                        </li>
                        <li className="flex items-center justify-between rounded-md border border-neutral-200 px-3 py-2">
                          <span className="text-neutral-600">Output tokens</span>
                          <span className="text-neutral-800">
                            {selectedJob.aiAnalysis.meta.outputTokens ?? '--'}
                          </span>
                        </li>
                        <li className="flex items-center justify-between rounded-md border border-neutral-200 px-3 py-2">
                          <span className="text-neutral-600">Estimated cost</span>
                          <span className="text-neutral-800">
                            {selectedJob.aiAnalysis.meta.estimatedCostUsd !== null
                              ? `$${selectedJob.aiAnalysis.meta.estimatedCostUsd.toFixed(6)}`
                              : '--'}
                          </span>
                        </li>
                      </ul>
                    </div>
                  </div>
                )}
              </div>
            )}
          </section>
        </div>
      </section>
    </main>
  )
}

function MetricCard({ label, value }: { label: string; value: string }) {
  return (
    <article className="rounded-xl border border-neutral-200 p-4">
      <p className="text-xs uppercase tracking-wide text-neutral-500">{label}</p>
      <p className="mt-2 text-2xl font-semibold text-neutral-900">{value}</p>
    </article>
  )
}
