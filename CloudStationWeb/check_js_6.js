const fs = require('fs');
const acorn = require('acorn');

const content = fs.readFileSync('Views/DataAnalysis/Index.cshtml', 'utf8');

const scriptStart = content.indexOf('@section Scripts {');
const scriptEnd = content.lastIndexOf('}');
let jsCode = content.substring(scriptStart + 18, scriptEnd);

jsCode = jsCode.replace(/@\w+\(".*?"\)/g, "''"); 
jsCode = jsCode.replace(/@\([\w\.]+\)/g, "''");
jsCode = jsCode.replace(/@await [\w\.]+\(".*?"\)/g, "''");
jsCode = jsCode.replace(/@\w+/g, "''"); 
jsCode = jsCode.replace(/<script.*?>/g, '');
jsCode = jsCode.replace(/<\/script>/g, '');

try {
    acorn.parse(jsCode, { ecmaVersion: 2020 });
    console.log("Entire Script block is OK");
} catch(e) {
    console.log("Entire Script block ERROR:", e.message);
    const errLine = e.loc.line - 1;
    const fnLines = jsCode.split('\n');
    for (let i = Math.max(0, errLine - 5); i <= Math.min(fnLines.length - 1, errLine + 5); i++) {
         console.error(`${i+1}: ${fnLines[i]}`);
    }
}
