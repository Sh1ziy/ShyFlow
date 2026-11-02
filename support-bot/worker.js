// Бот поддержки ShyFlow (@ShyFlow_app) на Cloudflare Workers.
//
// Токен бота хранится ТОЛЬКО здесь (секрет BOT_TOKEN), в приложении его нет: ShyFlow лежит на GitHub, и токен из exe
// достали бы за минуту. Приложение говорит только с этим сервером:
//   POST /api/ticket   {client, text, version}  — вопрос пользователя; бот пересылает его всем админам
//   GET  /api/replies?client=...&after=...      — ответы админов этому пользователю
// Telegram шлёт сюда обновления (webhook) на POST /tg с секретом WEBHOOK_SECRET в заголовке.
//
// Админы отвечают ОБЯЗАТЕЛЬНО реплаем на сообщение пользователя — по нему бот понимает, кому ответ. Без реплая ответ
// никуда не уходит, бот так и пишет.
// Команды (только владельцы OWNERS): /addadmin <id>, /deladmin <id>. Всем админам: /admins, /help.
//
// Хранилище — Workers KV (привязка KV):
//   admins            — список id админов (JSON)
//   m:<chat>:<msg>    — сообщение у админа → client пользователя (30 дней)
//   inbox:<client>    — ответы этому пользователю (JSON, последние 50, 60 дней)
//   rl:ip:<хэш IP>    — защита от спама: не больше 5 вопросов в минуту с адреса; rl:all — не больше 30 со всех
//
// Настройка: README.md рядом.

const OWNERS = [6262106938, 6967265002];   // могут добавлять и удалять админов
const DEFAULT_ADMINS = [6262106938];       // админы с самого начала
const MAX_TEXT = 2000;
const MAX_PER_IP = 5;                      // сообщений в минуту с одного IP
const MAX_PER_MINUTE = 30;                 // всего сообщений в минуту со всех установок
const DAY = 86400;

export default {
  async fetch(req, env) {
    const url = new URL(req.url);
    try {
      if (req.method === 'POST' && url.pathname === '/tg') return await onTelegram(req, env);
      if (req.method === 'POST' && url.pathname === '/api/ticket') return await onTicket(req, env);
      if (req.method === 'GET' && url.pathname === '/api/replies') return await onReplies(url, env);
      if (req.method === 'GET' && url.pathname === '/setup') return await setup(url, env);
      return new Response('ShyFlow support bot', { status: 200 });
    } catch (e) {
      console.error(url.pathname, e && e.stack || e);   // видно в wrangler tail
      // Telegram повторяет обновление после 500 снова и снова: ошибку записали, больше не просим
      if (url.pathname === '/tg') return new Response('ok');
      return json({ ok: false, error: 'server' }, 500);
    }
  },
};

// ---------- приложение ----------

const CLIENT_RX = /^[a-f0-9]{32}$/;

async function onTicket(req, env) {
  let body;
  try { body = await req.json(); } catch { return json({ ok: false, error: 'bad_json' }, 400); }
  const client = String(body.client || '');
  const text = String(body.text || '').trim();
  const version = String(body.version || '').replace(/[^0-9A-Za-z.-]/g, '').slice(0, 20);   // без переносов: версия не может притвориться текстом бота
  if (!CLIENT_RX.test(client)) return json({ ok: false, error: 'bad_client' }, 400);
  if (!text) return json({ ok: false, error: 'empty' }, 400);
  if (text.length > MAX_TEXT) return json({ ok: false, error: 'too_long' }, 400);

  // client выдумывает сам клиент, поэтому считаем по IP. Отказ ничего не пишет: спам не расходует лимит записей KV.
  // Счётчики KV приблизительные; против настоящей атаки нужно правило Rate Limiting в Cloudflare (README)
  const ipKey = 'rl:ip:' + await ipHash(req);
  const [byIp, all] = await Promise.all([readCount(env, ipKey), readCount(env, 'rl:all')]);
  if (byIp >= MAX_PER_IP || all >= MAX_PER_MINUTE) return json({ ok: false, error: 'rate' }, 429);
  await Promise.all([bump(env, ipKey, byIp), bump(env, 'rl:all', all)]);

  const admins = await getAdmins(env);
  const head = `💬 Сообщение из ShyFlow ${version}\nПользователь #${client.slice(0, 6)}\n\n`;
  const tail = '\n\n↩️ Чтобы ответить, ответь РЕПЛАЕМ на это сообщение.';
  let sent = 0;
  for (const id of admins) {
    const r = await tg(env, 'sendMessage', { chat_id: id, text: head + text + tail });
    if (r && r.ok) {
      sent++;
      await env.KV.put(`m:${id}:${r.result.message_id}`, client, { expirationTtl: 30 * DAY });
    }
  }
  if (!sent) return json({ ok: false, error: 'no_admins' }, 503);
  return json({ ok: true });
}

