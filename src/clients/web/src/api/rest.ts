import { formatPairingCode } from "../features/channels/pairingFormat";
import { clearSession, getAccessToken, refresh } from "./session";

export function getApiBaseUrl(): string {
  const raw = import.meta.env.VITE_API_BASE_URL?.trim() ?? "";
  if (!raw) {
    return "";
  }
  return raw.replace(/\/$/, "");
}

export function getBearerToken(): string {
  return import.meta.env.VITE_BEARER_TOKEN?.trim() ?? "";
}

export function hasBearerToken(): boolean {
  return getBearerToken().length > 0;
}

export class ApiError extends Error {
  readonly status: number;
  readonly code?: string;

  constructor(message: string, status: number, code?: string) {
    super(message);
    this.name = "ApiError";
    this.status = status;
    this.code = code;
  }
}

async function authorizedFetch(
  path: string,
  init?: RequestInit,
  retry = true,
): Promise<Response> {
  const token = getAccessToken();
  if (!token) {
    clearSession();
    throw new ApiError("No se pudo entrar.", 401);
  }

  const response = await fetch(`${getApiBaseUrl()}${path}`, {
    ...init,
    headers: {
      Accept: "application/json",
      Authorization: `Bearer ${token}`,
      ...(init?.headers ?? {}),
    },
  });

  if (response.status === 401 && !getBearerToken()) {
    if (retry) {
      const refreshed = await refresh();
      if (refreshed) {
        return authorizedFetch(path, init, false);
      }
    }

    clearSession();
    throw new ApiError("No se pudo entrar.", 401);
  }

  return response;
}

async function readApiError(response: Response): Promise<ApiError> {
  let code: string | undefined;
  try {
    const body = (await response.json()) as { code?: unknown };
    if (typeof body.code === "string") {
      code = body.code;
    }
  } catch {
    code = undefined;
  }

  return new ApiError(`HTTP ${response.status}`, response.status, code);
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await authorizedFetch(path, {
    ...init,
    headers: {
      "Content-Type": "application/json",
      ...(init?.headers ?? {}),
    },
  });

  if (!response.ok) {
    throw await readApiError(response);
  }

  if (response.status === 204) {
    return undefined as T;
  }

  return (await response.json()) as T;
}

export interface ConversationDto {
  id: string;
  title: string | null;
  startedAt: string;
  lastMessageAt: string;
}

export interface MessageDto {
  id: string;
  conversationId: string;
  role: string;
  content: string;
  sequence: number;
  wasInterrupted: boolean;
  metadata: string | null;
  createdAt: string;
}

export interface PersonalityDto {
  profile: {
    name: string;
    language: string;
    timeZone: string;
    relationship: string | null;
    style: {
      formality: string;
      humor: string;
      verbosity: string;
      emoji: string;
    };
    traits: string[];
    boundaries: string[];
    instructions: string | null;
    voice: { provider: string; voiceId: string; speed: number };
    avatar: { model: string; idleAnimation: string };
  };
  systemPrompt: string;
  estimatedTokens: number;
}

export interface UsageByModelDto {
  provider: string;
  model: string;
  inputTokens: number;
  outputTokens: number;
  estimatedCostUsd: number;
}

export interface UsageDto {
  since: string;
  turns: number;
  inputTokens: number;
  outputTokens: number;
  estimatedCostUsd: number;
  monthlyBudgetUsd: number;
  warnAtPercent: number;
  byModel: UsageByModelDto[];
}

export function listConversations(take = 50): Promise<ConversationDto[]> {
  return request(`/api/conversations?take=${take}`);
}

export function createConversation(title?: string | null): Promise<ConversationDto> {
  return request("/api/conversations", {
    method: "POST",
    body: JSON.stringify({ title: title ?? null }),
  });
}

export function getMessages(
  conversationId: string,
  take = 100,
): Promise<MessageDto[]> {
  return request(`/api/conversations/${conversationId}/messages?take=${take}`);
}

export function deleteConversation(conversationId: string): Promise<void> {
  return request(`/api/conversations/${conversationId}`, { method: "DELETE" });
}

export function deleteAllConversations(): Promise<void> {
  return request("/api/conversations", { method: "DELETE" });
}

export function getPersonality(): Promise<PersonalityDto> {
  return request("/api/personality");
}

export function getUsage(since?: string): Promise<UsageDto> {
  const query = since ? `?since=${encodeURIComponent(since)}` : "";
  return request(`/api/usage${query}`);
}

export interface MemoryItemDto {
  id: string;
  user_id: string;
  kind: string;
  content: string;
  source_conversation_id: string | null;
  due_at: string | null;
  created_at: string;
  updated_at: string;
  write_provenance: string;
  pinned: boolean;
  index_terms: string | null;
}

