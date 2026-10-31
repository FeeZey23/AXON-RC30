import fs from 'node:fs';import path from 'node:path';import assert from 'node:assert/strict';import {fileURLToPath} from 'node:url';import {execFileSync} from 'node:child_process';
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');const read=p=>fs.readFileSync(path.join(root,p),'utf8');
execFileSync(process.execPath,[path.join(root,'scripts','Update-Documentation.mjs'),'--check'],{stdio:'inherit'});
const text=read('README.md');const labels=[...text.matchAll(/<summary><strong>(.*?)<\/strong><\/summary>/g)].map(m=>m[1]);
assert.deepEqual(labels,['AXON RC30 - English Description and Overview','AXON RC30 - Step.by.Step.Guide','AXON RC30 - Deutsche Beschreibung und Übersicht','AXON RC30 - Schritt.für.Schritt.Anleitung']);assert(!/<details\s+open|<script/i.test(text));
for(const lang of ['en','de']){const guide=read('docs/STEP-BY-STEP.'+lang+'.md');assert.equal([...guide.matchAll(/^### \d+\./gm)].length,9);for(const word of ['Get-FileHash','Connect','WebView2','SkipUiSmoke','AXON%20RC30.sln'])assert(guide.includes(word));}
function walk(dir){return fs.readdirSync(dir,{withFileTypes:true}).flatMap(d=>{if(['.git','.artifacts','releases','bin','obj','node_modules','__pycache__'].includes(d.name))return [];const p=path.join(dir,d.name);return d.isDirectory()?walk(p):p.endsWith('.md')?[p]:[];});}
for(const file of walk(root)){for(const m of fs.readFileSync(file,'utf8').matchAll(/\]\(([^\s)]+)\)/g)){const value=m[1].split('#')[0];if(!value||/^[a-z][a-z0-9+.-]*:/i.test(value))continue;const p=path.resolve(path.dirname(file),decodeURIComponent(value));assert(p.startsWith(root+path.sep)&&fs.existsSync(p),'Missing/unsafe link: '+p);}}
assert.equal(read('AXON RC30.sln'),read('ArenaDesktopCompanion.sln'));assert(read('AXON RC30.sln').includes('= "AXON RC30",'));
assert(!/^\s*(push|pull_request|release):/m.test(read('.github/workflows/windows-validation.yml')));
console.log('PASS documentation: canonical generation, 4 dropdowns, bilingual 9-step guides, links, named solution and manual CI');
