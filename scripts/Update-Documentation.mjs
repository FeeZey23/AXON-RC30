import fs from 'node:fs';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');
const files=['OVERVIEW.en.md','STEP-BY-STEP.en.md','OVERVIEW.de.md','STEP-BY-STEP.de.md'];
let index=0;const old=fs.readFileSync(path.join(root,'README.md'),'utf8');
const updated=old.replace(/(<summary><strong>.*?<\/strong><\/summary>)[\s]*?\n([\s\S]*?)\n<\/details>/g,(match,summary,body)=>{
 const text=fs.readFileSync(path.join(root,'docs',files[index++]),'utf8');
 const canonical=text.slice(text.indexOf('## ')).trim().replace(/\]\(\.\.\//g,'](./');
 return summary+'\n\n'+canonical+'\n\n</details>';
});
if(index!==4)throw Error('Expected exactly four dropdowns');
if(process.argv.includes('--check')){if(updated!==old)throw Error('README is stale. Run node scripts/Update-Documentation.mjs');}
else fs.writeFileSync(path.join(root,'README.md'),updated);
