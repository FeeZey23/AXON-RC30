/*
 * Arena.ai Desktop Companion – Workspace-Beobachter (experimentell, Standard: AUS)
 * ---------------------------------------------------------------------------
 * Diese Datei wird von der App als Dokument-Skript in arena.ai injiziert (vor den Seiten-
 * skripten, in allen Frames) und läuft damit auch nach Reloads.
 *
 * Absichtlich DUMM gehalten: Sie erkennt nur "hier ist wahrscheinlich eine Datei"
 * und meldet Rohtext + Kandidat an die App. Die ganze Entscheidung (Pfadschutz,
 * Kopfzeile entfernen, Überschreiben/Konflikt/Backup) fällt der testbare C#-Core.
 *
 * ToS-Hinweis: automatisiertes Auslesen ist in den Arena-Nutzungsbedingungen nicht
 * vorgesehen. Dieser Beobachter ist deshalb opt-in und kann jederzeit abgeschaltet
 * werden; der verlässliche Weg bleibt der vom Nutzer ausgelöste Download.
 */
(function () {
    'use strict';

    if (window.__arenaCompanionBot === true) {
        return;
    }
    window.__arenaCompanionBot = true;

    var CONFIG = {
        debounceMs: 400,
        maxCharsPerBlock: 400000,
        seenLimit: 5000
    };

    var seen = new Map();          // Element -> Hash des Inhalts
    var pending = new Map();       // Element -> {name, text}
    var timer = null;

    function enabled() {
        return window.__arenaCompanionBot === true;
    }

    function post(payload) {
        if (!enabled()) {
            return;
        }
        try {
            if (window.chrome && window.chrome.webview && window.chrome.webview.postMessage) {
                window.chrome.webview.postMessage(JSON.stringify(payload));
            }
        } catch (err) {
            /* App nicht erreichbar -> still ignorieren, Seite darf nicht sterben */
        }
    }

    // FNV-1a (64 bit, in zwei 32-bit-Hälften) – dependency-free, kein crypto.subtle,
    // damit es auch in nicht-sicheren Kontexten und in iframes funktioniert.
    function hash(text) {
        var h1 = 0x811c9dc5, h2 = 0x01000193;
        for (var i = 0; i < text.length; i++) {
            h1 ^= text.charCodeAt(i);
            h1 = (h1 + ((h1 << 1) | 0)) | 0;
            h2 = (h2 + (text.charCodeAt(i) * 31 | 0)) | 0;
        }
        return (h1 >>> 0).toString(16) + '-' + (h2 >>> 0).toString(16) + '-' + text.length;
    }

    function textOf(node) {
        // textContent statt innerText: innerText ist bei ausgeblendeten oder noch nicht
        // gerenderten Knoten leer, und genau das wurde bisher falsch synchronisiert.
        var t = node.textContent || '';
        return t.length > CONFIG.maxCharsPerBlock ? t.slice(0, CONFIG.maxCharsPerBlock) : t;
    }

    function headerCandidateFor(node) {
        // 1) explizite Marker, falls arena.ai sie hat
        var explicit = node.getAttribute && (
            node.getAttribute('data-file') ||
            node.getAttribute('data-filename') ||
            node.getAttribute('data-path') ||
            node.getAttribute('aria-label'));
        if (explicit && explicit.length < 300 && /\./.test(explicit)) {
            return explicit;
        }

        // 2) Beschriftung in der Nähe: Kopf des Code-Blocks (Üblichkeit bei Chat-UIs)
        var scope = node.closest ? (node.closest('pre, [class*="code"], [data-component="code"]') || node.parentElement) : node.parentElement;
        for (var hop = 0; hop < 4 && scope; hop++) {
            var label = scope.querySelector && scope.querySelector(
                '[class*="filename"], [class*="file-name"], [class*="filePath"], [class*="file-path"], [class*="title"], header span');
            if (label && label.textContent && label.textContent.length < 300) {
                return label.textContent.trim();
            }
            scope = scope.parentElement;
        }
        return '';
    }

    function collect() {
        var selector = 'pre, pre code, code[data-file], [data-filename], [aria-label][class*="code"]';
        var blocks;
        try {
            blocks = document.querySelectorAll(selector);
        } catch (err) {
            return;
        }

        for (var i = 0; i < blocks.length; i++) {
            var block = blocks[i];
            // verschachtelte Treffer vermeiden: nur der äußere Block zählt
            if (block.tagName === 'CODE' && block.parentElement && block.parentElement.tagName === 'PRE') {
                continue;
            }
            var text = textOf(block);
            if (!text || text.length < 4) {
                continue;
            }
            var h = hash(text);
            if (seen.get(block) === h) {
                continue;   // gleicher Inhalt wie beim letzten Mal -> kein Schreibvorgang
            }
            seen.set(block, h);
            if (seen.size > CONFIG.seenLimit) {
                seen.clear();
                seen.set(block, h);
            }
            pending.set(block, { name: headerCandidateFor(block), text: text });
        }
    }

    function flush() {
        timer = null;
        if (!enabled()) {
            pending.clear();
            return;
        }
        if (pending.size === 0) {
            return;
        }
        var batch = [];
        pending.forEach(function (value, key) {
            batch.push(value);
            pending.delete(key);
        });
        for (var i = 0; i < batch.length; i++) {
            post({
                action: 'SYNC_FILE',
                fileName: batch[i].name || '',
                content: batch[i].text
            });
        }
    }

    function schedule() {
        if (timer) {
            return;
        }
        timer = setTimeout(flush, CONFIG.debounceMs);
    }

    function start() {
        if (!document.body && !document.documentElement) {
            // Zu früh (Dokument-Skript läuft vor dem Body): einfach später nochmal.
            setTimeout(start, 150);
            return;
        }
        var target = document.body || document.documentElement;
        collect();
        try {
            new MutationObserver(function () {
                schedule();
                setTimeout(collect, 0);
            }).observe(target, { childList: true, subtree: true, characterData: true });
        } catch (err) {
            /* Observer nicht möglich (z. B. Shadow-Grenze) -> Bot bleibt harmlos inaktiv */
        }
        // Nach React-Re-Renders ohne DOM-Strukturänderung: ein Nachlauf reicht.
        setTimeout(collect, 1200);
        setTimeout(schedule, 1400);
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', function () {
            collect();
            schedule();
            start();
        });
    } else {
        start();
    }
})();
