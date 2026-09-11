const assert = require('node:assert/strict');
const fs = require('node:fs'), path = require('node:path'), vm = require('node:vm');
const {readDesktop, root} = require('./localization-data.cjs');
const data = readDesktop(), keys = [...data.parameters, ...data.properties].sort();
const placeholders = text => [...text.matchAll(/\{\d+(?:[^{}]*)\}/g)].map(m => m[0]).sort();
for (const [language, {values}] of Object.entries(data.result)) {
  assert.deepEqual(Object.keys(values).sort(), keys, language + ': key coverage');
  for (const key of keys) {
    assert.ok(values[key].trim(), language + ': empty ' + key);
    assert.deepEqual(placeholders(values[key]), placeholders(data.result.English.values[key]), language + ': placeholders ' + key);
  }
}
assert.ok(!data.source.includes('English with'), 'No language may inherit English resources');
const web = require('../html/i18n.js');
web.validateTranslations();
assert.equal(Object.keys(web.languageUrls).length, Object.keys(data.result).length, 'Desktop/website locale parity');
const profileSource = fs.readFileSync(path.join(root, 'Orynivo.Core/Library/ArtistProfileService.cs'), 'utf8');
const profileLanguageLine = profileSource.split('\n').find(line => line.includes('language = language is'));
for (const locale of Object.keys(web.languageUrls))
  assert.ok(profileLanguageLine.includes('"' + locale + '"'), locale + ': artist profile language support');
const mobile = JSON.parse(fs.readFileSync(path.join(root, 'Orynivo/Localization/MobileRemote.json'), 'utf8'));
for (const [language, values] of Object.entries(mobile)) {
  assert.deepEqual(Object.keys(values).sort(), Object.keys(mobile.en).sort(), language + ': mobile key coverage');
  assert.ok(Object.values(values).every(value => value.trim()), language + ': mobile empty value');
}
assert.ok(mobile.hi, 'Hindi mobile resources must exist');
const script = fs.readFileSync(path.join(root, 'html/i18n.js'), 'utf8');
for (const language of Object.keys(web.languageUrls)) {
  const buttons = Object.keys(web.languageUrls).map(l => ({
    dataset:{language:l}, classList:{toggle(){}}, setAttribute(){}, addEventListener(){}
  }));
  const shots = Array.from({length:9}, () => ({dataset:{}})), title = {};
  const document = {
    body:{dataset:{pageLanguage:language}},
    querySelectorAll: s => s === '[data-language]' ? buttons : shots,
    querySelector: s => s === '[data-shot].active' ? shots[0] : title
  };
  vm.runInNewContext(script, {document});
  assert.deepEqual(shots.map(s => s.dataset.title), web.galleryTitles[language]);
  assert.equal(title.textContent, web.galleryTitles[language][0]);
  const html = fs.readFileSync(path.join(root, 'html', language === 'en' ? '' : language, 'index.html'), 'utf8');
  assert.ok(html.includes('data-page-language="' + language + '"'));
  for (const locale of Object.keys(web.languageUrls)) {
    assert.ok(html.includes('data-language="' + locale + '"'), language + ': language selector ' + locale);
    assert.ok(html.includes('hreflang="' + locale + '"'), language + ': alternate link ' + locale);
  }
  assert.ok(!html.includes('undefined'), language + ': undefined metadata');
  for (const t of web.galleryTitles[language]) assert.ok(html.includes('data-title="' + t + '"'), language + ': static gallery ' + t);
}
console.log('PASS: ' + Object.keys(data.result).length + ' desktop locales x ' + keys.length + ' keys, placeholders, website selectors/metadata/gallery and mobile resource coverage.');

