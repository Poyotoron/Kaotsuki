const { invoke } = window.__TAURI__.core;
const { listen } = window.__TAURI__.event;
const { open, save } = window.__TAURI__.dialog;

const elements = {
  mapSelect: document.querySelector('#map-select'),
  mapRefresh: document.querySelector('#map-refresh'),
  mapOpen: document.querySelector('#map-open'),
  mapInfo: document.querySelector('#map-info'),
  receiveInfo: document.querySelector('#receive-info'),
  toggle: document.querySelector('#toggle-enabled'),
  toggleText: document.querySelector('#toggle-enabled .switch-text'),
  lipSync: document.querySelector('#toggle-lipsync'),
  lipSyncText: document.querySelector('#toggle-lipsync .switch-text'),
  reset: document.querySelector('#btn-reset'),
  saveExpression: document.querySelector('#btn-save-expression'),
  loadExpression: document.querySelector('#btn-load-expression'),
  targetInfo: document.querySelector('#target-info'),
  settings: document.querySelector('#btn-settings'),
  message: document.querySelector('#message'),
  search: document.querySelector('#search'),
  slots: document.querySelector('#slots'),
  dialog: document.querySelector('#settings-dialog'),
  host: document.querySelector('#set-host'),
  port: document.querySelector('#set-port'),
  hold: document.querySelector('#set-hold'),
  receive: document.querySelector('#set-receive'),
  receivePort: document.querySelector('#set-receive-port'),
  settingsError: document.querySelector('#settings-error'),
  settingsCancel: document.querySelector('#settings-cancel'),
  settingsSave: document.querySelector('#settings-save'),
};

let settings;
let currentMap = null;
let errorTimer = null;
const slotElements = new Map();
const meshViews = [];
const savedOpen = new Map();
let searching = false;

function errorMessage(error) {
  return typeof error === 'string' ? error : String(error);
}

function showMessage(message, kind) {
  elements.message.textContent = message;
  elements.message.dataset.kind = kind;
  elements.message.hidden = false;
  if (errorTimer !== null) {
    clearTimeout(errorTimer);
  }
  errorTimer = setTimeout(() => {
    elements.message.hidden = true;
    errorTimer = null;
  }, 5000);
}

function showError(message) {
  showMessage(message, 'error');
}

function renderEnabled(enabled) {
  elements.toggle.setAttribute('aria-checked', String(enabled));
  elements.toggleText.textContent = enabled ? 'ON' : 'OFF';
}

function renderLipSync(value) {
  elements.lipSync.setAttribute('aria-checked', String(value));
  elements.lipSyncText.textContent = value ? 'ON' : 'OFF';
}

function updateLipSyncAvailability() {
  elements.lipSync.disabled = !currentMap || !currentMap.hasLipSync;
}

function renderReceiveStatus(status) {
  if (status.state === 'listening') {
    elements.receiveInfo.textContent = `VRChat から受信中（ポート ${status.port}）`;
  } else if (status.state === 'failed') {
    elements.receiveInfo.textContent = `受信できません（ポート ${status.port} を他のアプリが使っている可能性があります）`;
  } else {
    elements.receiveInfo.textContent = 'VRChat から受信しない設定です';
  }
  elements.receiveInfo.dataset.kind = status.state === 'failed' ? 'error' : 'info';
}

function updateTargetInfo() {
  elements.targetInfo.textContent = `送信先 ${settings.host}:${settings.port}`;
}

function setControlsEnabled(enabled) {
  elements.reset.disabled = !enabled;
  elements.search.disabled = !enabled;
  elements.saveExpression.disabled = !enabled;
  elements.loadExpression.disabled = !enabled;
}

async function refreshMapList() {
  try {
    const maps = await invoke('list_maps');
    const fragment = document.createDocumentFragment();
    const empty = document.createElement('option');
    empty.value = '';
    empty.textContent = '（選択してください）';
    fragment.append(empty);
    for (const map of maps) {
      const option = document.createElement('option');
      option.value = map.path;
      option.textContent = map.avatarNames.length >= 2
        ? `${map.mapName}（${map.avatarNames.length} アバター）` : map.mapName;
      option.title = map.avatarNames.join('、');
      fragment.append(option);
    }
    elements.mapSelect.replaceChildren(fragment);
    if (currentMap) {
      elements.mapSelect.value = currentMap.path;
    }
  } catch (error) {
    showError(errorMessage(error));
  }
}

