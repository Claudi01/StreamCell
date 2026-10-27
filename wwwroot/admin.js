(function () {
  "use strict";
  var U = window.StreamCellUI, $ = function (id) { return document.getElementById(id); };
  var token = "", config = null, pageId = "", selectedSlot = 0, dirty = false, saving = false, dragSlot = null;
  var obs = {connected:false,scenes:[],inputs:[]}, apps = null, info = null, shownLoadWarning = "";
  var textCallback = null, confirmCallback = null;
  var defaults = {
    app:{label:"Abrir aplicativo",icon:"app",color:"#9b8aff"},
    url:{label:"Abrir site",icon:"globe",color:"#5b9cff"},
    soundpad:{label:"Tocar som",icon:"music",color:"#f3cb69"},
    soundpad_stop:{label:"Parar sons",icon:"stop",color:"#ff9a61"},
    hotkey:{label:"Atalho",icon:"keyboard",color:"#d97de8"},
    media:{label:"Mídia",icon:"play",color:"#37d3bd"},
    obs_scene:{label:"Trocar cena",icon:"scene",color:"#5b9cff"},
    obs_mute:{label:"Microfone",icon:"mic",color:"#72d48b"},
    obs_stream:{label:"Ao vivo",icon:"broadcast",color:"#ff738d"},
    obs_record:{label:"Gravação",icon:"record",color:"#ff9a61"}
  };
  U.decorate();
  try {
    if (location.hash.length > 1) {
      token = decodeURIComponent(location.hash.slice(1));
      sessionStorage.setItem("streamcell.adminToken",token);
      history.replaceState(null,"",location.pathname + location.search);
    } else token = sessionStorage.getItem("streamcell.adminToken") || "";
  } catch (_) {
    token = location.hash.slice(1);
    if (location.hash) history.replaceState(null,"",location.pathname + location.search);
  }

  async function api(path, body) {
    var controller = new AbortController(), timeout = setTimeout(function () { controller.abort(); }, path === "/api/admin/browse" ? 180000 : 15000);
    try {
      var response = await fetch(path, {
        method:body === undefined ? "GET" : "POST", headers:{"Content-Type":"application/json","X-StreamCell-Admin":token},
        body:body === undefined ? undefined : JSON.stringify(body), cache:"no-store", signal:controller.signal
      });
      var data = await response.json().catch(function () { return {}; });
      if (!response.ok) {
        var error = new Error(data.error || "Não foi possível concluir esta ação.");
        error.status = response.status;
        if (response.status === 401 || response.status === 403) accessError("Abra novamente o editor pelo programa StreamCell no computador.");
        throw error;
      }
      return data;
    } catch (error) {
      if (error.name === "AbortError") throw new Error("A operação demorou demais. Tente novamente.");
      if (error instanceof TypeError) throw new Error("Não foi possível conectar ao StreamCell. Confira se o programa está aberto no PC.");
      throw error;
    } finally { clearTimeout(timeout); }
  }
  function accessError(message) {
    $("editorWorkspace").hidden = true;
    $("adminError").hidden = false;
    $("adminErrorText").textContent = message;
    $("saveStatus").textContent = "Editor desconectado";
    ["saveConfig","connectPhone","openSettings"].forEach(function (id) { $(id).disabled = true; });
  }
  function currentPage() { return config && config.pages.find(function (page) { return page.id === pageId; }); }
  function currentButton() { var page = currentPage(); return page && page.buttons.find(function (button) { return button.slot === selectedSlot; }); }
  function markDirty() {
    dirty = true;
    $("saveStatus").textContent = "Alterações não salvas";
    $("saveStatus").classList.add("unsaved");
    $("saveConfig").disabled = false;
  }
  function markSaved() {
    dirty = false;
    $("saveStatus").textContent = "Tudo salvo";
    $("saveStatus").classList.remove("unsaved");
    $("saveConfig").disabled = true;
  }
  function adoptConfig(data) {
    config = data;
    if (!config.obs) config.obs = {port:4455,passwordSet:false};
    if (!config.obs.port) config.obs.port = 4455;
    if (!Array.isArray(config.pages)) config.pages = [];
    if (!config.pages.length) config.pages.push({id:U.id("page"),name:"Principal",buttons:[]});
    config.pages.forEach(function (page) { if (!Array.isArray(page.buttons)) page.buttons = []; });
    if (!currentPage()) pageId = config.pages[0].id;
    config.columns = 5;
  }

  function renderPages() {
    var list = $("pageList");
    list.replaceChildren();
    config.pages.forEach(function (page,index) {
      var button = document.createElement("button");
      button.type = "button";
      button.className = "page-list-button" + (page.id === pageId ? " selected" : "");
      button.innerHTML = U.icon("grid");
      var name = document.createElement("span");
      name.textContent = page.name;
      var count = document.createElement("span");
      count.className = "page-item-count";
      count.textContent = page.buttons.length;
      button.append(name,count);
      if (page.id === pageId) button.setAttribute("aria-current","page");
      button.addEventListener("click",function () { pageId = page.id; selectedSlot = 0; renderAll(); });
      list.append(button);
    });
    var page = currentPage();
    $("currentPageTitle").textContent = page.name;
    $("canvasPageNumber").textContent = String(config.pages.indexOf(page) + 1).padStart(2,"0") + " / " + String(config.pages.length).padStart(2,"0");
    $("deletePage").disabled = config.pages.length <= 1;
    $("addPage").disabled = config.pages.length >= 20;
  }
  function renderGrid() {
    var page = currentPage(), grid = $("editorGrid");
    grid.replaceChildren();
    $("keyCount").textContent = page.buttons.length + " / 15 teclas";
    for (var slot = 0; slot < 15; slot++) {
      var button = page.buttons.find(function (item) { return item.slot === slot; });
      var key = U.key(button,{slot:slot,edit:true});
      key.classList.toggle("is-selected", slot === selectedSlot);
      key.setAttribute("aria-pressed", slot === selectedSlot ? "true" : "false");
      key.draggable = !!button;
      (function (position,item,el) {
        el.addEventListener("click",function () { selectedSlot = position; renderGrid(); renderInspector(); });
        el.addEventListener("dragstart",function (event) {
          if (!item) { event.preventDefault(); return; }
          dragSlot = position;
          event.dataTransfer.effectAllowed = "move";
          event.dataTransfer.setData("text/plain",String(position));
          el.classList.add("dragging");
        });
        el.addEventListener("dragend",function () {
          dragSlot = null;
          grid.querySelectorAll(".dragging,.drag-over").forEach(function (node) { node.classList.remove("dragging","drag-over"); });
        });
        el.addEventListener("dragover",function (event) {
          if (dragSlot === null || dragSlot === position) return;
          event.preventDefault();
          event.dataTransfer.dropEffect = "move";
          el.classList.add("drag-over");
        });
        el.addEventListener("dragleave",function () { el.classList.remove("drag-over"); });
        el.addEventListener("drop",function (event) {
          event.preventDefault();
          el.classList.remove("drag-over");
          if (dragSlot !== null && dragSlot !== position) moveButton(dragSlot,position);
          dragSlot = null;
        });
      })(slot,button,key);
      grid.append(key);
    }
  }
  function moveButton(from,to) {
    var page = currentPage(), source = page.buttons.find(function (button) { return button.slot === from; });
    if (!source || to < 0 || to > 14) return;
    var destination = page.buttons.find(function (button) { return button.slot === to; });
    source.slot = to;
    if (destination) destination.slot = from;
    selectedSlot = to;
    markDirty();
    renderAll();
  }
  function renderPickers() {
    var button = currentButton();
    $("iconPicker").replaceChildren();
    Object.keys(U.iconNames).forEach(function (name) {
      var choice = document.createElement("button");
      choice.type = "button";
      choice.className = "icon-choice" + (button && button.icon === name ? " selected" : "");
      choice.innerHTML = U.icon(name);
      choice.title = U.iconNames[name];
      choice.setAttribute("aria-label",U.iconNames[name]);
      choice.setAttribute("aria-pressed",button && button.icon === name ? "true" : "false");
      choice.addEventListener("click",function () { var current = currentButton(); if (!current) return; current.icon = name; markDirty(); renderGrid(); renderPickers(); });
      $("iconPicker").append(choice);
    });
    $("colorPicker").replaceChildren();
    var colors = U.colors.slice();
    if (button && colors.indexOf(button.color) < 0) colors.push(U.color(button.color));
    colors.forEach(function (color) {
      var choice = document.createElement("button");
      choice.type = "button";
      choice.className = "color-choice" + (button && button.color === color ? " selected" : "");
      choice.style.setProperty("--swatch",color);
      choice.title = color;
      choice.setAttribute("aria-label","Cor " + color);
      choice.setAttribute("aria-pressed",button && button.color === color ? "true" : "false");
      choice.addEventListener("click",function () { var current = currentButton(); if (!current) return; current.color = color; markDirty(); renderGrid(); renderPickers(); });
      $("colorPicker").append(choice);
    });
  }
  function renderInspector() {
    var button = currentButton();
    $("selectedSlot").textContent = String(selectedSlot + 1).padStart(2,"0");
    $("inspectorEmpty").hidden = !!button;
    $("buttonForm").hidden = !button;
    if (!button) return;
    $("buttonType").value = button.type;
    $("buttonLabel").value = button.label || "";
    $("buttonAppTarget").value = button.target || "";
    $("buttonArguments").value = button.arguments || "";
    $("buttonUrl").value = button.target || "";
    $("buttonSoundIndex").value = button.soundIndex || 1;
    $("buttonKeys").value = button.keys || "";
    $("buttonMedia").value = button.target || "play_pause";
    $("buttonConfirm").checked = !!button.confirm || button.type === "obs_stream";
    $("buttonConfirm").disabled = button.type === "obs_stream";
    $("buttonConfirm").closest("label").title = button.type === "obs_stream" ? "Iniciar ou parar a live sempre pede confirmação." : "";
    $("buttonSlot").replaceChildren();
    for (var i = 0; i < 15; i++) {
      var option = document.createElement("option");
      option.value = i;
      var other = currentPage().buttons.find(function (item) { return item.slot === i; });
      option.textContent = "Tecla " + String(i + 1).padStart(2,"0") + (other && i !== selectedSlot ? " · trocar com " + other.label : "");
      $("buttonSlot").append(option);
    }
    $("buttonSlot").value = selectedSlot;
    renderPickers();
    renderActionFields();
  }
  function renderActionFields() {
    var button = currentButton();
    if (!button) return;
    ["actionApp","actionUrl","actionSoundpad","actionSoundpadStop","actionHotkey","actionMedia","actionObs"].forEach(function (id) { $(id).hidden = true; });
    var mapping = {app:"actionApp",url:"actionUrl",soundpad:"actionSoundpad",soundpad_stop:"actionSoundpadStop",hotkey:"actionHotkey",media:"actionMedia"};
    if (mapping[button.type]) $(mapping[button.type]).hidden = false;
    if (button.type.indexOf("obs_") === 0) {
      $("actionObs").hidden = false;
      $("inspectorObsStatus").textContent = obs.connected ? "OBS conectado" : "OBS desconectado";
      $("inspectorObsStatus").style.color = obs.connected ? "#80cdaa" : "#cfad79";
      var hasTarget = button.type === "obs_scene" || button.type === "obs_mute";
      $("obsTargetField").hidden = !hasTarget;
      $("obsTargetLabel").textContent = button.type === "obs_scene" ? "Cena" : "Entrada de áudio";
      var targetSelect = $("buttonObsTarget");
      targetSelect.replaceChildren();
      var placeholder = document.createElement("option");
      placeholder.value = "";
      placeholder.textContent = obs.connected ? "Selecione uma opção" : "Conecte o OBS para listar";
      targetSelect.append(placeholder);
      var values = button.type === "obs_scene" ? obs.scenes : obs.inputs;
      values = Array.isArray(values) ? values.slice() : [];
      if (button.target && values.indexOf(button.target) < 0) values.unshift(button.target);
      values.forEach(function (value) { var option = document.createElement("option"); option.value = value; option.textContent = value; targetSelect.append(option); });
      targetSelect.value = button.target || "";
      var help = {
        obs_scene:"Ativa a cena escolhida. A tecla acende quando ela está no ar.",
        obs_mute:"Alterna o mudo da entrada escolhida. A tecla acende quando o áudio está silenciado.",
        obs_stream:"Inicia ou encerra a transmissão no OBS. A tecla acende enquanto a live estiver ativa.",
        obs_record:"Inicia ou encerra a gravação no OBS. A tecla acende durante a gravação."
      };
      $("obsActionHelp").textContent = help[button.type] + (!obs.connected && obs.error ? " " + obs.error : "");
    }
  }
  function renderAll() { renderPages(); renderGrid(); renderInspector(); }

  function newButton(type) {
    var preset = defaults[type];
    return {id:U.id("key"),slot:selectedSlot,label:preset.label,icon:preset.icon,color:preset.color,type:type,target:type === "media" ? "play_pause" : "",arguments:"",soundIndex:1,keys:"",confirm:type === "obs_stream"};
  }
  $("createButton").addEventListener("click",function () {
    if (!config || currentButton()) return;
    currentPage().buttons.push(newButton("app"));
    markDirty(); renderAll();
    $("buttonType").focus();
  });
  $("buttonForm").addEventListener("submit",function (event) { event.preventDefault(); });
  $("buttonType").addEventListener("change",function () {
    var button = currentButton(), old = defaults[button.type], next = defaults[this.value];
    if (!next) return;
    if (button.label === old.label) button.label = next.label;
    if (button.icon === old.icon) button.icon = next.icon;
    if (button.color === old.color) button.color = next.color;
    button.type = this.value;
    button.target = button.type === "media" ? "play_pause" : "";
    button.arguments = ""; button.keys = ""; button.soundIndex = 1;
    button.confirm = button.type === "obs_stream";
    markDirty(); renderGrid(); renderInspector();
  });
  function bindField(id,property,transform) {
    $(id).addEventListener("input",function () {
      var button = currentButton();
      if (!button) return;
      button[property] = transform ? transform(this) : this.value;
      markDirty();
      if (property === "label") renderGrid();
    });
  }
  bindField("buttonLabel","label");
  bindField("buttonAppTarget","target");
  bindField("buttonArguments","arguments");
  bindField("buttonUrl","target");
  bindField("buttonSoundIndex","soundIndex",function (el) { return Number(el.value); });
  bindField("buttonKeys","keys",function (el) { return el.value.toUpperCase(); });
  bindField("buttonMedia","target");
  bindField("buttonObsTarget","target");
  bindField("buttonConfirm","confirm",function (el) { return el.checked; });
  $("buttonSlot").addEventListener("change",function () { moveButton(selectedSlot,Number(this.value)); });
  $("deleteButton").addEventListener("click",function () {
    var button = currentButton();
    if (!button) return;
    askConfirm("Remover tecla?", "“" + button.label + "” será removida deste painel quando você salvar.",function () {
      currentPage().buttons = currentPage().buttons.filter(function (item) { return item.id !== button.id; });
      markDirty(); renderAll();
    });
  });

  function askText(title,value,callback) {
    textCallback = callback;
    $("textDialogTitle").textContent = title;
    $("textDialogInput").value = value;
    $("textDialog").showModal();
    $("textDialogInput").focus(); $("textDialogInput").select();
  }
  $("textDialogForm").addEventListener("submit",function (event) {
    event.preventDefault();
    var value = $("textDialogInput").value.trim();
    if (!value) { $("textDialogInput").focus(); return; }
    var callback = textCallback; textCallback = null;
    $("textDialog").close();
    if (callback) callback(value);
  });
  function askConfirm(title,text,callback) {
    confirmCallback = callback;
    $("adminConfirmTitle").textContent = title;
    $("adminConfirmText").textContent = text;
    $("adminConfirmDialog").showModal();
  }
  $("adminConfirmAccept").addEventListener("click",function () {
    var callback = confirmCallback; confirmCallback = null;
    $("adminConfirmDialog").close();
    if (callback) callback();
  });
  document.querySelectorAll("[data-close]").forEach(function (button) {
    button.addEventListener("click",function () { $(button.dataset.close).close(); });
  });
  $("textDialog").addEventListener("close",function () { textCallback = null; });
  $("adminConfirmDialog").addEventListener("close",function () { confirmCallback = null; });
  $("addPage").addEventListener("click",function () {
    if (config.pages.length >= 20) { U.toast("Seu deck aceita até 20 páginas.",true); return; }
    askText("Nova página","Nova página",function (name) {
      var page = {id:U.id("page"),name:name,buttons:[]};
      config.pages.push(page); pageId = page.id; selectedSlot = 0;
      markDirty(); renderAll();
    });
  });
  $("renamePage").addEventListener("click",function () {
    askText("Renomear página",currentPage().name,function (name) { currentPage().name = name; markDirty(); renderPages(); });
  });
  $("deletePage").addEventListener("click",function () {
    if (config.pages.length <= 1) return;
    var page = currentPage();
    askConfirm("Excluir página?", "A página “" + page.name + "” e suas " + page.buttons.length + " teclas serão removidas quando você salvar.",function () {
      config.pages = config.pages.filter(function (item) { return item.id !== page.id; });
      pageId = config.pages[0].id; selectedSlot = 0; markDirty(); renderAll();
    });
  });

  function openSettings() {
    if (!config) return;
    $("configTitle").value = config.title || "Meu StreamCell";
    $("obsPort").value = config.obs.port || 4455;
    $("obsPassword").value = config.obs.password || "";
    $("obsClearPassword").checked = !!config.obs.clearPassword;
    $("obsPasswordHelp").textContent = config.obs.passwordSet ? "Já existe uma senha salva. Deixe o campo vazio para mantê-la." : "Informe a senha definida no OBS. Se a autenticação estiver desativada, deixe vazio.";
    $("soundpadPath").value = config.soundpadPath || "";
    $("settingsDialog").showModal();
  }
  $("openSettings").addEventListener("click",openSettings);
  $("soundpadSettings").addEventListener("click",openSettings);
  $("obsSettings").addEventListener("click",openSettings);
  $("obsPassword").addEventListener("input",function () { if (this.value) $("obsClearPassword").checked = false; });
  $("obsClearPassword").addEventListener("change",function () { if (this.checked) $("obsPassword").value = ""; });
  $("applySettings").addEventListener("click",function () {
    var title = $("configTitle").value.trim(), port = Number($("obsPort").value);
    if (!title) { U.toast("Dê um nome para o seu deck.",true); $("configTitle").focus(); return; }
    if (!Number.isInteger(port) || port < 1 || port > 65535) { U.toast("Informe uma porta entre 1 e 65535.",true); $("obsPort").focus(); return; }
    config.title = title; config.obs.port = port; config.soundpadPath = $("soundpadPath").value.trim();
    delete config.obs.password;
    config.obs.clearPassword = $("obsClearPassword").checked;
    if ($("obsPassword").value) config.obs.password = $("obsPassword").value;
    markDirty(); $("settingsDialog").close();
    U.toast("Configurações aplicadas. Salve as alterações para ativá-las.");
  });
  async function browse(purpose,button) {
    button.disabled = true;
    try {
      var result = await api("/api/admin/browse",{purpose:purpose});
      if (result.path) {
        if (purpose === "soundpad") $("soundpadPath").value = result.path;
        else {
          var current = currentButton();
          if (!current || current.type !== "app") return;
          current.target = result.path; $("buttonAppTarget").value = result.path;
          if (current.label === defaults.app.label) {
            current.label = result.path.split(/[\\/]/).pop().replace(/\.(exe|lnk|url)$/i,"").slice(0,40);
            $("buttonLabel").value = current.label;
          }
          markDirty(); renderGrid();
        }
      }
    } catch (error) { U.toast(error.message,true); }
    finally { button.disabled = false; }
  }
  $("browseApp").addEventListener("click",function () { browse("app",this); });
  $("browseSoundpad").addEventListener("click",function () { browse("soundpad",this); });

  function renderApps() {
    var list = $("appsList"), query = $("appSearch").value.trim().toLocaleLowerCase("pt-BR");
    list.replaceChildren();
    var results = (apps || []).filter(function (app) { return (app.name || "").toLocaleLowerCase("pt-BR").includes(query); }).slice(0,150);
    if (!results.length) {
      var empty = document.createElement("p"); empty.className = "apps-empty"; empty.textContent = apps ? "Nenhum aplicativo encontrado." : "Buscando aplicativos instalados…"; list.append(empty); return;
    }
    results.forEach(function (app) {
      var button = document.createElement("button"); button.type = "button"; button.className = "app-result"; button.innerHTML = U.icon("app");
      var text = document.createElement("span"), name = document.createElement("strong"), path = document.createElement("small");
      name.textContent = app.name; path.textContent = app.path; text.append(name,path); button.append(text);
      button.addEventListener("click",function () {
        var current = currentButton();
        if (!current || current.type !== "app") return;
        current.target = app.path;
        if (current.label === defaults.app.label || !current.label) current.label = app.name.slice(0,40);
        markDirty(); renderGrid(); renderInspector(); $("appsDialog").close();
      });
      list.append(button);
    });
  }
  $("chooseApp").addEventListener("click",async function () {
    $("appSearch").value = ""; $("appsDialog").showModal(); renderApps(); $("appSearch").focus();
    if (apps) return;
    try { var result = await api("/api/admin/apps"); apps = result.apps || []; renderApps(); }
    catch (error) { $("appsList").textContent = error.message; U.toast(error.message,true); }
  });
  $("appSearch").addEventListener("input",renderApps);
  async function refreshObs(silent) {
    $("refreshObs").disabled = true;
    try {
      obs = await api("/api/admin/obs");
      if (!Array.isArray(obs.scenes)) obs.scenes = [];
      if (!Array.isArray(obs.inputs)) obs.inputs = [];
      renderActionFields();
      if (!silent) U.toast(obs.connected ? "OBS conectado. Cenas e entradas atualizadas." : obs.error || "Ative o WebSocket no OBS e confira as configurações.",!obs.connected);
    } catch (error) {
      obs = {connected:false,error:error.message,scenes:[],inputs:[]};
      renderActionFields();
      if (!silent) U.toast(error.message,true);
    } finally { $("refreshObs").disabled = false; }
  }
  $("refreshObs").addEventListener("click",function () { refreshObs(false); });

  async function copyText(value) {
    try {
      if (navigator.clipboard && window.isSecureContext) await navigator.clipboard.writeText(value);
      else {
        var input = document.createElement("textarea"); input.value = value; input.style.position = "fixed"; input.style.opacity = "0"; document.body.append(input); input.select();
        var copied = document.execCommand("copy"); input.remove();
        if (!copied) throw new Error("copy");
      }
      U.toast("Endereço copiado.");
    } catch (_) { U.toast("Selecione o endereço e copie manualmente.",true); }
  }
  function renderPhoneInfo(data) {
    info = data;
    if (data.warning && data.warning !== shownLoadWarning) { U.toast(data.warning,true); shownLoadWarning = data.warning; }
    $("phonePairCode").textContent = data.pairCode || "------";
    $("phoneCount").textContent = (data.phoneCount || 0) + ((data.phoneCount || 0) === 1 ? " celular conectado" : " celulares conectados");
    $("versionLabel").textContent = "STREAMCELL" + (data.version ? " / " + data.version : "");
    var list = $("phoneAddresses"); list.replaceChildren();
    (data.addresses || []).forEach(function (address) {
      var row = document.createElement("div"); row.className = "address-row";
      var value = document.createElement("code"); value.textContent = address;
      var copy = document.createElement("button"); copy.className = "icon-button"; copy.type = "button"; copy.innerHTML = U.icon("copy"); copy.title = "Copiar endereço"; copy.setAttribute("aria-label","Copiar endereço");
      copy.addEventListener("click",function () { copyText(address); });
      row.append(value,copy); list.append(row);
    });
    if (!(data.addresses || []).length) {
      var message = document.createElement("p"); message.className = "field-help"; message.textContent = "Conecte o computador a uma rede Wi-Fi ou Ethernet para mostrar o endereço."; list.append(message);
    }
    $("phoneInfoError").hidden = true;
  }
  async function loadInfo() { var data = await api("/api/admin/info"); renderPhoneInfo(data); }
  $("connectPhone").addEventListener("click",async function () {
    $("phoneDialog").showModal();
    try { await loadInfo(); }
    catch (error) { $("phoneInfoError").textContent = error.message; $("phoneInfoError").hidden = false; }
  });
  $("revokePhones").addEventListener("click",function () {
    askConfirm("Desconectar todos os celulares?", "Os celulares precisarão usar o novo código para conectar novamente.",async function () {
      $("revokePhones").disabled = true;
      try { renderPhoneInfo(await api("/api/admin/revoke",{})); U.toast("Conexões removidas. Use o novo código para reconectar."); }
      catch (error) { U.toast(error.message,true); }
      finally { $("revokePhones").disabled = false; }
    });
  });

  function validate() {
    for (var p = 0; p < config.pages.length; p++) {
      var page = config.pages[p];
      for (var b = 0; b < page.buttons.length; b++) {
        var button = page.buttons[b], message = "";
        button.label = (button.label || "").trim();
        if (button.type === "obs_stream") button.confirm = true;
        if (!button.label) message = "Informe o nome da tecla.";
        else if (button.type === "app" && !(button.target || "").trim()) message = "Escolha o aplicativo desta tecla.";
        else if (button.type === "url") {
          try { var url = new URL(button.target); if (!["http:","https:"].includes(url.protocol)) throw new Error(); }
          catch (_) { message = "Informe um endereço começando com https:// ou http://."; }
        } else if (button.type === "soundpad" && (!Number.isInteger(button.soundIndex) || button.soundIndex < 1 || button.soundIndex > 100000)) message = "Informe um número de som entre 1 e 100000.";
        else if (button.type === "hotkey" && !(button.keys || "").trim()) message = "Informe o atalho de teclado.";
        else if ((button.type === "obs_scene" || button.type === "obs_mute") && !button.target) message = "Escolha a cena ou entrada de áudio do OBS.";
        if (message) {
          pageId = page.id; selectedSlot = button.slot; renderAll();
          U.toast(page.name + " · tecla " + (button.slot + 1) + ": " + message,true);
          return false;
        }
      }
    }
    return true;
  }
  $("saveConfig").addEventListener("click",async function () {
    if (!config || saving || !validate()) return;
    saving = true;
    $("saveConfig").disabled = true;
    $("editorWorkspace").inert = true;
    $("openSettings").disabled = true;
    $("saveStatus").textContent = "Salvando…";
    var payload = JSON.parse(JSON.stringify(config)); payload.columns = 5;
    delete payload.obs.passwordSet;
    try {
      var result = await api("/api/admin/config",payload);
      if (result.pages) adoptConfig(result);
      else if (result.config && result.config.pages) adoptConfig(result.config);
      else adoptConfig(await api("/api/admin/config"));
      markSaved(); renderAll();
      U.toast("Deck salvo. Seu celular já pode receber as mudanças.");
      refreshObs(true);
    } catch (error) {
      markDirty(); U.toast(error.message,true);
    } finally {
      saving = false; $("editorWorkspace").inert = false; $("openSettings").disabled = $("adminError").hidden ? false : true;
    }
  });
  window.addEventListener("beforeunload",function (event) {
    if (!dirty) return;
    event.preventDefault(); event.returnValue = "";
  });
  async function init() {
    ["saveConfig","connectPhone","openSettings"].forEach(function (id) { $(id).disabled = true; });
    if (!token) { accessError("Abra no navegador o endereço “Editor do PC” exibido na janela do StreamCell. Ele libera esta página para configurar seu deck."); return; }
    try {
      adoptConfig(await api("/api/admin/config"));
      $("editorWorkspace").hidden = false;
      $("adminError").hidden = true;
      $("connectPhone").disabled = false; $("openSettings").disabled = false;
      markSaved(); renderAll();
      refreshObs(true);
      loadInfo().catch(function () {});
    } catch (error) { accessError(error.message); }
  }
  init();
})();