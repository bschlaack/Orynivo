// Shared source reader for localization validation and migration tools.
const fs = require('node:fs');
const path = require('node:path');
const root = path.join(__dirname, '..');
const literal = '"(?:\\\\.|[^"\\\\])*"';
function readDesktop() {
  const model = fs.readFileSync(path.join(root, 'Orynivo/Localization/Strings.cs'), 'utf8');
  const source = fs.readFileSync(path.join(root, 'Orynivo/Localization/LocalizationManager.cs'), 'utf8');
  const parameters = [...model.split(')')[0].matchAll(/string (\w+)/g)].map(m => m[1]);
  const properties = [...model.matchAll(/public string (\w+) \{ get; init; \}/g)].map(m => m[1]);
  const result = {};
  for (const name of ['German', 'English', 'French', 'Spanish', 'Russian', 'ChineseSimplified']) {
    const start = source.indexOf(`private static readonly LocalizedStrings ${name} = `);
    const end = source.indexOf('\n    };', start) + 7;
    const block = source.slice(start, end);
    const values = {};
    if (block.includes('= new(')) {
      const ctor = block.slice(block.indexOf('= new(') + 6, block.indexOf('\n    {'));
      const args = [...ctor.matchAll(new RegExp(literal, 'g'))].map(m => JSON.parse(m[0]));
      if (args.length !== parameters.length) throw new Error(`${name}: ${args.length} constructor arguments / ${parameters.length} parameters`);
      parameters.forEach((p, i) => values[p] = args[i]);
    }
    for (const m of block.matchAll(new RegExp('(\\w+)\\s*=\\s*(' + literal + ')', 'g'))) values[m[1]] = JSON.parse(m[2]);
    result[name] = { values, block, start, end };
  }
  return { source, parameters, properties, result };
}
module.exports = { readDesktop, root };
