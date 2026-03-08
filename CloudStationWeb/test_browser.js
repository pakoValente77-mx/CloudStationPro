const puppeteer = require('puppeteer');

(async () => {
  const browser = await puppeteer.launch({ headless: 'new' });
  const page = await browser.newPage();
  
  page.on('console', msg => {
      console.log('BROWSER CONSOLE:', msg.text());
  });

  page.on('pageerror', error => {
      console.log('BROWSER ERROR:', error.message);
  });
  
  try {
      await page.goto('http://localhost:5215/DataAnalysis', { waitUntil: 'networkidle0' });
  } catch (e) {
      console.log('Navigation Error:', e.message);
  }
  
  await browser.close();
})();
