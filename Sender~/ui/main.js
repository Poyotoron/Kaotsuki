const { invoke } = window.__TAURI__.core;
const { listen } = window.__TAURI__.event;
const { open } = window.__TAURI__.dialog;

const elements = {
  mapSelect: document.querySelector('#map-select'),
  mapRefresh: document.querySelector('#map-refresh'),
  mapOpen: document.querySelector('#map-open'),
  mapInfo: document.querySelector('#map-info'),
  on: document.querySelector('#btn-on'),
  off: document.querySelector('#btn-off'),
  reset: document.querySelector('#btn-reset'),
  targetInfo: document.querySelector('#target-info'),
  settings: document.querySelector('#btn-settings'),
  error: document.querySelector('#error'),
  search: document.querySelector('#search'),
  slots: document.querySelector('#slots'),
  dialog: document.querySelector('#settings-dialog'),
  host: document.querySelector('#set-host'),
  port: document.querySelector('#set-port'),
  hold: document.querySelector('#set-hold'),
  settingsError: document.querySelector('#settings-error'),
  settingsCancel: document.querySelector('#settings-cancel'),
  settingsSave: document.querySelector('#settings-save'),
};

let settings;
let currentMap = null;
let errorTimer = null;
const slotElements = new Map();
const groups = [];

function errorMessage(error) {
  return typeof error === 'string' ? error : String(error);
}

function showError(message) {
  elements.error.textContent = message;
  elements.error.hidden = false;
  if (errorTimer !== null) {
    clearTimeout(errorTimer);
  }
  errorTimer = setTimeout(() => {
    elements.error.hidden = true;
    errorTimer = null;
  }, 5000);
}

function updateTargetInfo() {
  elements.targetInfo.textContent = `送信先 ${settings.host}:${settings.port}`;
}

function setControlsEnabled(enabled) {
  elements.on.disabled = !enabled;
  elements.off.disabled = !enabled;
  elements.reset.disabled = !enabled;
  elements.search.disabled = !enabled;
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
      option.textContent = map.avatarName;
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
    settings.lastMapPath = path;
    renderSlots(map);
    elements.mapInfo.textContent = map.channelCount >= 2
      ? `${map.avatarName} — ${map.slots.length} スロット（${map.channelCount} 枠）`
      : `${map.avatarName} — ${map.slots.length} スロット`;
    setControlsEnabled(true);
    await refreshMapList();
  } catch (error) {
    showError(errorMessage(error));
  }
}

function renderSlots(map) {
  slotElements.clear();
  groups.length = 0;
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
    details.open = true;
    const summary = document.createElement('summary');
    summary.textContent = `${mesh}（${slots.length}）`;
    details.append(summary);
    const rows = document.createElement('div');
    rows.className = 'rows';
    const group = { details, rows: [] };

    for (const slot of slots) {
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

      const weight = document.createElement('span');
      weight.className = 'weight';
      weight.textContent = valueToWeight(slot.defaultValue);

      const reset = document.createElement('button');
      reset.type = 'button';
      reset.className = 'reset';
      reset.textContent = '↺';
      reset.title = '既定値に戻す';

      range.addEventListener('input', () => {
        const value = Number(range.value);
        weight.textContent = valueToWeight(value);
        invoke('set_slot', { channel: slot.channel, index: slot.index, value })
          .catch((error) => showError(errorMessage(error)));
      });
      reset.addEventListener('click', () => {
        range.value = String(slot.defaultValue);
        weight.textContent = valueToWeight(slot.defaultValue);
        invoke('set_slot', { channel: slot.channel, index: slot.index, value: slot.defaultValue })
          .catch((error) => showError(errorMessage(error)));
      });

      row.append(name, range, weight, reset);
      rows.append(row);
      const view = { row, name: slot.blendShape, range, weight, defaultValue: slot.defaultValue };
      slotElements.set(`${slot.channel}:${slot.index}`, view);
      group.rows.push(view);
    }

    details.append(rows);
    fragment.append(details);
    groups.push(group);
  }
  elements.slots.replaceChildren(fragment);
}

function valueToWeight(value) {
  return (value * 100 / 255).toFixed(1);
}

function resetSliders() {
  for (const view of slotElements.values()) {
    view.range.value = String(view.defaultValue);
    view.weight.textContent = valueToWeight(view.defaultValue);
  }
}

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

elements.on.addEventListener('click', () => {
  invoke('send_enabled', { enabled: true }).catch((error) => showError(errorMessage(error)));
});

elements.off.addEventListener('click', async () => {
  try {
    await invoke('send_enabled', { enabled: false });
    resetSliders();
  } catch (error) {
    showError(errorMessage(error));
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
  for (const group of groups) {
    let visible = 0;
    for (const view of group.rows) {
      const matches = view.name.toLowerCase().includes(query);
      view.row.hidden = !matches;
      if (matches) {
        visible += 1;
      }
    }
    group.details.hidden = visible === 0;
  }
});

elements.settings.addEventListener('click', () => {
  elements.host.value = settings.host;
  elements.port.value = String(settings.port);
  elements.hold.value = String(settings.holdMs);
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
    settings = await invoke('get_settings');
    updateTargetInfo();
    await refreshMapList();
    const initial = await invoke('initial_map_path');
    if (initial) {
      await loadMap(initial);
    }
    await listen('send-error', (event) => showError(event.payload));
  } catch (error) {
    showError(errorMessage(error));
  }
}

initialize();
