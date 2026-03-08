const puppeteer = require('puppeteer');

(async () => {
  const browser = await puppeteer.launch({ headless: 'new' });
  const page = await browser.newPage();
  
  // We need to bypass auth or login
  // Best way to get the exact HTML is to just read the dev server response if it wasn't auth
  // But since there is auth, let's login
  await page.goto('http://localhost:5215/Account/Login', { waitUntil: 'networkidle0' });
  await page.type('#username', 'admin'); // guess, or maybe just look at how it reacts
  // Actuallly, auth redirects to login. Let's get the JS out of the controller maybe?
  // Wait, the API returns the HTML without login if we use the backend directly? No.
  
  // Let's just try to grab the layout and the view and combine them to accurately count lines!
  await browser.close();
})();
