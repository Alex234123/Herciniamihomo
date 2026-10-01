const fs = require('fs');
const content = fs.readFileSync('D:/DL/Herciniamihomo/data/config.yaml', 'utf-8');
const lines = content.split('\n');
const line = lines[738]; // line 739
console.log('Line 739 text:', JSON.stringify(line));
console.log('Hex bytes:');
console.log(Buffer.from(line).toString('hex'));
