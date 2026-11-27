/* CryoUtils page logic: shared EN/AR language sync, interactive Lite mode mockup, capture UX sandbox, and GitHub Releases fetcher. */
import { I18N } from "./i18n.js";

const LANG_KEY = "cryotray-docs-lang";
const CU_RELEASE_API = "https://api.github.com/repos/ZerroDevs/CryoUtils/releases/latest";
const CU_VERSION_URL = "https://raw.githubusercontent.com/ZerroDevs/CryoUtils/main/VERSION";
const CU_CACHE_KEY = "cryoutils-docs-release-v1";
const CU_BLOCK_KEY = "cryoutils-docs-release-blocked";
const FRESH_MS = 5 * 60 * 1000;

const SEED_CRYOUTILS = {
  tag: "v0.0.101",
  publishedAt: "2026-11-27T14:00:00Z",
  htmlUrl: "https://github.com/ZerroDevs/CryoUtils/releases/tag/v0.0.101",
  setup: {
    name: "CryoUtils-Setup-0.0.101.exe",
    size: 45249560,
    url: "https://github.com/ZerroDevs/CryoUtils/releases/download/v0.0.101/CryoUtils-Setup-0.0.101.exe",
    shaUrl: "https://github.com/ZerroDevs/CryoUtils/releases/download/v0.0.101/CryoUtils-Setup-0.0.101.exe.sha256"
  },
  liteSetup: {
    name: "CryoUtils-Setup-Lite-0.0.101.exe",
    size: 2833188,
    url: "https://github.com/ZerroDevs/CryoUtils/releases/download/v0.0.101/CryoUtils-Setup-Lite-0.0.101.exe",
    shaUrl: "https://github.com/ZerroDevs/CryoUtils/releases/download/v0.0.101/CryoUtils-Setup-Lite-0.0.101.exe.sha256"
  },
  zip: {
    name: "CryoUtils-0.0.101-win-x64.zip",
    size: 60518885,
    url: "https://github.com/ZerroDevs/CryoUtils/releases/download/v0.0.101/CryoUtils-0.0.101-win-x64.zip",
    shaUrl: "https://github.com/ZerroDevs/CryoUtils/releases/download/v0.0.101/CryoUtils-0.0.101-win-x64.zip.sha256"
  },
  liteZip: {
    name: "CryoUtils-Lite-0.0.101-win-x64.zip",
    size: 957935,
    url: "https://github.com/ZerroDevs/CryoUtils/releases/download/v0.0.101/CryoUtils-Lite-0.0.101-win-x64.zip",
    shaUrl: "https://github.com/ZerroDevs/CryoUtils/releases/download/v0.0.101/CryoUtils-Lite-0.0.101-win-x64.zip.sha256"
  }
};

const $ = (selector, root = document) => root.querySelector(selector);
const $$ = (selector, root = document) => Array.from(root.querySelectorAll(selector));

const state = {
  lang: "en",
  lite: false,
  uxTab: "snap",
  prettyFrame: false,
  release: SEED_CRYOUTILS,
  releaseStatus: "checking",
  copyState: "",
  copyTimer: 0,
  loading: false
};

function t(key, vars = {}) {
  const dict = I18N[state.lang] || I18N.en;
  let value = dict[key] ?? I18N.en[key] ?? key;
  Object.keys(vars).forEach((name) => {
    value = value.replaceAll("{" + name + "}", String(vars[name]));
  });
  return value;
}

function formatBinaryMb(bytes) {
  const mb = Number(bytes || 0) / 1048576;
  const digits = mb < 10 ? 2 : 1;
  const locale = state.lang === "ar" ? "ar-u-nu-latn" : "en-US";
  try {
    return new Intl.NumberFormat(locale, {
      minimumFractionDigits: digits,
      maximumFractionDigits: digits
    }).format(mb) + " MB";
  } catch {
    return mb.toFixed(digits) + " MB";
  }
}

function formatDate(iso) {
  const locale = state.lang === "ar" ? "ar-u-nu-latn" : "en-US";
  try {
    return new Intl.DateTimeFormat(locale, {
      dateStyle: "medium",
      timeZone: "UTC"
    }).format(new Date(iso));
  } catch {
    return String(iso || "").slice(0, 10);
  }
}

function setText(el, text) {
  if (el && el.textContent !== text) el.textContent = text;
}