async function loadMap(path) {
  try {
    const map = await invoke('load_map', { path });
    currentMap = map;
    renderLipSync(false);
    updateLipSyncAvailability();
    settings.lastMapPath = path;
    renderSlots(map);
    elements.mapInfo.textContent = map.channelCount >= 2
      ? `${map.mapName} — ${map.slots.length} スロット（${map.channelCount} 枠）`
      : `${map.mapName} — ${map.slots.length} スロット`;
    setControlsEnabled(true);
    await refreshMapList();
  } catch (error) {
    showError(errorMessage(error));
  }
}

function renderSlots(map) {
  slotElements.clear();
  meshViews.length = 0;
  savedOpen.clear();
  searching = false;
  elements.search.value = '';
  const grouped = new Map();
  for (const slot of map.slots) {
    if (!grouped.has(slot.mesh)) {
      grouped.set(slot.mesh, []);
    }
    grouped.get(slot.mesh).push(slot);
  }

  const fragment = document.createDocumentFragment();
  for (const [mesh, slots] of grouped) {
    const details = document.createElement('details');
    details.className = 'mesh';
    details.open = true;
    const summary = document.createElement('summary');
    summary.textContent = `${mesh}（${slots.length}）`;
    details.append(summary);
    const rows = document.createElement('div');
    rows.className = 'rows';
    details.append(rows);
    const meshView = { details, rows: [], groups: [] };
    let previousGroup = '';
    let groupView = null;
    let groupRows = null;

    for (const slot of slots) {
      if (slot.group && slot.group !== previousGroup) {
        const groupDetails = document.createElement('details');
        groupDetails.className = 'group';
        const groupSummary = document.createElement('summary');
        groupRows = document.createElement('div');
        groupRows.className = 'rows';
        groupDetails.append(groupSummary, groupRows);
        details.append(groupDetails);
        groupView = { details: groupDetails, summary: groupSummary, name: slot.group, rows: [] };
        meshView.groups.push(groupView);
      }
      previousGroup = slot.group;

      const row = document.createElement('div');
      row.className = 'row';
      row.dataset.channel = String(slot.channel);
      row.dataset.index = String(slot.index);

      const name = document.createElement('span');
      name.className = 'name';
      name.textContent = slot.blendShape;
      name.title = slot.blendShape;

      const range = document.createElement('input');
      range.type = 'range';
      range.min = '0';
      range.max = '255';
      range.step = '1';
      range.value = String(slot.defaultValue);

      const weight = document.createElement('input');
      weight.type = 'text';
      weight.inputMode = 'decimal';
      weight.autocomplete = 'off';
      weight.spellcheck = false;
      weight.className = 'weight';
      weight.value = valueToWeight(slot.defaultValue);
      weight.title = '0〜100 の数値。値は約 0.4 刻みに丸められます';
      weight.setAttribute('aria-label', `${slot.blendShape} のウェイト`);

      const reset = document.createElement('button');
      reset.type = 'button';
      reset.className = 'reset';
      reset.textContent = '↺';
      reset.title = '既定値に戻す';

      row.append(name, range, weight, reset);
      (slot.group ? groupRows : rows).append(row);
      const view = { row, name: slot.blendShape, channel: slot.channel, index: slot.index, range, weight, defaultValue: slot.defaultValue };
      range.addEventListener('input', () => setSlotValue(view, Number(range.value)));
      reset.addEventListener('click', () => setSlotValue(view, view.defaultValue));
      weight.addEventListener('blur', () => commitWeight(view));
      weight.addEventListener('keydown', (event) => {
        if (event.key === 'Enter') {
          event.preventDefault();
          commitWeight(view);
          weight.blur();
        } else if (event.key === 'Escape') {
          event.preventDefault();
          weight.value = valueToWeight(Number(range.value));
          weight.blur();
        }
      });
      slotElements.set(`${slot.channel}:${slot.index}`, view);
      (slot.group ? groupView.rows : meshView.rows).push(view);
    }

    for (const group of meshView.groups) {
      group.summary.textContent = `${group.name}（${group.rows.length}）`;
    }
    fragment.append(details);
    meshViews.push(meshView);
  }
  elements.slots.replaceChildren(fragment);
}

