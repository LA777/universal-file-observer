const { env } = require('process');

const target = env.ASPNETCORE_HTTPS_PORT ? `https://localhost:${env.ASPNETCORE_HTTPS_PORT}` :
  env.ASPNETCORE_URLS ? env.ASPNETCORE_URLS.split(';')[0] : 'https://localhost:7150';

const PROXY_CONFIG = [
  {
    context: [
      "/api/auth",
      "/api/user",
      "/api/weatherforecast",
      "/api/snapshot",
      "/api/filesystem",
      "/api/label",
      "/api/labels",
      "/api/search"
    ],
    target,
    secure: false
  }
]

module.exports = PROXY_CONFIG;
