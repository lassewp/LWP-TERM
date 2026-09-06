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
            case "output":
                term.write(b64ToBytes(msg.data));
                break;
            case "config":
                if (msg.fontFamily) { term.options.fontFamily = msg.fontFamily; }
                if (msg.fontSize) { term.options.fontSize = msg.fontSize; }
                if (msg.scrollback) { term.options.scrollback = msg.scrollback; }
                if (msg.theme) { term.options.theme = msg.theme; }
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
