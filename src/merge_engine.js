const fs = require('fs');
const path = require('path');
const https = require('https');
const http = require('http');

/**
 * Herciniamihomo 配置合并与聚合订阅引擎
 * 支持从多个订阅链接 / 本地 yaml 文件抓取节点并合并去重
 */

function fetchUrl(url) {
  return new Promise((resolve, reject) => {
    const client = url.startsWith('https') ? https : http;
    client.get(url, { headers: { 'User-Agent': 'ClashMi/1.0.30' } }, (res) => {
      let data = '';
      res.on('data', chunk => data += chunk);
      res.on('end', () => resolve(data));
    }).on('error', err => reject(err));
  });
}

function parseYamlProxies(yamlText) {
  // 基础节点提取与正则解析
  const proxies = [];
  const lines = yamlText.split('\n');
  let inProxies = false;
  let currentProxyLines = [];

  for (let line of lines) {
    if (/^proxies:\s*$/.test(line)) {
      inProxies = true;
      continue;
    }
    if (inProxies && /^[a-zA-Z0-9_\-]+:\s*$/.test(line)) {
      break;
    }
    if (inProxies) {
      if (/^\s*-\s+name:\s*/.test(line)) {
        if (currentProxyLines.length > 0) {
          proxies.push(currentProxyLines.join('\n'));
          currentProxyLines = [];
        }
      }
      if (line.trim().length > 0) {
        currentProxyLines.push(line);
      }
    }
  }
  if (currentProxyLines.length > 0) {
    proxies.push(currentProxyLines.join('\n'));
  }
  return proxies;
}

async function mergeProfiles(sources, outputPath) {
  console.log('开始合并配置源:', sources);
  const allProxies = [];
  const proxyNames = new Set();

  for (let src of sources) {
    try {
      let content = '';
      if (src.startsWith('http://') || src.startsWith('https://')) {
        content = await fetchUrl(src);
      } else if (fs.existsSync(src)) {
        content = fs.readFileSync(src, 'utf-8');
      }

      const proxies = parseYamlProxies(content);
      for (let p of proxies) {
        const match = p.match(/name:\s*["']?([^"'\n\r]+)["']?/);
        if (match) {
          let name = match[1].trim();
          if (!proxyNames.has(name)) {
            proxyNames.add(name);
            allProxies.push(p);
          }
        }
      }
    } catch (e) {
      console.error('解析配置源失败:', src, e.message);
    }
  }

  console.log(`成功合并 ${allProxies.length} 个去重代理节点`);
}

module.exports = { mergeProfiles };
