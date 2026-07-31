const repoApi = 'https://api.github.com/repos/bschlaack/Orynivo/releases/latest';

document.querySelector('.nav-toggle').addEventListener('click', (event) => {
  const nav = document.querySelector('#main-nav');
  const open = nav.classList.toggle('open');
  event.currentTarget.setAttribute('aria-expanded', String(open));
});

document.querySelectorAll('#main-nav a').forEach(link => link.addEventListener('click', () => {
  document.querySelector('#main-nav').classList.remove('open');
  document.querySelector('.nav-toggle').setAttribute('aria-expanded', 'false');
}));

const shot = document.querySelector('#main-shot');
const shotTitle = document.querySelector('#shot-title');
document.querySelectorAll('[data-shot]').forEach(button => button.addEventListener('click', () => {
  document.querySelectorAll('[data-shot]').forEach(item => {
    item.classList.remove('active');
    item.setAttribute('aria-selected', 'false');
  });
  button.classList.add('active');
  button.setAttribute('aria-selected', 'true');
  shot.animate([{opacity:.2,transform:'scale(.99)'},{opacity:1,transform:'scale(1)'}],{duration:320});
  shot.src = `/assets/screenshots/${button.dataset.shot}`;
  shot.alt = `Orynivo – ${button.dataset.title}`;
  shotTitle.textContent = button.dataset.title;
}));

function selectInstallPanel(name) {
  document.querySelectorAll('[data-install]').forEach(item => item.classList.toggle('active', item.dataset.install === name));
  document.querySelectorAll('[data-panel]').forEach(panel => panel.classList.toggle('active', panel.dataset.panel === name));
}

document.querySelectorAll('[data-install]').forEach(button => button.addEventListener('click', () => {
  selectInstallPanel(button.dataset.install);
}));

function openServerInstructions(event) {
  if (event) event.preventDefault();
  selectInstallPanel('server');
  history.replaceState(null, '', '#server-install');
  requestAnimationFrame(() => document.querySelector('#server-install').scrollIntoView({behavior:'smooth', block:'start'}));
}

document.querySelector('#server-setup-link').addEventListener('click', openServerInstructions);
if (location.hash === '#server-install') openServerInstructions();

const lightbox = document.querySelector('#lightbox');
document.querySelector('.shot-open').addEventListener('click', () => {
  lightbox.querySelector('img').src = shot.src;
  lightbox.querySelector('p').textContent = shotTitle.textContent;
  lightbox.showModal();
});
lightbox.querySelector('button').addEventListener('click', () => lightbox.close());
lightbox.addEventListener('click', event => { if (event.target === lightbox) lightbox.close(); });

function toggleLegalNote(event) {
  event.preventDefault();
  const note = document.querySelector(event.currentTarget.getAttribute('href'));
  note.hidden = !note.hidden;
  if (!note.hidden) note.scrollIntoView({behavior:'smooth',block:'nearest'});
}

document.querySelector('#contact-link').addEventListener('click', toggleLegalNote);
document.querySelector('#privacy-link').addEventListener('click', toggleLegalNote);

const observer = new IntersectionObserver(entries => entries.forEach(entry => {
  if (entry.isIntersecting) entry.target.classList.add('visible');
}), {threshold:.08});
document.querySelectorAll('.reveal').forEach(item => observer.observe(item));
document.querySelector('#year').textContent = new Date().getFullYear();

function matchesAsset(asset, link) {
  const name = asset.name;
  if (!name.includes(link.dataset.match || '')) return false;
  if (link.dataset.tail && !name.endsWith(link.dataset.tail)) return false;
  if (link.dataset.exclude && name.includes(link.dataset.exclude)) return false;
  return true;
}

fetch(repoApi, {headers:{Accept:'application/vnd.github+json'}})
  .then(response => { if (!response.ok) throw new Error('release request failed'); return response.json(); })
  .then(release => {
    document.querySelector('#release-version').textContent = release.tag_name;
    document.querySelector('#release-notes').href = release.html_url;
    document.querySelectorAll('.asset-link').forEach(link => {
      const asset = release.assets.find(item => matchesAsset(item, link));
      if (asset) {
        link.href = asset.browser_download_url;
        link.title = `${asset.name} (${(asset.size / 1024 / 1024).toFixed(1)} MB)`;
      }
    });
  })
  .catch(() => { /* Statische Links zur aktuellen Release-Seite bleiben nutzbar. */ });
