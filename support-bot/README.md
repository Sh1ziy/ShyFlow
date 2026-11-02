# Бот поддержки ShyFlow (@ShyFlow_app) на Cloudflare Workers

Токен бота хранится только в Cloudflare (секрет), в ShyFlow его нет. Бесплатного тарифа Workers хватает с запасом.

## 0. Новый токен
Старый токен засвечен (его вставляли в чат). В Telegram: **@BotFather → /revoke → @ShyFlow_app** — получишь новый токен.
Его никуда, кроме шага 3, не вставляй.

## 1. Worker
1. Зайди на https://dash.cloudflare.com (зарегистрируйся, если нет аккаунта).
2. **Workers & Pages → Create → Create Worker**, имя `shyflow-support`, **Deploy**.
3. **Edit code**, удали всё, вставь содержимое `worker.js` из этой папки, **Deploy**.

## 2. Хранилище (KV)
1. **Storage & Databases → KV → Create**, имя `shyflow-support`.
2. Открой Worker `shyflow-support` → **Settings → Bindings → Add → KV namespace**:
   имя переменной **`KV`**, namespace `shyflow-support`. Сохрани.

## 3. Секреты
Worker → **Settings → Variables and Secrets → Add**, тип **Secret**:
- `BOT_TOKEN` — новый токен из шага 0;
- `WEBHOOK_SECRET` — любая длинная случайная строка из букв и цифр (например 40 символов). Запомни её на шаг 4.

## 4. Подключить к Telegram
Открой в браузере (подставь свой адрес Worker и WEBHOOK_SECRET):

```
https://shyflow-support.<твой-поддомен>.workers.dev/setup?key=<WEBHOOK_SECRET>
```

Должно быть `"webhook":{"ok":true,...}`, `"kv":"ok"`, `"token":"ok"`.

## 5. Адрес в ShyFlow
Пришли адрес Worker (`https://shyflow-support.<...>.workers.dev`) — он вписывается в `Island\AppInfo.cs` (`SupportUrl`).

## Как пользоваться
- Пользователь: правый клик по острову → **«Поддержка»** → пишет вопрос. Ответ приходит в то же окно и уведомлением в остров.
- Админ получает сообщение в боте и отвечает **обязательно реплаем** (свайп или «Ответить»). Без реплая бот ответ не отправит и скажет об этом.
- Админы с самого начала: `6262106938`. Добавлять и убирать админов могут `6262106938` и `6967265002`:
  `/addadmin 123456789`, `/deladmin 123456789`, `/admins` — список.
- Защита от спама: не больше 5 сообщений в минуту с одного IP и 30 со всех, до 2000 символов. После обновления `worker.js` вставь его в Worker заново и нажми **Deploy**.
- Защита от настоящей атаки: Cloudflare → **Security → WAF → Rate limiting rules**, правило на путь `/api/ticket` (счётчики в KV приблизительные).
