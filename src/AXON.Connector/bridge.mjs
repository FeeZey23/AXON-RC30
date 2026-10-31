// AXON RC30: built-in Node modules only; AES-GCM v1 compatible. No npm/pip.
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import {fileURLToPath} from 'node:url';
const home=path.dirname(fileURLToPath(import.meta.url));
const cfg=JSON.parse(fs.readFileSync(path.join(home,'session.json'),'utf8'));
const origin=process.env.AXON_TEST_RELAY || 'https://filebin.net';
if (!/^adc-[a-z0-9_-]{8,60}$/i.test(cfg.Briefkasten)||cfg.Transport!=='aes-gcm-v1'||!cfg.Kennwort||!cfg.Projektordner||!(/^[a-f0-9]{32}$/i.test(cfg.SessionId))||!(/^[a-f0-9]{32}$/i.test(cfg.ConnectId))) throw Error('Ungültige Sitzung; Connect erneut ausführen.');
const key=crypto.createHash('sha256').update('AXON-GCM-v1\0'+cfg.Kennwort).digest();
const norm=x=>String(x).replaceAll('/','\\').replace(/[\\]+$/,'').toLowerCase();
export function seal(name,body){const nonce=crypto.randomBytes(12),cipher=crypto.createCipheriv('aes-256-gcm',key,nonce,{authTagLength:16});cipher.setAAD(Buffer.from('AXON1\0'+cfg.Briefkasten+'\0'+name));const clear=JSON.stringify({ts:Math.floor(Date.now()/1000),body});const data=Buffer.concat([cipher.update(clear,'utf8'),cipher.final(),cipher.getAuthTag()]);return JSON.stringify({v:1,nonce:nonce.toString('base64'),data:data.toString('base64')});}
export function open(name,raw){const e=JSON.parse(raw);if(e.v!==1)throw Error('Unbekannte Transportversion');const n=Buffer.from(e.nonce,'base64'),d=Buffer.from(e.data,'base64');if(n.length!==12||d.length<16)throw Error('Ungültiger Umschlag');const c=crypto.createDecipheriv('aes-256-gcm',key,n,{authTagLength:16});c.setAAD(Buffer.from('AXON1\0'+cfg.Briefkasten+'\0'+name));c.setAuthTag(d.subarray(-16));const clear=JSON.parse(Buffer.concat([c.update(d.subarray(0,-16)),c.final()]).toString('utf8'));if(name==='hello.json'&&(Date.now()/1000-clear.ts>180||clear.ts-Date.now()/1000>300))throw Error('Lebenszeichen veraltet/Uhrabweichung');return clear.body;}
let deadline=Date.now()+60000;
// @AXON_HTTP@
async function http(method,name,body){return relayHttp(origin,cfg.Briefkasten,method,name,body,deadline);}

