const fs = require('fs');

const content = fs.readFileSync('Views/DataAnalysis/Index.cshtml', 'utf8');
const scriptStart = content.indexOf('@section Scripts {');
const scriptEnd = content.lastIndexOf('}');
let jsCode = content.substring(scriptStart + 18, scriptEnd);
jsCode = jsCode.replace(/@\w+\(".*?"\)/g, "''").replace(/@/g, ''); 
jsCode = jsCode.replace(/<script.*?>/g, '').replace(/<\/script>/g, '');

const lines = jsCode.split('\n');

// Find function updateChart
let startIdx = -1;
for (let i = 0; i < lines.length; i++) {
   if (lines[i].includes('function updateChart(')) {
       startIdx = i;
       break;
   }
}

let endIdx = -1;
let openBrackets = 0;
for (let i = startIdx; i < lines.length; i++) {
    openBrackets += (lines[i].match(/\{/g) || []).length;
    openBrackets -= (lines[i].match(/\}/g) || []).length;
    if (openBrackets === 0) {
        endIdx = i;
        break;
    }
}

const updateChartCode = lines.slice(startIdx, endIdx + 1).join('\n');
console.log(`Checking updateChart spanning ${endIdx - startIdx} lines`);
try {
    require('acorn').parse(updateChartCode, { ecmaVersion: 2020 });
    console.log("updateChart is valid");
} catch(e) {
    console.log("ERROR in updateChart:", e.message);
    const errLine = e.loc.line - 1;
    const fnLines = updateChartCode.split('\n');
    for (let i = Math.max(0, errLine - 3); i <= Math.min(fnLines.length - 1, errLine + 3); i++) {
         console.error(`${i+1}: ${fnLines[i]}`);
    }
}