function valueToWeight(value) {
  return (value * 100 / 255).toFixed(1);
}

function renderSlotValue(view, value) {
  view.range.value = String(value);
  view.weight.value = valueToWeight(value);
}

function setSlotValue(view, value) {
  renderSlotValue(view, value);
  invoke('set_slot', { channel: view.channel, index: view.index, value })
    .catch((error) => showError(errorMessage(error)));
}

function parseWeight(text) {
  const normalized = text.trim()
    .replace(/[０-９]/g, (digit) => String.fromCharCode(digit.charCodeAt(0) - 0xFEE0))
    .replace(/．/g, '.');
  if (!/^\d{1,3}(\.\d+)?$/.test(normalized)) {
    return null;
  }
  const value = Number(normalized);
  return value >= 0 && value <= 100 ? value : null;
}

function commitWeight(view) {
  const weight = parseWeight(view.weight.value);
  const current = Number(view.range.value);
  if (weight === null) {
    view.weight.value = valueToWeight(current);
    view.weight.classList.add('invalid');
    clearTimeout(view.invalidTimer);
    view.invalidTimer = setTimeout(() => view.weight.classList.remove('invalid'), 1500);
    showError('0〜100 の数値を入力してください');
    return;
  }
  const value = Math.round(weight * 255 / 100);
  if (value === current) {
    view.weight.value = valueToWeight(value);
  } else {
    setSlotValue(view, value);
  }
}

function resetSliders() {
  for (const view of slotElements.values()) {
    renderSlotValue(view, view.defaultValue);
  }
}

function localTimestamp(date) {
  const pad = (value) => String(value).padStart(2, '0');
  return `${date.getFullYear()}${pad(date.getMonth() + 1)}${pad(date.getDate())}-${pad(date.getHours())}${pad(date.getMinutes())}${pad(date.getSeconds())}`;
}

