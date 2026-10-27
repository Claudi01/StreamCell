(function () {
  "use strict";
  var paths = {
    grid: '<rect x="3" y="3" width="6" height="6" rx="1.5"/><rect x="15" y="3" width="6" height="6" rx="1.5"/><rect x="3" y="15" width="6" height="6" rx="1.5"/><rect x="15" y="15" width="6" height="6" rx="1.5"/>',
    play: '<path d="m9 5 11 7-11 7Z"/>',
    pause: '<path d="M8 5v14M16 5v14"/>',
    music: '<path d="M9 18V5l11-2v13M9 8l11-2"/><ellipse cx="6" cy="18" rx="3" ry="3"/><ellipse cx="17" cy="16" rx="3" ry="3"/>',
    sound: '<path d="m11 5-6 4H2v6h3l6 4ZM15 8a6 6 0 0 1 0 8M18 5a10 10 0 0 1 0 14"/>',
    mic: '<rect x="9" y="2" width="6" height="12" rx="3"/><path d="M5 10v2a7 7 0 0 0 14 0v-2M12 19v3M8 22h8"/>',
    mute: '<path d="m11 5-6 4H2v6h3l6 4ZM17 9l5 6M22 9l-5 6"/>',
    monitor: '<rect x="2" y="3" width="20" height="14" rx="2"/><path d="M12 17v4M8 21h8"/>',
    app: '<rect x="3" y="3" width="18" height="18" rx="4"/><path d="M8 12h8M12 8v8"/>',
    link: '<path d="m10 13 4-4M8 16l-1 1a4 4 0 0 1-6-6l4-4a4 4 0 0 1 6 0m2 10a4 4 0 0 0 6 0l4-4a4 4 0 0 0-6-6l-1 1" transform="translate(1 0) scale(.91)"/>',
    globe: '<circle cx="12" cy="12" r="9"/><ellipse cx="12" cy="12" rx="4" ry="9"/><path d="M3 12h18"/>',
    camera: '<path d="m16 9 5-3v12l-5-3"/><rect x="2" y="5" width="14" height="14" rx="3"/>',
    broadcast: '<circle cx="12" cy="12" r="2"/><path d="M7.8 7.8a6 6 0 0 0 0 8.4m8.4-8.4a6 6 0 0 1 0 8.4M5 5a10 10 0 0 0 0 14M19 5a10 10 0 0 1 0 14"/>',
    record: '<circle cx="12" cy="12" r="9"/><circle cx="12" cy="12" r="4" fill="currentColor" stroke="none"/>',
    scene: '<rect x="3" y="5" width="18" height="16" rx="2"/><path d="M3 10h18M7 2l4 3M14 2l4 3"/>',
    keyboard: '<rect x="2" y="5" width="20" height="14" rx="3"/><path d="M6 9h.01M10 9h.01M14 9h.01M18 9h.01M6 13h.01M10 13h.01M14 13h.01M18 13h.01M8 16h8"/>',
    bolt: '<path d="m13 2-9 12h7l-1 8 10-12h-7Z"/>',
    star: '<path d="m12 3 2.8 5.7 6.2.9-4.5 4.4 1.1 6.2-5.6-3-5.6 3 1.1-6.2L3 9.6l6.2-.9Z"/>',
    heart: '<path d="M20 4a5 5 0 0 0-8 2 5 5 0 0 0-8-2C-3 10 12 21 12 21S27 10 20 4Z"/>',
    coffee: '<path d="M4 8h13v8a5 5 0 0 1-5 5H9a5 5 0 0 1-5-5ZM17 9h2a3 3 0 0 1 0 6h-2M7 2v2M12 2v2"/>',
    game: '<path d="M8 7h8c4 0 5 5 5 10 0 3-3 3-6-1H9c-3 4-6 4-6 1 0-5 1-10 5-10Z"/><path d="M7 10v5M4.5 12.5h5M16 11h.01M18 14h.01"/>',
    chat: '<path d="M21 11a9 9 0 0 1-9 9H4l-2 2V11a9 9 0 0 1 19 0Z"/><path d="M7 10h10M7 14h6"/>',
    folder: '<path d="M3 5h6l2 3h10v11a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5Z"/>',
    stop: '<rect x="5" y="5" width="14" height="14" rx="3"/>',
    next: '<path d="m4 5 10 7-10 7ZM19 5v14"/>',
    previous: '<path d="m20 5-10 7 10 7ZM5 5v14"/>',
    volumeup: '<path d="m11 5-6 4H2v6h3l6 4ZM16 12h6M19 9v6"/>',
    volumedown: '<path d="m11 5-6 4H2v6h3l6 4ZM16 12h6"/>',
    phone: '<rect x="6" y="2" width="12" height="20" rx="3"/><path d="M10 18h4"/>',
    settings: '<path d="m12 3 2 3 3-1 1 3 3 2-2 3 1 3-3 1-2 3-3-2-3 1-1-3-3-2 2-3-1-3 3-1Z"/><circle cx="12" cy="12" r="3"/>',
    save: '<path d="M4 3h13l4 4v14H3V3ZM7 3v6h9V3M7 21v-8h10v8"/>',
    plus: '<path d="M12 5v14M5 12h14"/>',
    close: '<path d="m6 6 12 12M18 6 6 18"/>',
    left: '<path d="m14 6-6 6 6 6"/>',
    right: '<path d="m10 6 6 6-6 6"/>',
    check: '<path d="m5 12 4 4L19 6"/>',
    refresh: '<path d="M20 7V2l-3 3a8 8 0 1 0 3 12M20 7h-5"/>',
    expand: '<path d="M9 3H3v6M15 3h6v6M21 15v6h-6M3 15v6h6"/>',
    copy: '<rect x="8" y="8" width="13" height="13" rx="2"/><path d="M16 8V3H3v13h5"/>',
    trash: '<path d="M3 6h18M9 6V3h6v3M5 6l1 15h12l1-15M10 10v7M14 10v7"/>',
    grip: '<path d="M9 5h.01M15 5h.01M9 12h.01M15 12h.01M9 19h.01M15 19h.01"/>'
  };
  var iconNames = {grid:"Painel",play:"Reproduzir",pause:"Pausa",music:"Música",sound:"Som",mic:"Microfone",mute:"Mudo",monitor:"Monitor",app:"Aplicativo",link:"Link",globe:"Internet",camera:"Câmera",broadcast:"Ao vivo",record:"Gravar",scene:"Cena",keyboard:"Atalho",bolt:"Raio",star:"Estrela",heart:"Coração",coffee:"Intervalo",game:"Jogo",chat:"Chat",folder:"Pasta",stop:"Parar",next:"Próximo",previous:"Anterior",volumeup:"Mais volume",volumedown:"Menos volume"};
  function icon(name, className) {
    return '<svg class="icon ' + (className || "") + '" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">' + (paths[name] || paths.bolt) + '</svg>';
  }
  function decorate(root) {
    (root || document).querySelectorAll("[data-icon]").forEach(function (el) { el.innerHTML = icon(el.dataset.icon); });
  }
  function color(value) { return /^#[a-f0-9]{6}$/i.test(value || "") ? value : "#9b8aff"; }
  function key(button, options) {
    options = options || {};
    var el = document.createElement("button");
    el.type = "button";
    el.className = "deck-key" + (!button ? " key-empty" : "") + (button && button.active ? " is-active" : "");
    el.dataset.slot = options.slot;
    if (button) {
      el.style.setProperty("--key-color", color(button.color));
      var symbol = document.createElement("span");
      symbol.className = "key-symbol";
      symbol.innerHTML = icon(button.icon);
      var label = document.createElement("span");
      label.className = "key-label";
      label.textContent = button.label || "Sem título";
      el.append(symbol, label);
      if (button.active) {
        var dot = document.createElement("span");
        dot.className = "key-active-dot";
        el.append(dot);
      }
      el.setAttribute("aria-label", button.label + (button.active ? ", ativo" : ""));
      if (!options.edit && button.available === false) {
        el.classList.add("is-unavailable");
        el.disabled = true;
        el.title = "Conecte o OBS no computador para usar esta tecla";
      }
    } else {
      el.innerHTML = options.edit ? icon("plus") : '<span class="empty-dot"></span>';
      el.setAttribute("aria-label", options.edit ? "Configurar tecla " + (options.slot + 1) : "Espaço vazio");
      if (!options.edit) { el.disabled = true; el.setAttribute("aria-hidden", "true"); }
    }
    if (options.edit) {
      var position = document.createElement("span");
      position.className = "key-position";
      position.textContent = String(options.slot + 1).padStart(2,"0");
      el.append(position);
    }
    return el;
  }
  var toastTimer;
  function toast(message, error) {
    var el = document.getElementById("toast");
    if (!el) return;
    clearTimeout(toastTimer);
    el.textContent = message;
    el.className = "toast visible" + (error ? " toast-error" : "");
    toastTimer = setTimeout(function () { el.className = "toast"; }, error ? 6000 : 3200);
  }
  function id(prefix) {
    return prefix + "-" + (window.crypto && crypto.randomUUID ? crypto.randomUUID().replace(/-/g,"") : Date.now().toString(36) + Math.random().toString(36).slice(2));
  }
  window.StreamCellUI = {icon:icon,decorate:decorate,key:key,color:color,toast:toast,id:id,iconNames:iconNames,colors:["#9b8aff","#5b9cff","#37d3bd","#72d48b","#f3cb69","#ff9a61","#ff738d","#d97de8"]};
})();