const sleep=ms=>new Promise(r=>setTimeout(r,Math.max(0,Math.min(ms,deadline-Date.now()))));
async function hello(){const raw=await http('GET','hello.json');if(raw===null)return null;const h=JSON.parse(open('hello.json',raw));if(h.connectionId!==cfg.ConnectId)throw Error('Connect-Bindung geändert: neuen Connect-Text senden. Nicht lokal weiterarbeiten.');if(norm(h.projekt)!==norm(cfg.Projektordner))throw Error('Falsches Projekt. Keine Aktion ausgeführt.');return h;}
async function job(kind,fields={},seconds=60){deadline=Date.now()+seconds*1000;const id=new Date().toISOString().replace(/[-:TZ.]/g,'')+'-'+crypto.randomBytes(6).toString('hex'),name='job-'+id+'.json',result='result-'+id+'.json';const j={...fields,id,kind,projekt:cfg.Projektordner};await http('PUT',name,seal(name,JSON.stringify(j)));while(Date.now()<deadline){const raw=await http('GET',result);if(raw!==null){const r=JSON.parse(open(result,raw));if(r.id!==id)throw Error('Falsche Ergebnis-ID');if(r.status==='ok'&&kind==='ping'&&norm(r.ausgabe)!==norm(cfg.Projektordner))throw Error('Falsche Rückkanalbindung');await http('DELETE',name);await http('DELETE',result);if(r.status!=='ok')throw Error(r.hinweis||r.ausgabe||'Auftrag abgewiesen');return r;}await sleep(1000);}throw Error('Keine Antwort innerhalb der Deadline. Auftrag kann noch ausstehen: '+id);}
async function ready(){deadline=Date.now()+45000;let last='Noch kein Lebenszeichen';while(Date.now()<deadline){try{const h=await hello();if(h)return h;}catch(e){if(/Falsches Projekt|Uhrabweichung|Transportversion|Bindung geändert/.test(e.message))throw e;last=e.message;}await sleep(1000);}throw Error(last+'; Companion/Relay nicht bereit');}
async function handshake(verbose=true){await ready();await job('ping',{note:'AXON-CONNECT:'+cfg.ConnectId},25);await job('ping',{note:'AXON-ACK:'+cfg.ConnectId},25);fs.writeFileSync(path.join(home,'binding.json'),JSON.stringify({session:cfg.SessionId,connect:cfg.ConnectId,root:cfg.Projektordner}),{mode:384});if(verbose)console.log(JSON.stringify({status:'verbunden',projekt:cfg.Projektordner,version:'0.2.0-rc30'}));}
async function projectJob(kind,fields={},seconds=60){
 let receipt;try{receipt=JSON.parse(fs.readFileSync(path.join(home,'binding.json'),'utf8'));}catch{}
 if(!receipt||receipt.session!==cfg.SessionId||receipt.connect!==cfg.ConnectId||norm(receipt.root)!==norm(cfg.Projektordner))await handshake();else await ready();
 try{return await job(kind,fields,seconds);}catch(e){if(e.message!=='AXON_HANDSHAKE_REQUIRED')throw e;await handshake();return await job(kind,fields,seconds);}
}
const [command='status',...args]=process.argv.slice(2);
try{
 if(command==='handshake'||command==='connect')await handshake();
 else if(command==='status'){const only=args.includes('--transport-only');if(!only)await handshake(false);const h=await hello();if(!h)throw Error('Kein Lebenszeichen');console.log(JSON.stringify({...h,status:only?'transport_only':'verbunden',transportOnly:only,pruefung:only?'Nur Lebenszeichen; KEIN Ende-zu-Ende-Nachweis':'Frischer Hin-/Rückkanal mit Projektbindung und ACK bestätigt'},null,2));}
 else if(command==='read'){const r=await projectJob('read',{path:args[0]});if(args[1])fs.writeFileSync(args[1],r.ausgabe||'');else console.log(r.ausgabe||'');}
 else if(command==='write'){if(!args[0]||!args[1])throw Error('write <Projektpfad> <--stdin|temporäre Transferdatei>');const data=args[1]==='--stdin'?fs.readFileSync(0):fs.readFileSync(args[1]);if(data.length>4*1024*1024)throw Error('Datei größer als 4 MiB');console.log(JSON.stringify(await projectJob('write',{path:args[0],content:data.toString('base64'),kodierung:'base64'})));}
 else if(command==='run')console.log((await projectJob('run',{command:args[0]},300)).ausgabe||'');
 else if(command==='vorschlag'){const program=args.shift();console.log((await projectJob('vorschlag',{program,argumente:args,zweck:'Expliziter Agent-Aufruf'},300)).ausgabe||'');}
 else if(command==='ping')console.log(JSON.stringify(await job('ping')));
 else if(command==='stand'||command==='manifest'){const r=await projectJob('manifest');console.log(r.ausgabe||'');}
 else if(command==='look'||command==='blick')console.log((await projectJob('look')).ausgabe||'');
 else throw Error('Befehle: status, handshake, ping, read, write, run, vorschlag, stand, blick');
}catch(e){console.error('AXON: '+e.message);process.exitCode=2;}
