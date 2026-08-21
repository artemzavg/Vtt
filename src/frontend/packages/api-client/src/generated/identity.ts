// Generated contract surface for contracts/openapi/identity-bff.v1.json.
// Network behavior is intentionally centralized here so UI code never handles session credentials.

export interface UserProfile {
  userId: string;
  displayName: string;
  locale: string;
  timeZone: string;
  version: number;
}

export interface BrowserSession {
  expiresAt: string;
  user: UserProfile;
}

export interface LoginSession {
  sessionId: string;
  deviceLabel: string;
  createdAt: string;
  lastUsedAt: string;
  expiresAt: string;
  isCurrent: boolean;
  isRevoked: boolean;
}

export class IdentityApiError extends Error {
  constructor(
    readonly status: number,
    readonly code: string,
  ) {
    super(code);
  }
}

export function createIdentityClient(apiBaseUrl: string) {
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
      throw new IdentityApiError(response.status, problem.code ?? "identity.request_failed");
    }
    return response.status === 204 ? (undefined as T) : ((await response.json()) as T);
  };

  return {
    register: (input: Record<string, string>) =>
      request<unknown>("/v1/auth/registrations", { method: "POST", body: JSON.stringify(input) }),
    login: (email: string, password: string) =>
      request<BrowserSession>("/v1/auth/login", {
        method: "POST",
        body: JSON.stringify({ email, password, deviceLabel: navigator.userAgent }),
      }),
    requestReset: (email: string) =>
      request<unknown>("/v1/auth/password-resets", { method: "POST", body: JSON.stringify({ email }) }),
    completeReset: (challengeToken: string, newPassword: string) =>
      request<void>("/v1/auth/password-resets/complete", {
        method: "POST",
        body: JSON.stringify({ challengeToken, newPassword }),
      }),
    verifyEmail: (challengeToken: string) =>
      request<void>("/v1/auth/email-verifications", {
        method: "POST",
        body: JSON.stringify({ challengeToken }),
      }),
    me: () => request<UserProfile>("/v1/me"),
    logout: () => request<void>("/v1/auth/logout", { method: "POST" }),
    updateProfile: (profile: UserProfile) =>
      request<UserProfile>("/v1/me", {
        method: "PUT",
        body: JSON.stringify({
          displayName: profile.displayName,
          locale: profile.locale,
          timeZone: profile.timeZone,
          version: profile.version,
        }),
      }),
    sessions: () => request<LoginSession[]>("/v1/me/sessions"),
    revokeSession: (id: string) => request<void>(`/v1/me/sessions/${id}`, { method: "DELETE" }),
    revokeOtherSessions: () => request<void>("/v1/me/sessions", { method: "DELETE" }),
  };
}