async function onReplies(url, env) {
  const client = url.searchParams.get('client') || '';
  const after = Number(url.searchParams.get('after') || '0');
  if (!CLIENT_RX.test(client)) return json({ ok: false, error: 'bad_client' }, 400);
  const inbox = readInbox(await env.KV.get('inbox:' + client));
  return json({ ok: true, replies: inbox.filter(r => r.id > after) });
}

// ---------- Telegram ----------

async function onTelegram(req, env) {
  if (!env.WEBHOOK_SECRET || req.headers.get('X-Telegram-Bot-Api-Secret-Token') !== env.WEBHOOK_SECRET) return new Response('forbidden', { status: 403 });
  const update = await req.json();
  const msg = update.message;
  if (!msg || !msg.from || msg.chat.type !== 'private') return new Response('ok');
  const from = msg.from.id;
  const chat = msg.chat.id;
  const text = (msg.text || '').trim();
  const admins = await getAdmins(env);
  const isAdmin = admins.includes(from);
  const isOwner = OWNERS.includes(from);

  if (text.startsWith('/')) {
    const [cmd, arg] = text.split(/\s+/, 2);
    const name = cmd.split('@')[0].toLowerCase();
    if (name === '/start' || name === '/help') {
      await say(env, chat, isAdmin || isOwner
        ? 'Ты в поддержке ShyFlow. Вопросы из приложения приходят сюда.\n\n' +
          '↩️ Отвечай ОБЯЗАТЕЛЬНО реплаем на сообщение пользователя, иначе ответ не уйдёт.\n\n' +
          '/admins — кто админ' + (isOwner ? '\n/addadmin <id> — добавить админа\n/deladmin <id> — убрать админа' : '')
        : 'Это бот поддержки ShyFlow. Написать нам можно прямо из приложения: правый клик по острову → «Поддержка».');
      return new Response('ok');
    }
    if (name === '/admins' && (isAdmin || isOwner)) {
      await say(env, chat, 'Админы: ' + (admins.length ? admins.join(', ') : 'никого') + '\nМогут добавлять: ' + OWNERS.join(', '));
      return new Response('ok');
    }
    if ((name === '/addadmin' || name === '/deladmin') && isOwner) {
      const id = Number(arg);
      if (!Number.isSafeInteger(id) || id <= 0) { await say(env, chat, `Напиши id: ${name} 123456789`); return new Response('ok'); }
      let list = admins.filter(a => a !== id);
      if (name === '/addadmin') list.push(id);
      await env.KV.put('admins', JSON.stringify(list));
      await say(env, chat, (name === '/addadmin' ? '✅ Добавлен: ' : '🗑 Убран: ') + id + '\nАдмины: ' + (list.join(', ') || 'никого'));
      if (name === '/addadmin') await say(env, id, 'Тебя добавили в поддержку ShyFlow. Вопросы из приложения будут приходить сюда. Отвечай реплаем.');
      return new Response('ok');
    }
    if (name === '/addadmin' || name === '/deladmin') { await say(env, chat, 'Добавлять и убирать админов могут только владельцы.'); return new Response('ok'); }
  }

  if (!isAdmin) {
    await say(env, chat, 'Это бот поддержки ShyFlow. Написать нам можно прямо из приложения: правый клик по острову → «Поддержка».');
    return new Response('ok');
  }

  // админ отвечает: только реплаем на сообщение пользователя
  const replyTo = msg.reply_to_message;
  if (!replyTo) {
    await say(env, chat, '⚠️ Не отправлено. Чтобы ответить пользователю, ответь РЕПЛАЕМ (свайп или «Ответить») на его сообщение.', msg.message_id);
    return new Response('ok');
  }
  const client = await env.KV.get(`m:${chat}:${replyTo.message_id}`);
  if (!client) {
    await say(env, chat, '⚠️ Не отправлено: это не сообщение пользователя (или ему больше 30 дней). Ответь реплаем на сообщение из приложения.', msg.message_id);
    return new Response('ok');
  }
  if (!text) {
    await say(env, chat, '⚠️ Пока можно отвечать только текстом.', msg.message_id);
    return new Response('ok');
  }
  const key = 'inbox:' + client;
  const inbox = readInbox(await env.KV.get(key));
  inbox.push({ id: Date.now(), text: text.slice(0, 4000), at: new Date().toISOString() });
  await env.KV.put(key, JSON.stringify(inbox.slice(-50)), { expirationTtl: 60 * DAY });
  await say(env, chat, '✅ Ответ отправлен пользователю #' + client.slice(0, 6), msg.message_id);
  return new Response('ok');
}

