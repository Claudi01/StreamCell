(function () {
  "use strict";
  var U = window.StreamCellUI;
  var $ = function (id) { return document.getElementById(id); };
  var token = "";
  try { token = localStorage.getItem("streamcell.phoneToken") || ""; } catch (_) {}
  var deck = null, pageId = "", connected = false, polling = false, timer, lastSignature = "", pendingConfirm = null;
  var running = new Set();
  U.decorate();

  async function request(path, body) {
    var controller = new AbortController();
    var timeout = setTimeout(function () { controller.abort(); }, 10000);
    try {
      var response = await fetch(path, {
        method: body === undefined ? "GET" : "POST",
        headers: {"Content-Type":"application/json","X-StreamCell-Token":token},
        body: body === undefined ? undefined : JSON.stringify(body),
        cache: "no-store", signal: controller.signal
      });
      var result = await response.json().catch(function () { return {}; });
      if (!response.ok) { var error = new Error(result.error || "Não foi possível concluir a ação."); error.status = response.status; throw error; }
      return result;
    } catch (error) {
      if (error.name === "AbortError" || error instanceof TypeError) {
        throw new Error("Sem conexão com o PC. Confira o Wi-Fi e mantenha o StreamCell aberto.");
      }
      throw error;
    } finally { clearTimeout(timeout); }
  }

  function showPairing(message) {
    clearTimeout(timer);
    token = "";
    connected = false;
    deck = null;
    lastSignature = "";
    running.clear();
    try { localStorage.removeItem("streamcell.phoneToken"); } catch (_) {}
    $("controllerShell").hidden = true;
    $("pairingScreen").hidden = false;
    if ($("confirmDialog").open) $("confirmDialog").close();
    pendingConfirm = null;
    $("pairError").textContent = message || "";
    $("pairError").hidden = !message;
  }

  function showController() {
    $("pairingScreen").hidden = true;
    $("controllerShell").hidden = false;
  }

  function connection(value) {
    connected = value;
    $("connectionStatus").className = "connection-pill" + (value ? " connected" : " disconnected");
    $("connectionStatus").querySelector("span").textContent = value ? "PC conectado" : "Reconectando";
    $("mobileGrid").classList.toggle("grid-offline", !value);
    $("controllerHint").textContent = value ? (currentPage() && !currentPage().buttons.length ? "Esta página está vazia. Configure as teclas no editor do PC e salve." : "Toque em uma tecla para executar no computador.") : "Sem conexão. Confira o Wi-Fi e mantenha o StreamCell aberto no PC.";
  }

  function currentPage() {
    return deck && (deck.pages.find(function (page) { return page.id === pageId; }) || deck.pages[0]);
  }

  function render() {
    if (!deck) return;
    var page = currentPage();
    if (page) pageId = page.id;
    $("deckTitle").textContent = deck.title || "Meu StreamCell";
    document.title = (deck.title || "Meu deck") + " · StreamCell";
    var grid = $("mobileGrid");
    var capacity = Math.min(48,Math.max(4,(page.columns||5)*(page.rows||3)));
    grid.style.gridTemplateColumns = "repeat(" + (page.columns||5) + ", minmax(0,1fr))";
    grid.style.gridTemplateRows = "repeat(" + (page.rows||3) + ", minmax(0,1fr))";
    for (var slot = 0; slot < capacity; slot++) {
      var button = page && page.buttons.find(function (item) { return item.slot === slot; });
      var fresh = U.key(button, {slot:slot});
      if (button && running.has(button.id)) { fresh.classList.add("is-running"); fresh.disabled = true; }
      var el = grid.children[slot];
      if (!el) {
        el = fresh;
        el.addEventListener("click", function () { if (this._action) onPress(this._action); });
        grid.append(el);
      } else {
        if (el.innerHTML !== fresh.innerHTML) el.innerHTML = fresh.innerHTML;
        el.className = fresh.className;
        el.style.cssText = fresh.style.cssText;
        el.disabled = fresh.disabled;
        el.title = fresh.title;
        el.setAttribute("aria-label", fresh.getAttribute("aria-label"));
        if (button) el.removeAttribute("aria-hidden"); else el.setAttribute("aria-hidden","true");
      }
      el._action = button;
      el.dataset.id = button ? button.id : "";
    }
    while (grid.children.length > capacity) grid.lastElementChild.remove();
    grid.classList.toggle("grid-offline", !connected);
    var index = page ? deck.pages.indexOf(page) : 0;
    $("mobilePageNumber").textContent = String(index + 1).padStart(2,"0") + " / " + String(deck.pages.length || 1).padStart(2,"0");
    $("previousPage").disabled = index <= 0;
    $("nextPage").disabled = index >= deck.pages.length - 1;
    var tabs = $("mobilePages");
    Array.from(tabs.children).forEach(function (tab) {
      if (!deck.pages.some(function (item) { return item.id === tab.dataset.page; })) tab.remove();
    });
    deck.pages.forEach(function (item, position) {
      var tab = Array.from(tabs.children).find(function (node) { return node.dataset.page === item.id; });
      if (!tab) {
        tab = document.createElement("button");
        tab.type = "button";
        tab.dataset.page = item.id;
        tab.addEventListener("click",function () { pageId = this.dataset.page; render(); });
      }
      tab.className = "page-tab" + (item.id === pageId ? " selected" : "");
      tab.textContent = item.name;
      if (item.id === pageId) tab.setAttribute("aria-current","page"); else tab.removeAttribute("aria-current");
      if (tabs.children[position] !== tab) tabs.insertBefore(tab, tabs.children[position] || null);
    });
    $("obsStatus").textContent = deck.status && deck.status.obsConnected ? "OBS conectado" : "OBS desconectado";
    if (page && !page.buttons.length) $("controllerHint").textContent = "Esta página está vazia. Configure as teclas no editor do PC e salve.";
  }

  async function poll() {
    if (!token || polling) return;
    polling = true;
    var pollingToken = token;
    try {
      var result = await request("/api/deck");
      if (token !== pollingToken) return;
      if (!Array.isArray(result.pages)) throw new Error("O PC enviou um painel inválido. Atualize o StreamCell.");
      result.pages.forEach(function (page) { if (!Array.isArray(page.buttons)) page.buttons = []; });
      deck = result;
      showController();
      var wasConnected = connected;
      connection(true);
      var signature = JSON.stringify(result);
      if (signature !== lastSignature || !wasConnected) { lastSignature = signature; render(); }
    } catch (error) {
      if (token !== pollingToken) return;
      if (error.status === 401) { showPairing("A conexão foi encerrada pelo PC. Informe o novo código para reconectar."); return; }
      showController();
      connection(false);
    } finally {
      polling = false;
      clearTimeout(timer);
      if (token) timer = setTimeout(poll, document.hidden ? 5000 : 2200);
    }
  }

  function onPress(button) {
    if (!connected || running.has(button.id)) return;
    if (button.requiresConfirmation || button.type === "obs_stream") {
      pendingConfirm = button;
      $("confirmLabel").textContent = button.label;
      $("confirmDialog").showModal();
    } else execute(button, false);
  }

  async function execute(button, confirmed) {
    if (!connected || running.has(button.id)) return;
    running.add(button.id);
    render();
    if (navigator.vibrate) navigator.vibrate(18);
    try {
      var payload = {id:button.id};
      if (confirmed) payload.confirmed = true;
      var result = await request("/api/execute", payload);
      if (result.ok === false) throw new Error(result.message || "A ação não pôde ser executada.");
      U.toast(result.message || button.label + ": executado");
      clearTimeout(timer);
      poll();
    } catch (error) {
      if (error.status === 401) showPairing("A conexão foi encerrada. Conecte novamente com o código do PC.");
      else U.toast(error.message, true);
    } finally { running.delete(button.id); if (deck) render(); }
  }

  $("pairForm").addEventListener("submit", async function (event) {
    event.preventDefault();
    var code = $("pairCode").value.trim().replace(/[\s-]/g,"").toUpperCase();
    if (!code) return;
    $("pairButton").disabled = true;
    $("pairError").hidden = true;
    try {
      var result = await request("/api/pair", {code:code,username:$("pairUsername").value.trim(),password:$("pairPassword").value});
      if (!result.token) throw new Error("O PC não enviou a conexão. Tente novamente.");
      token = result.token;
      try { localStorage.setItem("streamcell.phoneToken",token); } catch (_) {}
      $("pairCode").value = ""; $("pairPassword").value = "";
      showController();
      await poll();
    } catch (error) {
      $("pairError").textContent = error.message;
      $("pairError").hidden = false;
    } finally { $("pairButton").disabled = false; }
  });
  $("previousPage").addEventListener("click",function () {
    if (!deck) return;
    var index = deck.pages.indexOf(currentPage());
    if (index > 0) { pageId = deck.pages[index - 1].id; render(); }
  });
  $("nextPage").addEventListener("click",function () {
    if (!deck) return;
    var index = deck.pages.indexOf(currentPage());
    if (index < deck.pages.length - 1) { pageId = deck.pages[index + 1].id; render(); }
  });
  function cancelConfirm() { pendingConfirm = null; $("confirmDialog").close(); }
  $("cancelConfirm").addEventListener("click", cancelConfirm);
  $("cancelConfirmTop").addEventListener("click", cancelConfirm);
  $("confirmDialog").addEventListener("cancel", function () { pendingConfirm = null; });
  $("acceptConfirm").addEventListener("click",function () {
    var action = pendingConfirm;
    cancelConfirm();
    if (action) execute(action, true);
  });
  $("disconnectButton").addEventListener("click",function () { showPairing(); });
  $("fullscreenButton").addEventListener("click", async function () {
    try {
      if (document.fullscreenElement) await document.exitFullscreen();
      else if (document.documentElement.requestFullscreen) await document.documentElement.requestFullscreen();
      else U.toast("Use “Adicionar à tela inicial” no menu do navegador para abrir como app.");
    } catch (_) { U.toast("A tela cheia não está disponível neste navegador."); }
  });
  document.addEventListener("visibilitychange",function () {
    if (!document.hidden && token) { clearTimeout(timer); poll(); }
  });
  window.addEventListener("online",function () { if (token) { clearTimeout(timer); poll(); } });
  var initialPairCode = new URLSearchParams(location.search).get("pair"); if(initialPairCode) $("pairCode").value = initialPairCode.toUpperCase();
  if (token) { showController(); connection(false); poll(); }
  else showPairing();
})();