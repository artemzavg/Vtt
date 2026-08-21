import { createCampaignClient, IdentityApiError, type Campaign } from "@vtt/api-client";
import { type FormEvent, useEffect, useMemo, useState } from "react";

interface CampaignsPageProps {
  readonly apiBaseUrl: string;
  readonly onOpen: (campaignId: string) => void;
  readonly onUnauthorized: () => void;
}

export function CampaignsPage({ apiBaseUrl, onOpen, onUnauthorized }: CampaignsPageProps) {
  const client = useMemo(() => createCampaignClient(apiBaseUrl), [apiBaseUrl]);
  const [campaigns, setCampaigns] = useState<Campaign[]>([]);
  const [loading, setLoading] = useState(true);
  const [message, setMessage] = useState("");

  useEffect(() => {
    void client
      .list()
      .then(setCampaigns)
      .catch((error: unknown) => {
        if (error instanceof IdentityApiError && error.status === 401) onUnauthorized();
        else setMessage("Не удалось загрузить кампании. Прямые ссылки остаются доступны.");
      })
      .finally(() => setLoading(false));
  }, [client, onUnauthorized]);

  const createCampaign = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const form = event.currentTarget;
    const name = String(new FormData(form).get("name"));
    try {
      const campaign = await client.create(name);
      setCampaigns((items) => [campaign, ...items]);
      form.reset();
      setMessage("Черновик кампании создан.");
    } catch {
      setMessage("Не удалось создать кампанию.");
    }
  };

  const acceptInvite = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const form = event.currentTarget;
    const token = String(new FormData(form).get("token"));
    try {
      const campaign = await client.acceptInvitation(token);
      setCampaigns((items) => [
        campaign,
        ...items.filter((item) => item.campaignId !== campaign.campaignId),
      ]);
      form.reset();
      setMessage("Приглашение принято.");
    } catch {
      setMessage("Приглашение недействительно или уже использовано.");
    }
  };

  return (
    <main id="main-content" className="page">
      <div className="page-heading">
        <div>
          <p className="eyebrow">Игровые миры</p>
          <h1>Мои кампании</h1>
        </div>
      </div>

      <div className="campaign-layout">
        <section className="diagnostic-panel" aria-labelledby="campaign-list-heading">
          <h2 id="campaign-list-heading">Доступные кампании</h2>
          {loading && <p role="status">Загружаем кампании…</p>}
          {!loading && campaigns.length === 0 && (
            <p>Пока нет кампаний. Создайте первую или примите приглашение.</p>
          )}
          <div className="campaign-grid">
            {campaigns.map((campaign) => (
              <button
                className="campaign-card"
                key={campaign.campaignId}
                onClick={() => onOpen(campaign.campaignId)}
              >
                <span className="eyebrow">
                  {campaign.role} · {campaign.status}
                </span>
                <strong>{campaign.name}</strong>
                <span>Policy revision {campaign.policyRevision}</span>
              </button>
            ))}
          </div>
        </section>

        <aside>
          <section className="diagnostic-panel">
            <h2>Новая кампания</h2>
            <form onSubmit={createCampaign}>
              <label>
                Название
                <input name="name" minLength={2} maxLength={120} required />
              </label>
              <button>Создать черновик</button>
            </form>
          </section>
          <section className="diagnostic-panel">
            <h2>Принять приглашение</h2>
            <form onSubmit={acceptInvite}>
              <label>
                Код приглашения
                <input name="token" autoComplete="off" required />
              </label>
              <button>Присоединиться</button>
            </form>
          </section>
        </aside>
      </div>
      {message && (
        <p role="status" className="form-message">
          {message}
        </p>
      )}
    </main>
  );
}