function renderPreviewMode() {
  const win = $("#cu-preview");
  if (!win) return;
  win.classList.toggle("is-lite", state.lite);

  const sw = $("#cu-lite-switch");
  if (sw) sw.setAttribute("aria-checked", String(state.lite));

  setText($("#cu-lite-state"), t(state.lite ? "cu.preview.modeOn" : "cu.preview.modeOff"));
  setText($("#cu-preview-note"), t(state.lite ? "cu.preview.noteLite" : "cu.preview.noteFull"));
  setText($("#cu-banner-title"), t(state.lite ? "cu.preview.bannerLiteTitle" : "cu.preview.bannerFullTitle"));
  setText($("#cu-banner-body"), t(state.lite ? "cu.preview.bannerLiteBody" : "cu.preview.bannerFullBody"));
  setText($("#cu-tray-readout"), t(state.lite ? "cu.preview.trayLite" : "cu.preview.trayFull"));
  setText($("#cu-state-readout"), t(state.lite ? "cu.preview.stateLite" : "cu.preview.stateFull"));
}

function renderUxSandbox() {
  $$(".cu-ux-tab").forEach((tab) => {
    tab.classList.toggle("is-active", tab.dataset.cuUx === state.uxTab);
  });

  const prefixMap = {
    snap: "cu.ux.snap",
    ocr: "cu.ux.ocr",
    drag: "cu.ux.drag",
    ghost: "cu.ux.ghost"
  };
  const prefix = prefixMap[state.uxTab] || prefixMap.snap;
  setText($("#cu-ux-badge"), t(prefix + "Badge"));
  setText($("#cu-ux-heading"), t(prefix + "Title"));
  setText($("#cu-ux-desc"), t(prefix + "Desc"));
  setText($("#cu-shot-caption"), t(prefix + "PreviewLabel"));

  const stage = $("#cu-shot-stage");
  const card = $("#cu-shot-card");
  const frameBtn = $("#cu-frame-btn");
  const tag = $("#cu-shot-tag");
  if (stage) stage.classList.toggle("is-framed", state.prettyFrame && state.uxTab !== "ghost");
  if (card) card.classList.toggle("is-ghost", state.uxTab === "ghost");
  if (frameBtn) {
    frameBtn.classList.toggle("is-active", state.prettyFrame);
    frameBtn.setAttribute("aria-pressed", String(state.prettyFrame));
  }
  if (tag) {
    if (state.uxTab === "ocr") tag.textContent = "TXT";
    else if (state.uxTab === "ghost") tag.textContent = "WS_EX_TRANSPARENT";
    else tag.textContent = state.prettyFrame ? "Framed PNG" : "PNG";
  }
}

function checksumCommand() {
  return "Get-FileHash .\\" + state.release.setup.name + " -Algorithm SHA256";
}

function updateCopyStatus() {
  const status = $("#cu-copy-status");
  const btn = $("#cu-copy-checksum");
  if (!status || !btn) return;
  if (state.copyState === "success") {
    status.textContent = t("release.copied");
    status.classList.remove("is-error");
    btn.textContent = t("release.copied");
  } else if (state.copyState === "error") {
    status.textContent = t("release.copyError");
    status.classList.add("is-error");
    btn.textContent = t("release.copy");
  } else {
    status.textContent = "";
    status.classList.remove("is-error");
    btn.textContent = t("release.copy");
  }
}

function renderRelease() {
  const r = state.release;
  const version = r.tag.startsWith("v") ? r.tag : "v" + r.tag;
  setText($("#cu-release-version"), version);
  setText($("#cu-preview-ver"), version);

  const dateEl = $("#cu-release-date");
  if (dateEl) {
    if (r.publishedAt) {
      dateEl.dateTime = r.publishedAt;
      dateEl.textContent = formatDate(r.publishedAt);
    } else {
      dateEl.removeAttribute("datetime");
      dateEl.textContent = t("release.latestLabel");
    }
  }

  const bindLink = (elId, fileId, sizeId, shaId, asset) => {
    if (!asset) return;
    const anchor = $(elId);
    if (anchor) anchor.href = asset.url;
    setText($(fileId), asset.name);
    setText($(sizeId), typeof asset.size === "number" ? formatBinaryMb(asset.size) : t("release.latestLabel"));
    const shaAnchor = $(shaId);
    if (shaAnchor) shaAnchor.href = asset.shaUrl;
  };

  bindLink("#cu-download-setup", "#cu-setup-filename", "#cu-setup-size", "#cu-setup-sha", r.setup);
  bindLink("#cu-download-lite", "#cu-lite-filename", "#cu-lite-size", "#cu-lite-sha", r.liteSetup);
  bindLink("#cu-download-zip", "#cu-zip-filename", "#cu-zip-size", "#cu-zip-sha", r.zip);
  bindLink("#cu-download-lite-zip", "#cu-lite-zip-filename", "#cu-lite-zip-size", "#cu-lite-zip-sha", r.liteZip);

  const cmd = checksumCommand();
  setText($("#cu-checksum-command"), cmd);
  const copyBtn = $("#cu-copy-checksum");
  if (copyBtn) copyBtn.dataset.command = cmd;

  const statusEl = $("#cu-release-status");
  if (statusEl) {
    statusEl.classList.remove("is-ok", "is-fallback");
    if (state.releaseStatus === "latest") {
      statusEl.classList.add("is-ok");
      statusEl.textContent = t("cu.dl.statusLatest");
    } else if (state.releaseStatus === "version") {
      statusEl.classList.add("is-fallback");
      statusEl.textContent = t("cu.dl.statusVersion");
    } else if (state.releaseStatus === "cached") {
      statusEl.classList.add("is-fallback");
      statusEl.textContent = t("cu.dl.statusUnavailable", { tag: r.tag });
    } else {
      statusEl.textContent = t("cu.dl.statusChecking");
    }
  }
}

