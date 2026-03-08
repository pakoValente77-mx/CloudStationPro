const puppeteer = require('puppeteer');

(async () => {
  const browser = await puppeteer.launch({ headless: 'new' });
  const page = await browser.newPage();
  
  page.on('console', msg => console.log('PAGE LOG:', msg.text()));
  page.on('pageerror', error => console.log('PAGE ERROR:', error.message));
  page.on('requestfailed', request => {
      console.log('REQUEST FAILED:', request.url(), request.failure().errorText);
  });

  // Since we require auth, let's just make a dummy HTML with layout to exactly mimic it
  const html = `
    <!DOCTYPE html>
    <html>
    <head></head>
    <body>
        <script src="https://code.jquery.com/jquery-3.6.0.min.js"></script>
        <script src="https://code.highcharts.com/stock/highstock.js"></script>
        <script>
            try {
                console.log("Highcharts type:", typeof Highcharts);
                Highcharts.setOptions({});
                console.log("Success");
            } catch(e) {
                console.error("Caught error:", e.message);
            }
        </script>
    </body>
    </html>
  `;
  
  await page.setContent(html);
  await new Promise(r => setTimeout(r, 2000));
  await browser.close();
})();
