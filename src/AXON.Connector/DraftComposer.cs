using System.Text.Json;
namespace AXON.Connector;

/// <summary>User-requested draft placement only: no send, Enter, history extraction or network calls.</summary>
public static class DraftComposer
{
    public static string Script(string text,Uri expected,bool verifyOnly=false)
    {
        if(text.Length==0)throw new ArgumentException("Leerer Entwurf.",nameof(text));
        var payload=JsonSerializer.Serialize(text);var origin=JsonSerializer.Serialize(expected.GetLeftPart(UriPartial.Authority));var path=JsonSerializer.Serialize(expected.AbsolutePath);var query=JsonSerializer.Serialize(expected.Query);var fragment=JsonSerializer.Serialize(expected.Fragment);
        return "(()=>{const text="+payload+",origin="+origin+",path="+path+",query="+query+",fragment="+fragment+",verify="+(verifyOnly?"true":"false")+";"+Body+"})()";
    }
    private const string Body="""
    const fail=Code=>({Ok:false,Code,Length:0});
    if(location.origin!==origin||location.pathname!==path||location.search!==query||location.hash!==fragment)return fail('chat_changed');
    const key=Symbol.for('axon.rc30.drafts');const previous=window[key]??=new WeakMap();
    const value=e=>{
      if('value'in e)return e.value;
      let text='';const visit=n=>{
        if(n.nodeType===Node.TEXT_NODE){text+=n.data;return;}if(n.nodeType!==Node.ELEMENT_NODE)return;
        if(n!==e&&(n.getAttribute('aria-hidden')==='true'||n.getAttribute('contenteditable')==='false'||getComputedStyle(n).display==='none'))return;
        if(n.tagName==='BR'){text+='\n';return;}
        const block=n!==e&&['block','list-item','table-row'].includes(getComputedStyle(n).display);
        if(block&&text&&!text.endsWith('\n'))text+='\n';for(const child of n.childNodes)visit(child);if(block&&!text.endsWith('\n'))text+='\n';
      };visit(e);return text.replace(/\r\n/g,'\n');
    };
    const candidates=[...document.querySelectorAll('textarea,[contenteditable="true"]')].filter(e=>e.isConnected&&e.getClientRects().length&&getComputedStyle(e).visibility!=='hidden'&&!e.disabled&&!e.readOnly&&!e.closest('[inert]'));
    const score=e=>{const hint=[e.id,e.getAttribute('name'),e.getAttribute('placeholder'),e.getAttribute('aria-label'),e.getAttribute('data-placeholder')].join(' ');if(/search|suchen|filter/i.test(hint))return -1;return (/prompt|message|nachricht|chat|ask/i.test(hint)?10:0)+(e.matches('.ProseMirror,.tiptap,[role="textbox"]')?2:0);};
    const sorted=candidates.map(e=>({e,s:score(e)})).filter(x=>x.s>=0).sort((a,b)=>b.s-a.s);
    if(!sorted.length)return fail('no_composer');if(sorted.length>1&&sorted[0].s===sorted[1].s)return fail('ambiguous_composer');
    const e=sorted[0].e,current=value(e);const matches=a=>a===text||(!(e instanceof HTMLTextAreaElement)&&a.replace(/\n+$/,'')===text.replace(/\n+$/,''));
    if(verify){if(matches(current)&&previous.has(e))previous.set(e,current);return {Ok:matches(current),Code:matches(current)?'verified':'draft_changed',Length:current.length};}
    if(matches(current)){previous.set(e,current);return {Ok:true,Code:'already_present',Length:current.length};}
    if(current.trim().length&&previous.get(e)!==current)return fail('draft_not_empty');
    const watchKey=Symbol.for('axon.rc30.draft-watch');const watched=window[watchKey]??=new WeakSet();
    if(!watched.has(e)){watched.add(e);e.addEventListener('input',event=>{if(event.isTrusted)previous.delete(e);});}
    e.focus();
    if(e instanceof HTMLTextAreaElement){const setter=Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype,'value').set;setter.call(e,text);e.dispatchEvent(new Event('input',{bubbles:true,composed:true}));e.dispatchEvent(new Event('change',{bubbles:true}));e.setSelectionRange(text.length,text.length);}
    else {
      if(['normal','nowrap'].includes(getComputedStyle(e).whiteSpace))e.style.whiteSpace='pre-wrap';
      const selection=getSelection(),range=document.createRange();range.selectNodeContents(e);selection.removeAllRanges();selection.addRange(range);
      if(!document.execCommand('insertText',false,text)){e.replaceChildren(document.createTextNode(text));e.dispatchEvent(new InputEvent('input',{inputType:'insertText',data:text,bubbles:true,composed:true}));}
      // Chromium can materialize trailing ASCII spaces as NBSP. Correct only newly inserted
      // artifacts when the original has no NBSP; never rewrite unrelated/user-owned content.
      if(!text.includes('\u00a0')){const walker=document.createTreeWalker(e,NodeFilter.SHOW_TEXT);let node,changed=false;while((node=walker.nextNode())){if(node.data.includes('\u00a0')){node.data=node.data.replaceAll('\u00a0',' ');changed=true;}}if(changed)e.dispatchEvent(new InputEvent('input',{inputType:'insertText',data:text,bubbles:true,composed:true}));}
    }
    const actual=value(e);if(!matches(actual))return fail('editor_rejected');previous.set(e,actual);return {Ok:true,Code:'inserted',Length:actual.length};
    """;
}