function setDocumentLanguage(lang) {
  state.lang = lang === "ar" ? "ar" : "en";
  document.documentElement.lang = state.lang;
  document.documentElement.dir = state.lang === "ar" ? "rtl" : "ltr";
  document.title = t("cu.meta.title");

  const meta = $("#meta-description");
  if (meta) meta.setAttribute("content", t("cu.meta.description"));

  $$("[data-i18n]").forEach((el) => {
    const val = t(el.dataset.i18n);
    if (el.textContent !== val) el.textContent = val;
  });

  $$("[data-i18n-aria]").forEach((el) => {
    el.setAttribute("aria-label", t(el.dataset.i18nAria));
  });

  $$(".lang-btn").forEach((btn) => {
    const active = btn.dataset.lang === state.lang;
    btn.classList.toggle("is-active", active);
    btn.setAttribute("aria-pressed", String(active));
  });

  try {
    localStorage.setItem(LANG_KEY, state.lang);
  } catch {
    // Ignore storage quota / privacy block.
  }

  renderPreviewMode();
  renderUxSandbox();
  renderRelease();
  updateCopyStatus();
}

function releaseFromApi(data) {
  const assets = Array.isArray(data.assets) ? data.assets : [];
  const isBinary = (a) => !/\.sha256$/i.test(a.name);
  const setup = assets.find((a) => /setup/i.test(a.name) && /\.exe$/i.test(a.name) && !/-lite/i.test(a.name) && isBinary(a));
  const liteSetup = assets.find((a) => /-lite/i.test(a.name) && /\.exe$/i.test(a.name) && isBinary(a));
  const zip = assets.find((a) => /\.zip$/i.test(a.name) && !/-lite/i.test(a.name) && isBinary(a));
  const liteZip = assets.find((a) => /-lite.*\.zip$/i.test(a.name) && isBinary(a));
  if (!setup || !zip) throw new Error("Release assets incomplete");

  const tag = data.tag_name || SEED_CRYOUTILS.tag;
  const clean = String(tag).replace(/^v/i, "");
  const base = "https://github.com/ZerroDevs/CryoUtils/releases/download/" + tag;
  const mapAsset = (asset, fallbackName) => asset ? ({
    name: asset.name,
    size: asset.size,
    url: asset.browser_download_url,
    shaUrl: asset.browser_download_url + ".sha256"
  }) : ({
    name: fallbackName,
    size: null,
    url: base + "/" + fallbackName,
    shaUrl: base + "/" + fallbackName + ".sha256"
  });

  return {
    tag,
    publishedAt: data.published_at || null,
    htmlUrl: data.html_url || SEED_CRYOUTILS.htmlUrl,
    setup: mapAsset(setup, "CryoUtils-Setup-" + clean + ".exe"),
    liteSetup: mapAsset(liteSetup, "CryoUtils-Setup-Lite-" + clean + ".exe"),
    zip: mapAsset(zip, "CryoUtils-" + clean + "-win-x64.zip"),
    liteZip: mapAsset(liteZip, "CryoUtils-Lite-" + clean + "-win-x64.zip")
  };
}

function releaseFromVersion(raw, previous) {
  const clean = String(raw || "").trim().replace(/^v/i, "");
  if (!/^\d+\.\d+\.\d+$/.test(clean)) return null;
  const tag = "v" + clean;
  if (previous && previous.tag === tag) return previous;
  const base = "https://github.com/ZerroDevs/CryoUtils/releases/download/" + tag;
  const make = (name) => ({ name, size: null, url: base + "/" + name, shaUrl: base + "/" + name + ".sha256" });
  return {
    tag,
    publishedAt: null,
    htmlUrl: "https://github.com/ZerroDevs/CryoUtils/releases/tag/" + tag,
    setup: make("CryoUtils-Setup-" + clean + ".exe"),
    liteSetup: make("CryoUtils-Setup-Lite-" + clean + ".exe"),
    zip: make("CryoUtils-" + clean + "-win-x64.zip"),
    liteZip: make("CryoUtils-Lite-" + clean + "-win-x64.zip")
  };
}

