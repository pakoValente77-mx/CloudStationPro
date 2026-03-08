const fs = require('fs');
const acorn = require('acorn');

const content = fs.readFileSync('Views/DataAnalysis/Index.cshtml', 'utf8');
const scriptRegex = /<script>([\s\S]*?)<\/script>/gi;
let match;
let count = 0;

while ((match = scriptRegex.exec(content)) !== null) {
    count++;
    const jsCode = match[1];
    try {
        acorn.parse(jsCode, { ecmaVersion: 2020 });
        console.log(`Script tag ${count} OK`);
    } catch (e) {
        console.log(`Script tag ${count} ERROR: ${e.message}`);
        
        // Print the lines around the error
        const lines = jsCode.split('\n');
        const errLine = e.loc.line - 1;
        console.log('--- ERROR CONTEXT ---');
        for (let i = Math.max(0, errLine - 3); i <= Math.min(lines.length - 1, errLine + 3); i++) {
            console.log(`${i+1}: ${lines[i]}`);
        }
        console.log('---------------------');
    }
}
