const fs = require('fs');
const acorn = require('acorn');

const content = fs.readFileSync('Views/DataAnalysis/Index.cshtml', 'utf8');
const scriptStart = content.indexOf('@section Scripts {');
if (scriptStart !== -1) {
    const scriptEnd = content.lastIndexOf('}');
    let jsCode = content.substring(scriptStart + 18, scriptEnd);
    // Remove razor syntax temporarily
    jsCode = jsCode.replace(/@/g, ''); 
    jsCode = jsCode.replace(/<script>/g, '');
    jsCode = jsCode.replace(/<\/script>/g, '');
    try {
        acorn.parse(jsCode, { ecmaVersion: 2020 });
        console.log(`Main Script OK`);
    } catch (e) {
        console.log(`Main Script ERROR: ${e.message}`);
        const lines = jsCode.split('\n');
        const errLine = e.loc.line - 1;
        console.log('--- ERROR CONTEXT ---');
        for (let i = Math.max(0, errLine - 3); i <= Math.min(lines.length - 1, errLine + 3); i++) {
            console.log(`${i+1}: ${lines[i]}`);
        }
        console.log('---------------------');
    }
}
