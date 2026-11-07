// Injected into every router HTML page by the local proxy.
// Config comes from window.__XT_CFG = {lang, dict:{zh:[en,ro]}, patterns:[{re,en,ro}]}.
(function () {
  if (window.__XT || !window.__XT_CFG) return;
  var cfg = window.__XT_CFG;
  var CJK = /[一-鿿]/;
  var IDX = { en: 0, ro: 1 };
  var lang = cfg.lang;
  var dict = cfg.dict;
  // Substring replacement only uses keys of 3+ chars, so short units (天, 分, 秒) can't corrupt other text.
  var subKeys = Object.keys(dict).filter(function (k) { return k.length >= 3; })
    .sort(function (a, b) { return b.length - a.length; });
  var patterns = (cfg.patterns || []).map(function (p) { return { re: new RegExp(p.re), en: p.en, ro: p.ro }; });
  var SKIP = { SCRIPT: 1, STYLE: 1, NOSCRIPT: 1, TEXTAREA: 1 };
  var ATTRS = ['placeholder', 'title', 'alt', 'value'];
  var textRec = new WeakMap();
  var attrRec = new WeakMap();
  var missing = {};
  var toSend = [];
  var sendTimer = null;

  function norm(s) { return s.replace(/\s+/g, ' ').trim(); }

  function report(n) {
    if (missing[n]) return;
    missing[n] = 1;
    toSend.push(n);
    if (sendTimer) return;
    sendTimer = setTimeout(function () {
      sendTimer = null;
      var batch = toSend; toSend = [];
      var page = location.pathname.replace(/;stok=[^\/]*/, '');
      try {
        fetch('/__xt/missing', { method: 'POST', headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ page: page, strings: batch }) });
      } catch (e) {}
    }, 1500);
  }

  // Returns the translation of s, or s itself when nothing applies.
  function translate(s) {
    var i = IDX[lang];
    if (i === undefined || !CJK.test(s)) return s;
    var n = norm(s);
    var e = dict[n];
    var tail = '';
    if (!e) {
      // "名称：" and "名称:" share the entry for "名称".
      var c = /^(.*?)\s*([:：])$/.exec(n);
      if (c && dict[c[1]]) { e = dict[c[1]]; tail = ':'; }
    }
    var out = null;
    if (e && e[i]) {
      out = s.replace(n, e[i] + tail);
      if (out === s) out = e[i] + tail;
    } else {
      for (var p = 0; p < patterns.length && out === null; p++) {
        var m = patterns[p].re.exec(n);
        if (m) out = patterns[p][lang === 'en' ? 'en' : 'ro'].replace(/\$(\d)/g, function (_, d) { return m[+d] || ''; });
      }
      if (out === null) {
        out = s;
        for (var k = 0; k < subKeys.length; k++) {
          var v = dict[subKeys[k]][i];
          if (v && out.indexOf(subKeys[k]) >= 0) out = out.split(subKeys[k]).join(v);
        }
      }
    }
    if (CJK.test(out)) report(n);
    return out;
  }

  function doText(node) {
    // Template text living inside <script>/<style> is code, not page text.
    if (node.parentNode && SKIP[node.parentNode.tagName]) return;
    var rec = textRec.get(node);
    var orig = rec && node.data === rec.t ? rec.o : node.data;
    var t = lang === 'zh' ? orig : translate(orig);
    textRec.set(node, { o: orig, t: t });
    if (node.data !== t) node.data = t;
  }

  function doAttrs(el) {
    var recs = attrRec.get(el);
    if (!recs) { recs = {}; attrRec.set(el, recs); }
    for (var a = 0; a < ATTRS.length; a++) {
      var name = ATTRS[a];
      if (name === 'value' && !(el.tagName === 'INPUT' && /^(button|submit|reset)$/i.test(el.type))) continue;
      var cur = el.getAttribute(name);
      if (cur === null) continue;
      var r = recs[name];
      var orig = r && cur === r.t ? r.o : cur;
      var t = lang === 'zh' ? orig : translate(orig);
      recs[name] = { o: orig, t: t };
      if (cur !== t) el.setAttribute(name, t);
    }
  }

  function walk(node) {
    if (node.nodeType === 3) { doText(node); return; }
    if (node.nodeType !== 1 || SKIP[node.tagName]) return;
    doAttrs(node);
    for (var c = node.firstChild; c; c = c.nextSibling) walk(c);
  }

  var queue = [];
  var timer = null;
  function schedule(n) {
    queue.push(n);
    if (timer) return;
    timer = setTimeout(function () {
      timer = null;
      var q = queue; queue = [];
      for (var i = 0; i < q.length; i++) walk(q[i]);
    }, 30);
  }

  function setLang(l) {
    lang = l;
    walk(document.documentElement);
    var f = document.getElementsByTagName('iframe');
    for (var i = 0; i < f.length; i++) {
      try { f[i].contentWindow.postMessage({ xtLang: l }, '*'); } catch (e) {}
    }
  }

  window.addEventListener('message', function (ev) {
    if (ev.data && ev.data.xtLang) setLang(ev.data.xtLang);
  });

  // alert()/confirm() are native dialogs, so translate their message too.
  ['alert', 'confirm'].forEach(function (fn) {
    var o = window[fn];
    window[fn] = function (m) { return o.call(window, lang === 'zh' ? m : translate(String(m))); };
  });

  new MutationObserver(function (muts) {
    for (var i = 0; i < muts.length; i++) {
      var m = muts[i];
      if (m.type === 'childList') {
        for (var j = 0; j < m.addedNodes.length; j++) schedule(m.addedNodes[j]);
      } else if (m.type === 'characterData') {
        schedule(m.target);
      } else if (m.type === 'attributes') {
        schedule(m.target);
      }
    }
  }).observe(document.documentElement, {
    childList: true, subtree: true, characterData: true,
    attributes: true, attributeFilter: ATTRS
  });

  window.__XT = { setLang: setLang };
  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', function () { walk(document.documentElement); });
  } else {
    walk(document.documentElement);
  }
})();