function localIsoString(date) {
  const pad = (value) => String(value).padStart(2, '0');
  const offset = -date.getTimezoneOffset();
  const sign = offset >= 0 ? '+' : '-';
  const magnitude = Math.abs(offset);
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}:${pad(date.getSeconds())}${sign}${pad(Math.floor(magnitude / 60))}:${pad(magnitude % 60)}`;
}

const expressionFilters = [{ name: '表情ファイル', extensions: ['json'] }];
elements.saveExpression.addEventListener('click', async () => {
  try {
    const folder = await invoke('expression_folder');
    const date = new Date();
    const mapName = currentMap.mapName.replace(/[<>:"/\\|?*\u0000-\u001F]/g, '_');
    const name = `${mapName}_${localTimestamp(date)}.json`;
    const path = await save({ defaultPath: `${folder}\\${name}`, filters: expressionFilters });
    if (path === null) {
      return;
    }
    const count = await invoke('save_expression', { path, savedAt: localIsoString(date) });
    showMessage(`表情を保存しました（${count} 件）`, 'info');
  } catch (error) {
    showError(errorMessage(error));
  }
});

elements.loadExpression.addEventListener('click', async () => {
  try {
    const folder = await invoke('expression_folder');
    const path = await open({ defaultPath: folder, multiple: false, filters: expressionFilters });
    if (path === null) {
      return;
    }
    const result = await invoke('apply_expression', { path });
    for (const slot of result.values) {
      const view = slotElements.get(`${slot.channel}:${slot.index}`);
      if (view) {
        renderSlotValue(view, slot.value);
      }
    }
    showMessage(result.unmatched === 0
      ? `表情を読み込みました（${result.applied} 件を適用）`
      : `表情を読み込みました（${result.applied} 件を適用、${result.unmatched} 件はこのアバターにありません）`, 'info');
  } catch (error) {
    showError(errorMessage(error));
  }
});

elements.mapSelect.addEventListener('change', () => {
  if (elements.mapSelect.value) {
    loadMap(elements.mapSelect.value);
  }
});

elements.mapRefresh.addEventListener('click', refreshMapList);
elements.mapOpen.addEventListener('click', async () => {
  try {
    const path = await open({
      multiple: false,
      filters: [{ name: 'Kaotsuki マップ', extensions: ['json'] }],
    });
    if (path) {
      await loadMap(path);
    }
  } catch (error) {
    showError(errorMessage(error));
  }
});

elements.toggle.addEventListener('click', async () => {
  try {
    await invoke('set_enabled', { enabled: elements.toggle.getAttribute('aria-checked') !== 'true' });
  } catch (error) {
    showError(errorMessage(error));
  } finally {
    try {
      renderEnabled(await invoke('get_enabled'));
    } catch (error) {
      showError(errorMessage(error));
    }
  }
});

elements.lipSync.addEventListener('click', async () => {
  try {
    await invoke('set_lip_sync', { enabled: elements.lipSync.getAttribute('aria-checked') !== 'true' });
  } catch (error) {
    showError(errorMessage(error));
  } finally {
    try {
      renderLipSync(await invoke('get_lip_sync'));
    } catch (error) {
      showError(errorMessage(error));
    }
  }
});

elements.reset.addEventListener('click', async () => {
  try {
    await invoke('reset');
    resetSliders();
  } catch (error) {
    showError(errorMessage(error));
  }
});

elements.search.addEventListener('input', () => {
  const query = elements.search.value.toLowerCase();
  const active = query.length !== 0;
  if (active && !searching) {
    for (const mesh of meshViews) {
      savedOpen.set(mesh.details, mesh.details.open);
      for (const group of mesh.groups) {
        savedOpen.set(group.details, group.details.open);
      }
    }
  }
  for (const mesh of meshViews) {
    let visible = filterRows(mesh.rows, query);
    for (const group of mesh.groups) {
      const matches = filterRows(group.rows, query);
      group.details.hidden = active && matches === 0;
      group.details.open = active ? matches > 0 : (savedOpen.get(group.details) ?? group.details.open);
      visible += matches;
    }
    mesh.details.hidden = active && visible === 0;
    if (active && visible > 0) {
      mesh.details.open = true;
    } else if (!active) {
      mesh.details.open = savedOpen.get(mesh.details) ?? mesh.details.open;
    }
  }
  if (!active) {
    savedOpen.clear();
  }
  searching = active;
});

function filterRows(rows, query) {
  let visible = 0;
  for (const view of rows) {
    const matches = view.name.toLowerCase().includes(query);
    view.row.hidden = !matches;
    if (matches) {
      visible += 1;
    }
  }
  return visible;
}

elements.settings.addEventListener('click', () => {
  elements.host.value = settings.host;
  elements.port.value = String(settings.port);
  elements.hold.value = String(settings.holdMs);
  elements.receive.checked = settings.receive;
  elements.receivePort.value = String(settings.receivePort);
  elements.settingsError.textContent = '';
  elements.dialog.showModal();
});

elements.settingsCancel.addEventListener('click', () => elements.dialog.close());
elements.settingsSave.addEventListener('click', async () => {
  const next = {
    host: elements.host.value,
    port: Number(elements.port.value),
    holdMs: Number(elements.hold.value),
    lastMapPath: settings.lastMapPath,
    receive: elements.receive.checked,
    receivePort: Number(elements.receivePort.value),
  };
  try {
    await invoke('save_settings', { settings: next });
    settings = next;
    updateTargetInfo();
    elements.dialog.close();
  } catch (error) {
    elements.settingsError.textContent = errorMessage(error);
  }
});

async function initialize() {
  try {
    await listen('send-error', (event) => showError(event.payload));
    settings = await invoke('get_settings');
    await listen('receive-status', (event) => renderReceiveStatus(event.payload));
    await listen('enabled-changed', (event) => renderEnabled(event.payload));
    await listen('lipsync-changed', (event) => renderLipSync(event.payload));
    await listen('avatar-changed', async (event) => {
      renderLipSync(false);
      const { avatarId, mapPath } = event.payload;
      if (mapPath && (!currentMap || currentMap.path !== mapPath)) {
        await loadMap(mapPath);
      } else {
        resetSliders();
        if (!mapPath) {
          showMessage(`このアバターのマップが見つかりません（${avatarId}）`, 'info');
        }
      }
    });
    renderEnabled(await invoke('get_enabled'));
    renderLipSync(await invoke('get_lip_sync'));
    updateLipSyncAvailability();
    renderReceiveStatus(await invoke('get_receive_status'));
    updateTargetInfo();
    await refreshMapList();
    const initial = await invoke('initial_map_path');
    if (initial) {
      await loadMap(initial);
    }
  } catch (error) {
    showError(errorMessage(error));
  }
}

initialize();
