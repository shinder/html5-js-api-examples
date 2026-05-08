const CACHE = "html5-pwa-v1";
const ASSETS = [
  "./N01-pwa-basic.html",
  "./N02-pwa-service-worker.html",
  "./N03-pwa-offline.html",
  "./N04-pwa-install.html",
  "./manifest.webmanifest",
  "./icons/icon.svg",
];

self.addEventListener("install", (event) => {
  // 預先把資源放進快取
  event.waitUntil(caches.open(CACHE).then((c) => c.addAll(ASSETS)));
});

self.addEventListener("activate", (event) => {
  // 清掉舊版快取
  event.waitUntil(
    caches.keys().then((keys) =>
      Promise.all(keys.filter((k) => k !== CACHE).map((k) => caches.delete(k)))
    )
  );
});

self.addEventListener("fetch", (event) => {
  const url = new URL(event.request.url);

  // 只接管 PWA 相關資源；其他頁面（101–G02 等）走預設網路行為
  const isPwaResource =
    /\/N0\d/.test(url.pathname) ||
    url.pathname.endsWith("/manifest.webmanifest") ||
    url.pathname.includes("/icons/");

  if (!isPwaResource) return;

  // cache-first：先看快取、否則打網路
  event.respondWith(
    caches.match(event.request).then((cached) => cached || fetch(event.request))
  );
});
