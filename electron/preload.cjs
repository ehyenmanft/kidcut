const { contextBridge } = require('electron');

contextBridge.exposeInMainWorld('kidcutDesktop', {
  isDesktop: true,
  version: '1.0.0',
  platform: process.platform
});
