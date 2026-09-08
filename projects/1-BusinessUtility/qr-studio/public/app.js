'use strict';

const form = document.getElementById('qr-form');
const urlInput = document.getElementById('url');
const labelInput = document.getElementById('label');
const sizeInput = document.getElementById('size');
const eccInput = document.getElementById('ecc');
const formError = document.getElementById('form-error');

const preview = document.getElementById('preview');
const previewUrl = document.getElementById('preview-url');
const saveBtn = document.getElementById('save-btn');
const pngBtn = document.getElementById('png-btn');
const svgBtn = document.getElementById('svg-btn');

const libraryList = document.getElementById('library-list');
const libraryEmpty = document.getElementById('library-empty');
const libraryCount = document.getElementById('library-count');
const toast = document.getElementById('toast');

/** The URL of the code currently on screen, or null when there is nothing to save. */
let current = null;
let previewToken = 0;
let toastTimer = null;

function currentOptions() {
  return { ecc: eccInput.value, size: sizeInput.value, label: labelInput.value.trim() };
}

function showToast(message) {
  toast.textContent = message;
  toast.hidden = false;
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => { toast.hidden = true; }, 2600);
}

function showError(message) {
  formError.textContent = message;
  formError.hidden = !message;
}

function clearPreview(placeholder) {
  current = null;
  preview.dataset.state = 'empty';
  preview.innerHTML = `<p class="placeholder">${placeholder}</p>`;
  previewUrl.textContent = '';
  saveBtn.disabled = true;
  pngBtn.hidden = true;
  svgBtn.hidden = true;
}

function downloadHref(format) {
  const { ecc, size, label } = currentOptions();
  const params = new URLSearchParams({ url: current, format, ecc, size });
  if (label) params.set('label', label);
  return `/api/download?${params}`;
}

async function requestJson(url, options) {
  const response = await fetch(url, options);
  const body = await response.json().catch(() => ({}));
  if (!response.ok) throw new Error(body.error ?? `Request failed (${response.status})`);
  return body;
}

async function refreshPreview() {
  const raw = urlInput.value.trim();
  if (!raw) {
    showError('');
    clearPreview('Type a URL to see its QR code.');
    return;
  }

  const token = ++previewToken;
  preview.dataset.state = 'loading';

  const { ecc, size } = currentOptions();
  const params = new URLSearchParams({ url: raw, ecc, size });

  try {
    const data = await requestJson(`/api/qr?${params}`);
    if (token !== previewToken) return; // a newer keystroke already won

    current = data.url;
    preview.dataset.state = 'ready';
    preview.innerHTML = data.svg;
    previewUrl.textContent = data.url;
    saveBtn.disabled = false;
    pngBtn.href = downloadHref('png');
    svgBtn.href = downloadHref('svg');
    pngBtn.hidden = false;
    svgBtn.hidden = false;
    showError('');
  } catch (error) {
    if (token !== previewToken) return;
    clearPreview('No code yet.');
    showError(error.message);
  }
}

function libraryItem(entry) {
  const item = document.createElement('li');
  item.className = 'library-item';

  const img = document.createElement('img');
  img.src = `/library/${entry.files.png}`;
  img.alt = `QR code for ${entry.url}`;
  img.loading = 'lazy';

  const name = document.createElement('p');
  name.className = 'name';
  name.textContent = entry.label || entry.id;

  const meta = document.createElement('p');
  meta.className = 'meta';
  meta.textContent = `${entry.url} — ${entry.size} px, ECC ${entry.ecc}`;

  const row = document.createElement('div');
  row.className = 'row';

  const png = document.createElement('a');
  png.className = 'button secondary';
  png.href = `/library/${entry.files.png}`;
  png.download = entry.files.png;
  png.textContent = 'PNG';

  const svg = document.createElement('a');
  svg.className = 'button secondary';
  svg.href = `/library/${entry.files.svg}`;
  svg.download = entry.files.svg;
  svg.textContent = 'SVG';

  const del = document.createElement('button');
  del.type = 'button';
  del.className = 'secondary link-danger';
  del.textContent = 'Delete';
  del.addEventListener('click', () => deleteEntry(entry));

  row.append(png, svg, del);
  item.append(img, name, meta, row);
  return item;
}

async function refreshLibrary() {
  try {
    const entries = await requestJson('/api/library');
    libraryList.replaceChildren(...entries.map(libraryItem));
    libraryEmpty.hidden = entries.length > 0;
    libraryCount.textContent = entries.length
      ? `${entries.length} saved ${entries.length === 1 ? 'code' : 'codes'} in library/`
      : '';
  } catch (error) {
    showError(error.message);
  }
}

async function deleteEntry(entry) {
  if (!window.confirm(`Delete ${entry.files.png} and ${entry.files.svg}?`)) return;
  try {
    await requestJson(`/api/library/${encodeURIComponent(entry.id)}`, { method: 'DELETE' });
    showToast('Deleted');
    await refreshLibrary();
  } catch (error) {
    showError(error.message);
  }
}

saveBtn.addEventListener('click', async () => {
  if (!current) return;
  const { ecc, size, label } = currentOptions();

  saveBtn.disabled = true;
  try {
    const entry = await requestJson('/api/library', {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ url: current, label, ecc, size }),
    });
    showToast(`Saved library/${entry.files.png}`);
    await refreshLibrary();
  } catch (error) {
    showError(error.message);
  } finally {
    saveBtn.disabled = !current;
  }
});

form.addEventListener('submit', (event) => {
  event.preventDefault();
  refreshPreview();
});

let debounce = null;
urlInput.addEventListener('input', () => {
  clearTimeout(debounce);
  debounce = setTimeout(refreshPreview, 250);
});

for (const input of [sizeInput, eccInput]) {
  input.addEventListener('change', refreshPreview);
}

labelInput.addEventListener('input', () => {
  if (!current) return;
  pngBtn.href = downloadHref('png');
  svgBtn.href = downloadHref('svg');
});

refreshLibrary();