export interface MemoryListDto {
  items: MemoryItemDto[];
  skip: number;
  take: number;
}

export interface PreferenceItemDto {
  id: string;
  user_id: string;
  key: string;
  value: string;
  created_at: string;
  updated_at: string;
}

export function listMemories(skip = 0, take = 50): Promise<MemoryListDto> {
  return request(`/api/memories?skip=${skip}&take=${take}`);
}

export interface MemoryJourneyItemDto {
  id: string;
  user_id: string;
  kind: string;
  content: string | null;
  created_at: string;
  write_provenance: string;
  pinned: boolean;
  index_terms: string | null;
  superseded_by: string | null;
}

export interface MemoryJourneyDto {
  current: MemoryJourneyItemDto[];
  replaced: MemoryJourneyItemDto[];
  forgotten: MemoryJourneyItemDto[];
}

export function listMemoryJourney(): Promise<MemoryJourneyDto> {
  return request("/api/memories/journey");
}

export function restoreMemory(id: string): Promise<void> {
  return request(`/api/memories/${id}/restore`, { method: "POST" });
}

export function deleteMemory(id: string): Promise<void> {
  return request(`/api/memories/${id}`, { method: "DELETE" });
}

export function unpinMemory(id: string): Promise<void> {
  return request(`/api/memories/${id}/pinned`, {
    method: "PUT",
    body: JSON.stringify({ pinned: false }),
  });
}

export function listPreferences(): Promise<PreferenceItemDto[]> {
  return request("/api/preferences");
}

export function upsertPreference(key: string, value: string): Promise<PreferenceItemDto> {
  return request(`/api/preferences/${encodeURIComponent(key)}`, {
    method: "PUT",
    body: JSON.stringify({ value }),
  });
}

export function deletePreference(key: string): Promise<void> {
  return request(`/api/preferences/${encodeURIComponent(key)}`, { method: "DELETE" });
}

export async function downloadMemoryExport(): Promise<void> {
  const response = await authorizedFetch("/api/memories/export");

  if (!response.ok) {
    throw new ApiError(`HTTP ${response.status}`, response.status);
  }

  const blob = await response.blob();
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement("a");
  anchor.href = url;
  anchor.download = "memoria.json";
  document.body.appendChild(anchor);
  anchor.click();
  anchor.remove();
  window.setTimeout(() => URL.revokeObjectURL(url), 0);
}

export interface ExtensionToolDto {
  remoteName: string;
  catalogName: string | null;
  permissionLevel: string;
  outputTrust: string;
  writesDurableState: boolean;
  declared: boolean;
}

export interface ExtensionServerDto {
  id: string;
  enabled: boolean;
  transport: string;
  status: string;
  tools: ExtensionToolDto[];
  oauthConnected: boolean | null;
}

export interface ExtensionInventoryDto {
  servers: ExtensionServerDto[];
}

export interface ClassifyExtensionToolRequest {
  toolName: string;
  permissionLevel: string;
  outputTrust: string;
  writesDurableState: boolean;
}

export function listExtensions(): Promise<ExtensionInventoryDto> {
  return request("/api/extensions");
}

export function startCalendarOAuth(): Promise<{ url: string }> {
  return request("/api/extensions/calendar/oauth/start");
}

export type BriefingFeedbackValue = "done" | "dismissed" | "helpful" | "not_helpful";

export interface BriefingItemDto {
  id: string;
  title: string;
  reason: string;
  triage: string;
  source: string;
  sourceRef: string | null;
  feedback: string | null;
}

export interface BriefingDto {
  id: string;
  localDate: string;
  summary: string;
  today: BriefingItemDto[];
  ahead: BriefingItemDto[];
}

export function getTodayBriefing(): Promise<BriefingDto | null> {
  return request("/api/briefings/today");
}

export function setBriefingFeedback(
  briefingId: string,
  itemId: string,
  feedback: BriefingFeedbackValue,
): Promise<void> {
  return request(`/api/briefings/${briefingId}/items/${itemId}/feedback`, {
    method: "POST",
    body: JSON.stringify({ feedback }),
  });
}

export interface DiaryEntryDto {
  id: string;
  proposalId: string | null;
  previousId: string | null;
  successorId: string | null;
  reason: string;
  outcome: string;
  proposalStatus: string | null;
  createdAt: string;
}

export interface DiaryActionDto {
  outcome: string;
  toolName: string | null;
}

export function listDiary(): Promise<{ entries: DiaryEntryDto[] }> {
  return request("/api/diary");
}

export function approveDiary(proposalId: string): Promise<DiaryActionDto> {
  return request(`/api/diary/${proposalId}/approve`, { method: "POST", body: "{}" });
}

