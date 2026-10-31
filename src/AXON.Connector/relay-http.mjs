// Controlled Filebin download redirects. Never replay uploads/deletes or follow arbitrary hosts.
export async function relayHttp(origin,bin,method,name,body,deadline,fetcher=globalThis.fetch){
 const url=new URL(`${origin}/${bin}/${encodeURIComponent(name)}`);
 let response;
 for(let hop=0;;hop++){
  const remaining=deadline-Date.now();if(remaining<=0)throw Error('Connect-Deadline abgelaufen');
  try{response=await fetcher(url.href,{method,headers:{'User-Agent':'AXON-RC30 (curl/8.5.0)','Content-Type':'application/octet-stream'},body,signal:AbortSignal.timeout(Math.max(1,Math.min(12000,remaining))),redirect:'manual'});}
  catch(e){const code=String(e.cause?.code||e.name||'fetch').replace(/[^a-zA-Z0-9_-]/g,'').slice(0,64);throw Error(`Relay-Netzwerkfehler (${code})`,{cause:e});}
  if(![301,302,303,307,308].includes(response.status))break;
  const location=response.headers.get('location');await response.body?.cancel();
  if(method!=='GET'||hop!==0||url.origin!=='https://filebin.net'||!location)throw Error('Relay-Weiterleitung abgewiesen');
  const next=new URL(location,url);
  if(next.protocol!=='https:'||next.hostname!=='storage.filebin.net'||next.port||next.username||next.password)throw Error('Unzulässiges Relay-Weiterleitungsziel');
  url.href=next.href;
 }
 if(response.status===404&&(method==='GET'||method==='DELETE')){await response.body?.cancel();return method==='GET'?null:'';}
 if(response.status===429){await response.body?.cancel();throw Error('Relay überlastet (429); später erneut versuchen');}
 if(!response.ok){await response.body?.cancel();throw Error(`Relay HTTP ${response.status}`);}
 if(method==='DELETE'){await response.body?.cancel();return '';}
 const limit=6*1024*1024,declared=Number(response.headers.get('content-length')||0);
 if(declared>limit){await response.body?.cancel();throw Error('Antwort zu groß');}
 const reader=response.body?.getReader();if(!reader)return '';let size=0;const pieces=[];
 for(;;){const r=await reader.read();if(r.done)break;size+=r.value.length;if(size>limit){await reader.cancel();throw Error('Antwort zu groß');}pieces.push(Buffer.from(r.value));}
 return Buffer.concat(pieces).toString('utf8');
}