function readCache() {
  try {
    const raw = localStorage.getItem(CU_CACHE_KEY);
    if (!raw) return null;
    const parsed = JSON.parse(raw);
    if (!parsed || !parsed.release || !parsed.release.setup || !parsed.release.zip) return null;
    return parsed;
  } catch {
    return null;
  }
}

async function loadRelease() {
  if (state.loading) return;
  state.loading = true;
  const cached = readCache();
  if (cached) state.release = cached.release;

  let blockedUntil = 0;
  try { blockedUntil = Number(localStorage.getItem(CU_BLOCK_KEY)) || 0; } catch {}

  try {
    if (cached && Date.now() - Number(cached.apiCheckedAt || 0) < FRESH_MS) {
      state.releaseStatus = "latest";
      renderRelease();
      return;
    }

    if (Date.now() >= blockedUntil) {
      const ctrl = new AbortController();
      const timer = window.setTimeout(() => ctrl.abort(), 8000);
      try {
        const res = await fetch(CU_RELEASE_API, {
          headers: { Accept: "application/vnd.github+json" },
          cache: "no-store",
          signal: ctrl.signal
        });
        if (res.ok) {
          const parsed = releaseFromApi(await res.json());
          state.release = parsed;
          state.releaseStatus = "latest";
          try {
            localStorage.removeItem(CU_BLOCK_KEY);
            localStorage.setItem(CU_CACHE_KEY, JSON.stringify({ release: parsed, apiCheckedAt: Date.now() }));
          } catch {}
          renderRelease();
          return;
        } else if (res.status === 403 || res.status === 429) {
          const reset = Number(res.headers.get("X-RateLimit-Reset")) || 0;
          const until = reset ? reset * 1000 : Date.now() + FRESH_MS;
          try { localStorage.setItem(CU_BLOCK_KEY, String(until)); } catch {}
        }
      } catch {
        // Fall through to VERSION fallback.
      } finally {
        window.clearTimeout(timer);
      }
    }

    const ctrlV = new AbortController();
    const timerV = window.setTimeout(() => ctrlV.abort(), 6000);
    try {
      const vRes = await fetch(CU_VERSION_URL + "?t=" + Date.now(), {
        cache: "no-store",
        headers: { Accept: "text/plain" },
        signal: ctrlV.signal
      });
      if (vRes.ok) {
        const derived = releaseFromVersion(await vRes.text(), state.release);
        if (derived) {
          state.release = derived;
          state.releaseStatus = "version";
          renderRelease();
          return;
        }
      }
    } catch {
      // Fall back to seeded metadata.
    } finally {
      window.clearTimeout(timerV);
    }

    state.releaseStatus = "cached";
    renderRelease();
  } finally {
    state.loading = false;
  }
}

async function copyChecksumCommand() {
  const btn = $("#cu-copy-checksum");
  const cmd = (btn && btn.dataset.command) || checksumCommand();
  try {
    if (navigator.clipboard && window.isSecureContext) {
      await navigator.clipboard.writeText(cmd);
    } else {
      const ta = document.createElement("textarea");
      ta.value = cmd;
      ta.setAttribute("readonly", "");
      ta.style.position = "fixed";
      ta.style.opacity = "0";
      document.body.appendChild(ta);
      ta.select();
      if (!document.execCommand("copy")) throw new Error("Copy failed");
      ta.remove();
    }
    state.copyState = "success";
  } catch {
    state.copyState = "error";
  }
  updateCopyStatus();
  window.clearTimeout(state.copyTimer);
  state.copyTimer = window.setTimeout(() => {
    state.copyState = "";
    updateCopyStatus();
  }, 2200);
}

function initialize() {
  $$(".lang-btn").forEach((btn) => {
    btn.addEventListener("click", () => {
      if (btn.dataset.lang && btn.dataset.lang !== state.lang) {
        setDocumentLanguage(btn.dataset.lang);
      }
    });
  });

  const toggleLite = () => {
    state.lite = !state.lite;
    renderPreviewMode();
  };
  $("#cu-lite-switch")?.addEventListener("click", toggleLite);
  $("#cu-hero-lite-btn")?.addEventListener("click", toggleLite);

  $$(".cu-ux-tab").forEach((tab) => {
    tab.addEventListener("click", () => {
      state.uxTab = tab.dataset.cuUx || "snap";
      renderUxSandbox();
    });
  });

  $("#cu-frame-btn")?.addEventListener("click", () => {
    state.prettyFrame = !state.prettyFrame;
    renderUxSandbox();
  });

  $("#cu-copy-checksum")?.addEventListener("click", copyChecksumCommand);

  let savedLang = "en";
  try { savedLang = localStorage.getItem(LANG_KEY) || "en"; } catch {}
  setDocumentLanguage(savedLang === "ar" ? "ar" : "en");
  loadRelease();
}

initialize();