export function rejectDiary(proposalId: string): Promise<DiaryActionDto> {
  return request(`/api/diary/${proposalId}/reject`, { method: "POST", body: "{}" });
}

export function undoDiary(entryId: string): Promise<DiaryActionDto> {
  return request(`/api/diary/${entryId}/undo`, { method: "POST", body: "{}" });
}

export function setExtensionEnabled(serverId: string, enabled: boolean): Promise<void> {
  return request(`/api/extensions/${encodeURIComponent(serverId)}/enabled`, {
    method: "PUT",
    body: JSON.stringify({ enabled }),
  });
}

export function classifyExtensionTool(
  serverId: string,
  body: ClassifyExtensionToolRequest,
): Promise<void> {
  return request(`/api/extensions/${encodeURIComponent(serverId)}/tools`, {
    method: "PUT",
    body: JSON.stringify(body),
  });
}

export interface SkillProposalDto {
  id: string;
  name: string;
  description: string;
  createdAt: string;
  diff: string;
  diffSha256: string;
}

export function listSkillProposals(): Promise<SkillProposalDto[]> {
  return request("/api/skills/proposals");
}

export function approveSkillProposal(id: string, diffSha256: string): Promise<void> {
  return request(`/api/skills/proposals/${encodeURIComponent(id)}/approve`, {
    method: "POST",
    body: JSON.stringify({ diffSha256 }),
  });
}

export function discardSkillProposal(id: string): Promise<void> {
  return request(`/api/skills/proposals/${encodeURIComponent(id)}/discard`, {
    method: "POST",
  });
}

export interface ChannelStatusDto {
  id: string;
  enabled: boolean;
  botUsername: string | null;
  linked: boolean;
}

export interface ChannelsDto {
  channels: ChannelStatusDto[];
}

export interface ChannelPeerDto {
  id: string;
  channel: string;
  status: string;
  displayName: string | null;
  username: string | null;
  maskedExternalId: string;
  createdAt: string;
  lastInboundAt: string | null;
}

export interface ChannelPeerListDto {
  peers: ChannelPeerDto[];
}

export interface TelegramLinkDto {
  url: string;
  expiresAt: string;
}

export function listChannels(): Promise<ChannelsDto> {
  return request("/api/channels");
}

export function listChannelPeers(): Promise<ChannelPeerListDto> {
  return request("/api/channels/peers");
}

export function approveChannelPeer(peerId: string, code: string): Promise<void> {
  return request(`/api/channels/peers/${peerId}/approve`, {
    method: "POST",
    body: JSON.stringify({ code: formatPairingCode(code) }),
  });
}

export function revokeChannelPeer(peerId: string): Promise<void> {
  return request(`/api/channels/peers/${peerId}/revoke`, { method: "POST" });
}

export function blockChannelPeer(peerId: string): Promise<void> {
  return request(`/api/channels/peers/${peerId}/block`, { method: "POST" });
}

export function createTelegramLink(): Promise<TelegramLinkDto> {
  return request("/api/channels/telegram/link", { method: "POST" });
}

export interface ReminderItemDto {
  id: string;
  text: string;
  status: string;
  local_due: string;
  relative: string;
  recurrence: string | null;
  channel: string;
}

export interface ReminderListDto {
  items: ReminderItemDto[];
}

export interface NotificationLogItemDto {
  id: string;
  title: string;
  body: string;
  status: string;
  channel: string | null;
  action: string | null;
  local_at: string;
}

export interface NotificationLogDto {
  items: NotificationLogItemDto[];
}

export function listReminders(status: "active" | "missed"): Promise<ReminderListDto> {
  return request(`/api/reminders?status=${status}`);
}

export function cancelReminder(id: string): Promise<void> {
  return request(`/api/reminders/${id}`, { method: "DELETE" });
}

export function listNotifications(limit = 50): Promise<NotificationLogDto> {
  return request(`/api/notifications?limit=${limit}`);
}

export interface ActivityTaskDto {
  id: string;
  kind: string;
  state: string;
  failure_reason: string | null;
  cost_usd: number;
  created_at: string;
  updated_at: string;
  completed_at: string | null;
  partial: string | null;
}

export interface ActivityToolDto {
  name: string;
  extension: string;
  outcome: string;
  at: string;
}

export interface ActivityDto {
  tasks: ActivityTaskDto[];
  tools: ActivityToolDto[];
}

export function getActivity(): Promise<ActivityDto> {
  return request("/api/activity");
}

export function cancelActivity(id: string): Promise<void> {
  return request<void>(`/api/activity/${id}/cancel`, { method: "POST" });
}
