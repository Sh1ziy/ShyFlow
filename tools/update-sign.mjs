// Подпись установщика для автообновления (Island\Services\Updater.cs). Нужен только Node, закрытый ключ на GitHub НЕ кладём.
//   node tools/update-sign.mjs keygen <файл-ключа>            создать ключ; открытый ключ вписать в Updater.UpdateKey
//   node tools/update-sign.mjs sign <файл-ключа> <exe> <тег>  создать <exe>.sig
//   node tools/update-sign.mjs verify <открытый-ключ> <exe> <тег>
// Пароль ключа берётся из SHYFLOW_SIGN_PASS или спрашивается (при вводе виден на экране).
// Потеря ключа: новые версии нельзя будет доставить автообновлением, храни копию отдельно от компьютера.
// Выпуск: тег -> GitHub собирает релиз -> скачать ShyFlow-Setup.exe -> sign -> gh release upload <тег> ShyFlow-Setup.exe.sig
import { createHash, createPrivateKey, createPublicKey, generateKeyPairSync, sign, verify } from 'node:crypto';
import { existsSync, readFileSync, writeFileSync } from 'node:fs';
import { basename } from 'node:path';
import { createInterface } from 'node:readline/promises';

const [cmd, a, b, tag] = process.argv.slice(2);

async function pass() {
  if (process.env.SHYFLOW_SIGN_PASS) return process.env.SHYFLOW_SIGN_PASS;
  const rl = createInterface({ input: process.stdin, output: process.stdout });
  const p = await rl.question('Пароль ключа: ');
  rl.close();
  if (!p) throw new Error('Нужен пароль');
  return p;
}

const message = (exe, t) =>
  Buffer.from(`${basename(exe)}|${t}|${createHash('sha256').update(readFileSync(exe)).digest('hex')}`);

if (cmd === 'keygen' && a) {
  if (existsSync(a)) throw new Error('Файл уже есть, не перезаписываю: ' + a);
  const passphrase = await pass();
  const { publicKey, privateKey } = generateKeyPairSync('ec', { namedCurve: 'P-256' });
  writeFileSync(a, privateKey.export({ type: 'pkcs8', format: 'pem', cipher: 'aes-256-cbc', passphrase }), { flag: 'wx' });
  console.log('Открытый ключ для Updater.UpdateKey:\n' + publicKey.export({ type: 'spki', format: 'der' }).toString('base64'));
} else if (cmd === 'sign' && a && b && tag) {
  const key = createPrivateKey({ key: readFileSync(a), passphrase: await pass() });
  writeFileSync(b + '.sig', sign('sha256', message(b, tag), key));
  console.log('Готово: ' + b + '.sig');
} else if (cmd === 'verify' && a && b && tag) {
  const key = createPublicKey({ key: Buffer.from(a, 'base64'), format: 'der', type: 'spki' });
  const ok = verify('sha256', message(b, tag), key, readFileSync(b + '.sig'));
  console.log(ok ? 'Подпись верна' : 'Подпись НЕ верна');
  process.exitCode = ok ? 0 : 1;
} else {
  console.log('keygen <файл> | sign <файл> <exe> <тег> | verify <открытый-ключ> <exe> <тег>');
  process.exitCode = 2;
}
