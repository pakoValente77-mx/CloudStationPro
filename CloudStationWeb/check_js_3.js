const fs = require('fs');
const acorn = require('acorn');

const content = fs.readFileSync('Views/DataAnalysis/Index.cshtml', 'utf8');

// The error mentioned line 2 of the script tag which was the script src.
// Let's find exactly which script block is causing the error.
const scriptRegex = /<script.*?>([\s\S]*?)<\/script>/gi;
let match;
let count = 0;

while ((match = scriptRegex.exec(content)) !== null) {
    count++;
    const jsCode = match[1].trim();
    if (jsCode.length === 0) continue; // Skip empty scripts like <script src="..."></script>
    
    // We are looking for the content inside @section Scripts {
    if (jsCode.includes('analysisData')) {
         // This is our main block, but it has razor syntax. We need to strip it to test.
         let cleanCode = jsCode.replace(/@\w+\(".*?"\)/g, "''"); // Replace @ViewData["Title"]
         cleanCode = cleanCode.replace(/@/g, ''); // Naively strip remaining @ 
         
         try {
             acorn.parse(cleanCode, { ecmaVersion: 2020 });
             console.log(`Main Analysis Script OK`);
         } catch (e) {
             console.log(`Main Analysis Script ERROR: ${e.message}`);
             const lines = cleanCode.split('\n');
             const errLine = e.loc.line - 1;
             console.log('--- ERROR CONTEXT ---');
             for (let i = Math.max(0, errLine - 3); i <= Math.min(lines.length - 1, errLine + 3); i++) {
                 console.log(`${i+1}: ${lines[i]}`);
             }
             console.log('---------------------');
         }
    }
}
