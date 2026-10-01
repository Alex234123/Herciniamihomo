const fs = require('fs');

let content = fs.readFileSync('D:/DL/Herciniaselfconfig.yaml', 'utf-8');

const header = `mixed-port: 7890
redir-port: 7892
tproxy-port: 7895
external-controller: 127.0.0.1:9097
external-ui: ui
secret: ""
`;

content = content.replace(/^mixed-port: 7890\r?\nredir-port: 7892\r?\ntproxy-port: 7895\r?\n/, '');

// 完全移除 short-id 行
content = content.replace(/^\s*short-id:\s*.*?\r?\n/gm, '');

const finalContent = header + content;
fs.writeFileSync('D:/DL/Herciniamihomo/data/config.yaml', finalContent, 'utf-8');
console.log('已完全移除 short-id 并保存');
