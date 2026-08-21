import { IdentityApiError } from "./identity";

export interface Campaign {
  campaignId: string;
  ownerId: string;
  name: string;
  description: string;
  locale: string;
  timeZone: string;
  rulesetVersionId?: string;
  status: "Draft" | "Active" | "Archived";
  automationLevel: string;
  dicePolicy: string;
  version: number;
  policyRevision: number;
  role: "Owner" | "CoGm" | "Player" | "Observer";
  effectiveCapabilities: string[];
}

export interface CampaignMember {
  membershipId: string;
  userId: string;
  role: Campaign["role"];
  status: "Active" | "Suspended" | "Left" | "Removed";
  version: number;
  joinedAt: string;
}

export interface CampaignInvitation {
  invitationId: string;
  token?: string;
  role: string;
  expiresAt: string;
  maxUses: number;
  useCount?: number;
  status?: string;
  version: number;
}

export function createCampaignClient(apiBaseUrl: string) {
  const request = async <T>(path: string, init?: RequestInit): Promise<T> => {
    const csrf = document.cookie
      .split("; ")
      .find((cookie) => cookie.startsWith("vtt.csrf="))
      ?.split("=")[1];
    const response = await fetch(`${apiBaseUrl}${path}`, {
      ...init,
      credentials: "include",
      headers: {
        "Content-Type": "application/json",
        ...(csrf ? { "X-CSRF-Token": decodeURIComponent(csrf) } : {}),
        ...init?.headers,
      },
    });
    if (!response.ok) {
      const problem = (await response.json().catch(() => ({}))) as { code?: string };
      throw new IdentityApiError(response.status, problem.code ?? "campaign.request_failed");
    }
    return response.status === 204 ? (undefined as T) : ((await response.json()) as T);
  };

  return {
    list: () => request<Campaign[]>("/v1/campaigns"),
    get: (campaignId: string) => request<Campaign>(`/v1/campaigns/${campaignId}`),
    create: (name: string) =>
      request<Campaign>("/v1/campaigns", {
        method: "POST",
        body: JSON.stringify({
          name,
          locale: "ru-RU",
          timeZone: "Europe/Moscow",
          rulesetVersionId: "dnd5e-srd@1.0.0",
        }),
      }),
    lifecycle: (campaign: Campaign, action: "activate" | "archive" | "restore") =>
      request<Campaign>(`/v1/campaigns/${campaign.campaignId}:${action}`, {
        method: "POST",
        headers: { "If-Match": `"${campaign.version}"` },
      }),
    members: (campaignId: string) =>
      request<CampaignMember[]>(`/v1/campaigns/${campaignId}/members`),
    changeMember: (campaignId: string, member: CampaignMember, role: string, status: string) =>
      request<CampaignMember>(`/v1/campaigns/${campaignId}/members/${member.membershipId}`, {
        method: "PATCH",
        headers: { "If-Match": `"${member.version}"` },
        body: JSON.stringify({ role, status }),
      }),
    invitations: (campaignId: string) =>
      request<CampaignInvitation[]>(`/v1/campaigns/${campaignId}/invitations`),
    createInvitation: (campaignId: string, role: string) =>
      request<CampaignInvitation>(`/v1/campaigns/${campaignId}/invitations`, {
        method: "POST",
        body: JSON.stringify({
          role,
          expiresAt: new Date(Date.now() + 24 * 60 * 60 * 1000).toISOString(),
          maxUses: 1,
        }),
      }),
    acceptInvitation: (token: string) =>
      request<Campaign>("/v1/invitations:accept", {
        method: "POST",
        body: JSON.stringify({ token }),
      }),
  };
}
