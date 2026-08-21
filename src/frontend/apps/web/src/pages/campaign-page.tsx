import {
  createCampaignClient,
  type Campaign,
  type CampaignInvitation,
  type CampaignMember,
} from "@vtt/api-client";
import { type FormEvent, useEffect, useMemo, useState } from "react";

interface CampaignPageProps {
  readonly apiBaseUrl: string;
  readonly campaignId: string;
  readonly onBack: () => void;
}

export function CampaignPage({ apiBaseUrl, campaignId, onBack }: CampaignPageProps) {
  const client = useMemo(() => createCampaignClient(apiBaseUrl), [apiBaseUrl]);
  const [campaign, setCampaign] = useState<Campaign>();
  const [members, setMembers] = useState<CampaignMember[]>([]);
  const [invitations, setInvitations] = useState<CampaignInvitation[]>([]);
  const [rawInvite, setRawInvite] = useState("");
  const [message, setMessage] = useState("");

  useEffect(() => {
    void Promise.all([client.get(campaignId), client.members(campaignId)])
      .then(([loadedCampaign, loadedMembers]) => {
        setCampaign(loadedCampaign);
        setMembers(loadedMembers);
        if (loadedCampaign.effectiveCapabilities.includes("invitations.manage")) {
          void client.invitations(campaignId).then(setInvitations);
        }
      })
      .catch(() => setMessage("Кампания не найдена или доступ отозван."));
  }, [campaignId, client]);

  const lifecycle = async (action: "activate" | "archive" | "restore") => {
    if (!campaign) return;
    try {
      setCampaign(await client.lifecycle(campaign, action));
      setMessage("Состояние кампании обновлено.");
    } catch {
      setMessage("Состояние уже изменилось или правила кампании не готовы.");
    }
  };

  const createInvite = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const role = String(new FormData(event.currentTarget).get("role"));
    try {
      const invitation = await client.createInvitation(campaignId, role);
      setRawInvite(invitation.token ?? "");
      setInvitations((items) => [
        {
          invitationId: invitation.invitationId,
          role: invitation.role,
          expiresAt: invitation.expiresAt,
          maxUses: invitation.maxUses,
          version: invitation.version,
          status: "Active",
          useCount: 0,
        },
        ...items,
      ]);
      setCampaign(await client.get(campaignId));
      setMessage("Код показан один раз. Передайте его игроку безопасным каналом.");
    } catch {
      setMessage("Не удалось создать приглашение.");
    }
  };

  const changeRole = async (member: CampaignMember, role: string) => {
    if (
      !window.confirm(
        `Изменить роль участника на ${role}? Набор доступных действий изменится сразу.`,
      )
    )
      return;
    try {
      const updated = await client.changeMember(campaignId, member, role, member.status);
      setMembers((items) =>
        items.map((item) => (item.membershipId === updated.membershipId ? updated : item)),
      );
      setMessage("Роль и policy revision обновлены.");
    } catch {
      setMessage(
        "Изменение конфликтует с другой вкладкой или нарушает правило единственного Owner.",
      );
    }
  };

  if (!campaign) {
    return (
      <main id="main-content" className="page">
        <button className="secondary-button" onClick={onBack}>
          ← К списку
        </button>
        <p role="status">{message || "Загружаем кампанию…"}</p>
      </main>
    );
  }

  const canManageMembers = campaign.effectiveCapabilities.includes("members.manage");
  const canManageInvites = campaign.effectiveCapabilities.includes("invitations.manage");

  return (
    <main id="main-content" className="page">
      <button className="secondary-button" onClick={onBack}>
        ← К кампаниям
      </button>
      <div className="page-heading campaign-heading">
        <div>
          <p className="eyebrow">
            {campaign.role} · {campaign.status}
          </p>
          <h1>{campaign.name}</h1>
        </div>
        <span className="health-pill health-healthy">Policy r{campaign.policyRevision}</span>
      </div>
      <div className="campaign-actions" aria-label="Управление состоянием кампании">
        {campaign.status === "Draft" &&
          campaign.effectiveCapabilities.includes("campaign.activate") && (
            <button onClick={() => void lifecycle("activate")}>Активировать</button>
          )}
        {campaign.status !== "Archived" &&
          campaign.effectiveCapabilities.includes("campaign.archive") && (
            <button className="secondary-button" onClick={() => void lifecycle("archive")}>
              Архивировать
            </button>
          )}
        {campaign.status === "Archived" &&
          campaign.effectiveCapabilities.includes("campaign.restore") && (
            <button onClick={() => void lifecycle("restore")}>Восстановить</button>
          )}
      </div>
      <section className="diagnostic-panel">
        <h2>Участники</h2>
        {members.map((member) => (
          <div className="session-row" key={member.membershipId}>
            <span>
              <strong>{member.userId.slice(0, 8)}</strong>
              <br />
              {member.status}
            </span>
            {canManageMembers && member.role !== "Owner" ? (
              <label className="compact-field">
                Роль
                <select
                  value={member.role}
                  onChange={(event) => void changeRole(member, event.target.value)}
                >
                  <option value="CoGm">CoGM</option>
                  <option value="Player">Player</option>
                  <option value="Observer">Observer</option>
                </select>
              </label>
            ) : (
              <span>{member.role}</span>
            )}
          </div>
        ))}
      </section>
      {canManageInvites && (
        <section className="diagnostic-panel">
          <h2>Приглашения</h2>
          <form className="inline-form" onSubmit={createInvite}>
            <label>
              Роль
              <select name="role" defaultValue="Player">
                <option value="CoGm">CoGM</option>
                <option value="Player">Player</option>
                <option value="Observer">Observer</option>
              </select>
            </label>
            <button>Создать одноразовый код</button>
          </form>
          {rawInvite && (
            <output className="invite-token" aria-label="Одноразовый код приглашения">
              {rawInvite}
            </output>
          )}
          {invitations.map((invite) => (
            <p key={invite.invitationId}>
              {invite.role} · {invite.status} · использовано {invite.useCount ?? 0}/{invite.maxUses}
            </p>
          ))}
        </section>
      )}
      {message && (
        <p role="status" className="form-message">
          {message}
        </p>
      )}
    </main>
  );
}
