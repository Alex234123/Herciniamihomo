const fs = require('fs');

const content = fs.readFileSync('D:/DL/Herciniamihomo/data/config.yaml', 'utf-8');
const lines = content.split('\n');

let inProxies = false;
let proxyCount = 0;
let currentProxy = [];

for (let i = 0; i < lines.length; i++) {
  const line = lines[i];
  if (/^proxies:\s*$/.test(line)) {
    inProxies = true;
    continue;
  }
  if (inProxies && /^[a-zA-Z0-9_\-]+:\s*$/.test(line)) {
    break;
  }
  if (inProxies) {
    if (/^\s*-\s+name:/.test(line)) {
      proxyCount++;
      if (proxyCount === 24 || proxyCount === 25) {
        console.log(`\n=== PROXY ${proxyCount} (line ${i+1}) ===`);
      }
    }
    if (proxyCount === 24 || proxyCount === 25) {
      console.log(line);
    }
  }
}