// ---------- разовая настройка: /setup?key=<WEBHOOK_SECRET> ----------

async function setup(url, env) {
  if (!env.WEBHOOK_SECRET || url.searchParams.get('key') !== env.WEBHOOK_SECRET) return new Response('forbidden', { status: 403 });
  const hook = await tg(env, 'setWebhook', {
    url: url.origin + '/tg', secret_token: env.WEBHOOK_SECRET, allowed_updates: ['message'], drop_pending_updates: true,
  });
  await tg(env, 'setMyCommands', { commands: [
    { command: 'help', description: 'Как отвечать' },
    { command: 'admins', description: 'Кто админ' },
    { command: 'addadmin', description: 'Добавить админа (владельцы)' },
    { command: 'deladmin', description: 'Убрать админа (владельцы)' },
  ] });
  const kvOk = !!env.KV;
  return json({ webhook: hook, kv: kvOk ? 'ok' : 'НЕТ привязки KV', token: env.BOT_TOKEN ? 'ok' : 'НЕТ секрета BOT_TOKEN' });
}

// ---------- помощники ----------

async function ipHash(req) {
  const ip = req.headers.get('CF-Connecting-IP') || 'unknown';
  const d = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(ip));   // сам адрес не храним
  return [...new Uint8Array(d)].slice(0, 16).map(b => b.toString(16).padStart(2, '0')).join('');
}

async function readCount(env, key) {
  return parseInt((await env.KV.get(key)) || '0', 10);
}

// KV разрешает одну запись в секунду на ключ: счётчик не сработал — это не повод терять вопрос пользователя
async function bump(env, key, count) {
  try { await env.KV.put(key, String(count + 1), { expirationTtl: 60 }); }
  catch (e) { console.error('rate counter', key, e && e.message || e); }
}

function readInbox(raw) {
  try { const v = JSON.parse(raw || '[]'); return Array.isArray(v) ? v : []; } catch { return []; }
}

async function getAdmins(env) {
  const raw = await env.KV.get('admins');
  if (raw === null) return DEFAULT_ADMINS.slice();
  try { return JSON.parse(raw).map(Number).filter(Number.isSafeInteger); } catch { return DEFAULT_ADMINS.slice(); }
}

async function tg(env, method, payload) {
  const r = await fetch(`https://api.telegram.org/bot${env.BOT_TOKEN}/${method}`, {
    method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(payload),
  });
  try { return await r.json(); } catch { return null; }
}

function say(env, chat, text, replyTo) {
  const p = { chat_id: chat, text };
  if (replyTo) p.reply_parameters = { message_id: replyTo, allow_sending_without_reply: true };
  return tg(env, 'sendMessage', p);
}

function json(obj, status = 200) {
  return new Response(JSON.stringify(obj), { status, headers: { 'content-type': 'application/json; charset=utf-8' } });
}
