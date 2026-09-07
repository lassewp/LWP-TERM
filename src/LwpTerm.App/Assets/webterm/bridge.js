/*
 * Bridge between the WPF host (LwpTerm.App) and xterm.js.
 *
 *  Host -> page :  window.chrome.webview message, JSON string
 *      { type: "output", data: <base64 utf-8 bytes> }
 *      { type: "config", fontFamily, fontSize, scrollback, theme }
 *      { type: "clear" } | { type: "focus" } | { type: "fit" }
 *      { type: "find", term, forward }
 *
 *  page -> host :  window.chrome.webview.postMessage(object)
 *      { type: "ready" }
 *      { type: "input",  data: <base64 utf-8 bytes> }
 *      { type: "resize", cols, rows }
 *      { type: "bell" }
 */
(function () {
    "use strict";

    var host = window.chrome && window.chrome.webview;

    var term = new Terminal({
        allowProposedApi: true,
        cursorBlink: true,
        fontFamily: 'Cascadia Mono, Consolas, "Courier New", monospace',
        fontSize: 14,
        scrollback: 5000,
        theme: defaultTheme()
    });

    var fitAddon = new FitAddon.FitAddon();
    var searchAddon = new SearchAddon.SearchAddon();
    term.loadAddon(fitAddon);
    term.loadAddon(searchAddon);
    try {
        term.loadAddon(new WebLinksAddon.WebLinksAddon());
    } catch (e) { /* non-fatal */ }

    term.open(document.getElementById("term"));

    function defaultTheme() {
        return {
            background: "#1e1e1e",
            foreground: "#f1f1f1",
            cursor: "#f1f1f1",
            selectionBackground: "#264f78",
            black: "#1e1e1e", red: "#e06c75", green: "#98c379", yellow: "#e5c07b",
            blue: "#61afef", magenta: "#c678dd", cyan: "#56b6c2", white: "#dcdfe4",
            brightBlack: "#5c6370", brightRed: "#e06c75", brightGreen: "#98c379",
            brightYellow: "#e5c07b", brightBlue: "#61afef", brightMagenta: "#c678dd",
            brightCyan: "#56b6c2", brightWhite: "#ffffff"
        };
    }

    var enc = new TextEncoder();
    var dec = new TextDecoder();

    // ---- client-side keyword colouring (MobaXterm-style) --------------
    var colorize = true;

    var HL = [
        // interface names (Cisco / generic) -> magenta
        [/\b((?:Gigabit|TenGigabit|FortyGig|HundredGig|Fast|Ten|Forty)?Ethernet|Gi|Te|Fa|Eth|Vlan|Port-?channel|Po|Loopback|Lo|Tunnel|Tu|Serial|Se|mgmt|Management)\d+(?:[/.:]\d+)*\b/g, 95],
        // IPv4 (+ optional /prefix) -> bright cyan
        [/\b(?:\d{1,3}\.){3}\d{1,3}(?:\/\d{1,2})?\b/g, 96],
        // MAC address -> cyan
        [/\b(?:[0-9A-Fa-f]{2}[:-]){5}[0-9A-Fa-f]{2}\b|\b(?:[0-9A-Fa-f]{4}\.){2}[0-9A-Fa-f]{4}\b/g, 36],
        // good states -> green
        [/\b(up|yes|ok|active|connected|enabled?|permit(?:ted)?|success(?:ful)?|reachable|established|forwarding|valid|passed)\b/gi, 92],
        // bad states -> red
        [/\b(down|no|deny|denied|disabled?|err(?:or)?|err-?disabled?|fail(?:ed|ure)?|unreachable|notconnect|shutdown|blocked|invalid|expired|timeout|refused)\b/gi, 91],
        // in-between states -> yellow
        [/\b(unassigned|unset|unknown|warning|warn|listen(?:ing)?|learning|blocking|half|pending|partial)\b/gi, 93]
    ];

    function applyHighlight(s) {
        for (var i = 0; i < HL.length; i++) {
            var code = HL[i][1];
            s = s.replace(HL[i][0], "\x1b[" + code + "m$&\x1b[39m");
        }
        return s;
    }

    // Only recolour pure-ASCII chunks that carry no escapes of their own, so
    // server-supplied colour and multi-byte text are left untouched.
    function maybeColorize(bytes) {
        if (!colorize || bytes.length === 0 || bytes.length > 20000) {
            return null;
        }
        for (var i = 0; i < bytes.length; i++) {
            var b = bytes[i];
            if (b === 0x1b || b >= 0x80) {
                return null;
            }
        }
        return applyHighlight(dec.decode(bytes));
    }

    function b64ToBytes(b64) {
        var bin = atob(b64);
        var out = new Uint8Array(bin.length);
        for (var i = 0; i < bin.length; i++) {
            out[i] = bin.charCodeAt(i);
        }
        return out;
    }

    function bytesToB64(bytes) {
        var s = "";
        for (var i = 0; i < bytes.length; i++) {
            s += String.fromCharCode(bytes[i]);
        }
        return btoa(s);
    }

    function send(obj) {
        if (host) {
            host.postMessage(obj);
        }
    }

    // ---- terminal -> host ------------------------------------------------
    term.onData(function (data) {
        send({ type: "input", data: bytesToB64(enc.encode(data)) });
    });

    term.onBinary(function (data) {
        var bytes = new Uint8Array(data.length);
        for (var i = 0; i < data.length; i++) {
            bytes[i] = data.charCodeAt(i) & 255;
        }
        send({ type: "input", data: bytesToB64(bytes) });
    });

    term.onBell(function () {
        send({ type: "bell" });
    });

    var lastCols = 0, lastRows = 0;
    function doFit() {
        try {
            fitAddon.fit();
        } catch (e) { return; }
        if (term.cols !== lastCols || term.rows !== lastRows) {
            lastCols = term.cols;
            lastRows = term.rows;
            send({ type: "resize", cols: term.cols, rows: term.rows });
        }
    }

    var fitTimer = null;
    function scheduleFit() {
        if (fitTimer) {
            clearTimeout(fitTimer);
        }
        fitTimer = setTimeout(doFit, 40);
    }

    if (window.ResizeObserver) {
        new ResizeObserver(scheduleFit).observe(document.getElementById("term"));
    }
    window.addEventListener("resize", scheduleFit);

    // ---- host -> terminal ---------------------------------------------
    function handle(msg) {
        switch (msg.type) {
            case "output": {
                var bytes = b64ToBytes(msg.data);
                var colored = maybeColorize(bytes);
                term.write(colored !== null ? colored : bytes);
                break;
            }
            case "config":
                if (msg.fontFamily) { term.options.fontFamily = msg.fontFamily; }
                if (msg.fontSize) { term.options.fontSize = msg.fontSize; }
                if (msg.scrollback) { term.options.scrollback = msg.scrollback; }
                if (msg.theme) { term.options.theme = msg.theme; }
                if (typeof msg.colorize === "boolean") { colorize = msg.colorize; }
                scheduleFit();
                break;
            case "clear":
                term.clear();
                break;
            case "focus":
                term.focus();
                break;
            case "fit":
                doFit();
                break;
            case "find":
                if (msg.forward === false) {
                    searchAddon.findPrevious(msg.term || "");
                } else {
                    searchAddon.findNext(msg.term || "");
                }
                break;
        }
    }

    if (host) {
        host.addEventListener("message", function (e) {
            var data = e.data;
            if (typeof data === "string") {
                try { data = JSON.parse(data); } catch (err) { return; }
            }
            handle(data);
        });
    }

    // First layout, then tell the host we are live.
    requestAnimationFrame(function () {
        doFit();
        term.focus();
        send({ type: "ready", cols: term.cols, rows: term.rows });
    });
})();
